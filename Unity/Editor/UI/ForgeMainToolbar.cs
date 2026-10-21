using System;
using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;

namespace BuildForge.Editor.UI
{
    /// <summary>
    /// Build Profile and apply dropdowns and Build buttons in Unity's main toolbar
    /// (the MainToolbarElement API, Unity 6.3+), one element that the user places.
    /// Picking a profile activates it, like the build window's Activate. The
    /// second dropdown shows what is applied to the active profile, the state as
    /// its icon and the variant as text, and applies it like the window's Apply: it lists
    /// the variants when the project has any and offers Apply or Re-apply when it
    /// has none. Revert to Baseline follows whenever anything is applied, as in
    /// the window. Build and Build and Run build the active profile with the
    /// window's variant, like the window's Build; Build and Run also runs the
    /// player, like Unity's Build And Run.
    /// Unity hides package toolbar elements until the user shows them; this one is
    /// shown by default, hidden with Unity's own toolbar menu (right-click > Hide,
    /// or the ⋮ menu), and the choice is kept per user in UserSettings
    /// (<see cref="ForgeUserPreferences"/>). See ARCHITECTURE.md "Main Toolbar".
    /// </summary>
    [InitializeOnLoad]
    static class ForgeMainToolbar
    {
        // Also the name in Unity's toolbar menu and drag tooltip.
        internal const string ElementPath = "Build Forge";
        const double PollSeconds = 1.0;

        static EditorWindow s_ToolbarWindow;
        static Overlay s_Overlay;
        static double s_NextPoll;
        static string s_StateKey;

        static ForgeMainToolbar()
        {
            // Batch mode (CI, test runs) has no main toolbar.
            if (Application.isBatchMode)
                return;
            EditorApplication.update += Update;
            // A Build Forge profile created, deleted or edited for the active Build
            // Profile changes whether the apply dropdown shows.
            EditorApplication.projectChanged += () => s_StateKey = null;
        }

        static void Update()
        {
            if (EditorApplication.timeSinceStartup < s_NextPoll)
                return;
            s_NextPoll = EditorApplication.timeSinceStartup + PollSeconds;
            TrackOverlay();
            // The labels show state that changes outside Build Forge too (Unity's
            // own Switch Profile, Project Settings, drift), so compare a key of it.
            var key = StateKey();
            if (key == s_StateKey)
                return;
            s_StateKey = key;
            MainToolbar.Refresh(ElementPath);
        }

        /// <summary>
        /// Finds this element's overlay in the toolbar window (its id is the element
        /// path, the lookup MainToolbar.Refresh itself uses), applies the recorded
        /// visibility to each new overlay instance (Unity starts package elements
        /// hidden, and a layout reset recreates them), and records later Hide or
        /// Show from Unity's toolbar menu.
        /// </summary>
        static void TrackOverlay()
        {
            if (s_ToolbarWindow != null && s_ToolbarWindow.TryGetOverlay(ElementPath, out var current) && current == s_Overlay)
                return;
            s_ToolbarWindow = null;
            s_Overlay = null;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (!window.TryGetOverlay(ElementPath, out var overlay))
                    continue;
                s_ToolbarWindow = window;
                s_Overlay = overlay;
                overlay.displayed = ForgeUserPreferences.instance.ShowMainToolbar;
                overlay.displayedChanged += displayed => ForgeUserPreferences.instance.ShowMainToolbar = displayed;
                return;
            }
        }

        static string StateKey()
        {
            var active = BuildProfile.GetActiveBuildProfile();
            var applied = ForgeEditorState.AppliedBuildProfile;
            ForgeEditorState.CanMutate(out var reason);
            return string.Join("|",
                active != null ? AssetDatabase.GetAssetPath(active) : "platform:" + EditorUserBuildSettings.activeBuildTarget,
                !ForgeEditorState.HasAppliedGuid ? "" : applied != null ? AssetDatabase.GetAssetPath(applied) : "missing",
                ForgeEditorState.AppliedVariant ?? "",
                // Drift shows only on the active profile's label. GetDrift captures
                // the applied plugins' settings at most every two seconds.
                active != null && applied == active ? string.Join(",", ForgeEditorState.GetDrift()) : "",
                string.Join(",", ForgeSettings.instance.BuildVariants),
                // The Build buttons' tooltip names the window's variant.
                ForgeBuildWindow.SessionVariant ?? "",
                // Whether the Build buttons are enabled: the variant rules and the active
                // profile's Allowed Variants are edited without a projectChanged.
                active != null ? BuildVariants.ProfileError(ForgeProfileEmbedding.Get(active), ForgeBuildWindow.SessionVariant) ?? "" : "",
                // The Build buttons' icons come in a version per editor theme.
                EditorGUIUtility.isProSkin ? "dark" : "light",
                reason ?? "");
        }

        // Placement is left to Unity's default dock position and the user (Ctrl+drag).
        [MainToolbarElement(ElementPath)]
        static IEnumerable<MainToolbarElement> CreateElements()
        {
            var state = State.Capture();
            yield return new MainToolbarDropdown(ProfileContent(state), ShowProfileMenu)
            {
                enabled = state.CanMutate,
            };
            // Shown while its menu offers something: applying the active profile
            // (as the window enables Apply) or Revert to Baseline.
            yield return new MainToolbarDropdown(ApplyContent(state), ShowApplyMenu)
            {
                enabled = state.CanMutate,
                displayed = ApplyEntriesFor(state).Count > 0,
            };
            foreach (var run in new[] { false, true })
            {
                var button = BuildButtonFor(run, state);
                yield return new MainToolbarButton(new MainToolbarContent(button.Label, ForgeBuildWindow.BuildIcon(run), button.Tooltip), () => Build(run))
                {
                    enabled = button.Enabled,
                };
            }
        }

        /// <summary>What the dropdowns show, read fresh whenever they are built or opened.</summary>
        sealed class State
        {
            public List<ForgeProfile> All;
            public BuildProfile Active;
            public ForgeProfile ActiveForgeProfile;
            public string ResolveError;
            /// <summary>The active profile is the applied one.</summary>
            public bool Applied;
            /// <summary>Something is applied: the active profile or another.</summary>
            public bool AnythingApplied;
            public bool CanMutate;
            public string MutateReason;

            public static State Capture()
            {
                var state = new State { All = ForgeProfileLookup.FindAll(), Active = BuildProfile.GetActiveBuildProfile() };
                if (state.Active != null)
                    state.ActiveForgeProfile = ForgeProfileLookup.ResolveForBuildProfile(state.Active, state.All, out state.ResolveError);
                state.Applied = state.ActiveForgeProfile != null && ForgeEditorState.IsApplied(state.ActiveForgeProfile);
                state.AnythingApplied = ForgeEditorState.HasAppliedGuid;
                state.CanMutate = ForgeEditorState.CanMutate(out state.MutateReason);
                return state;
            }
        }

        static MainToolbarContent ProfileContent(State state)
        {
            var target = state.Active != null ? BuildProfileUtility.GetBuildTarget(state.Active) : EditorUserBuildSettings.activeBuildTarget;
            string tooltip;
            if (!state.CanMutate)
                tooltip = state.MutateReason;
            else if (state.Active == null)
                tooltip = $"Activate a Build Profile with Build Forge settings. No Unity Build Profile is active; Unity is on the {target} platform profile.";
            else if (state.ActiveForgeProfile == null)
                tooltip = $"Activate a Build Profile with Build Forge settings. {state.ResolveError}";
            else
                // Worded like the window's Activate tooltip.
                tooltip = "Activate a Build Profile with Build Forge settings, making it the active one. Another profile's " +
                          "applied plugin settings are reverted to baseline first. Apply the profile afterwards so Play Mode matches it.";
            // The profile's own platform icon, Meta Quest's for a Meta Quest profile, as in the build window.
            var icon = state.Active != null ? ForgeBuildWindow.PlatformIcon(state.Active) : ForgeBuildWindow.PlatformIcon(target);
            return new MainToolbarContent(ProfileText(state.Active != null ? state.Active.name : null), icon as Texture2D, tooltip);
        }

        static MainToolbarContent ApplyContent(State state)
        {
            var appliedVariant = ForgeEditorState.AppliedVariant;
            var variantsConfigured = ForgeSettings.instance.BuildVariants.Count > 0;
            var drift = state.Applied ? ForgeEditorState.GetDrift() : Array.Empty<string>();
            string tooltip;
            if (!state.CanMutate)
                tooltip = state.MutateReason;
            else if (state.ActiveForgeProfile != null && ForgePluginRegistry.BlockingError(state.ActiveForgeProfile) is string blocking)
                // Apply stops for it, as builds do; the menu lists the entries unavailable.
                tooltip = blocking;
            else if (drift.Count > 0)
                // The window's drift warning, and how the toolbar re-applies.
                tooltip = $"Editor settings differ from '{state.ActiveForgeProfile.DisplayName}' as applied: " +
                          string.Join(", ", drift.Select(ForgeBuildWindow.DescribeDriftKey)) + ". " +
                          (!variantsConfigured ? "Re-apply" : appliedVariant != null ? "Re-apply (pick the checked variant)" : "Applying a variant") +
                          " restores the profile's state; Revert to Baseline undoes it.";
            else if (state.Applied)
                tooltip = variantsConfigured
                    ? (appliedVariant == null
                        ? "Applied without a variant. Pick a variant to apply the profile with it, or Revert to Baseline."
                        : $"Applied with variant '{appliedVariant}'. " +
                          "Pick another variant, re-apply this one after editing the profile, or Revert to Baseline.")
                    : "Plugin settings applied. Re-apply after editing the profile, or Revert to Baseline.";
            else if (state.AnythingApplied)
            {
                // Another profile's settings are applied: the window's warning for that.
                var unfinished = ForgeEditorState.InterruptedError();
                tooltip = (ForgeEditorState.GetActiveMismatch(out _) ?? ForgeEditorState.UnresolvedAppliedWarning)
                          + (unfinished != null ? " " + unfinished : "");
            }
            else
                tooltip = variantsConfigured
                    ? "Pick a variant to apply the profile with it: its defines and the profile's plugin settings, so Play Mode matches."
                    : "Apply the profile's plugin settings (such as OpenXR features), so Play Mode matches.";
            var text = ApplyText(state.Applied, appliedVariant);
            var icon = ApplyIcon(state.Applied, drift.Count > 0, state.AnythingApplied);
            return icon != null
                ? new MainToolbarContent(text, EditorGUIUtility.IconContent(icon).image as Texture2D, tooltip)
                : new MainToolbarContent(text, tooltip);
        }

        /// <summary>
        /// The profile dropdown's label: the active Unity Build Profile's name (the
        /// profile's identity under the 1:1 rule), or "Platform profile" when one of
        /// those is active instead; the icon shows its platform. Unity 6.6 cuts
        /// toolbar dropdown text at 120 px.
        /// </summary>
        internal static string ProfileText(string activeBuildProfileName)
            => activeBuildProfileName ?? "Platform profile";

        /// <summary>
        /// The apply dropdown's label: "Not applied", or what is applied, with the
        /// state shown by <see cref="ApplyIcon"/>: the applied variant, or "Applied"
        /// without one. Only that much is text because Unity 6.6 cuts toolbar
        /// dropdown text at 120 px ("Applied (Development)" ended in an ellipsis there).
        /// </summary>
        internal static string ApplyText(bool applied, string appliedVariant)
        {
            if (!applied)
                return "Not applied";
            return appliedVariant ?? "Applied";
        }

        /// <summary>
        /// The apply dropdown's icon: a check mark while the active profile is applied,
        /// Unity's warning icon once it has drifted, and also while another profile's
        /// settings are applied underneath it ("Not applied" for the active profile,
        /// after Unity switched profiles without Build Forge); none while nothing is applied.
        /// </summary>
        internal static string ApplyIcon(bool applied, bool drifted, bool anythingApplied)
            => applied ? (drifted ? "console.warnicon.sml" : "GreenCheckmark")
                : anythingApplied ? "console.warnicon.sml" : null;

        internal readonly struct ProfileEntry
        {
            public readonly string Label;
            public readonly bool Enabled;
            public readonly bool Checked;
            public readonly ForgeProfile Profile;

            public ProfileEntry(string label, bool enabled, bool isChecked, ForgeProfile profile)
            {
                Label = label;
                Enabled = enabled;
                Checked = isChecked;
                Profile = profile;
            }
        }

        /// <summary>
        /// The profile menu, sorted by platform then name like the build window, with
        /// profiles that have no Unity Build Profile last: one entry per Build
        /// Forge profile, the active one checked. Profiles that cannot be activated
        /// (no Unity Build Profile, or one shared with another profile) are listed
        /// disabled with the reason; nothing is enabled while mutating is refused.
        /// </summary>
        internal static List<ProfileEntry> ProfileEntries(IReadOnlyList<ForgeProfile> all, BuildProfile active, bool canMutate)
        {
            var entries = new List<ProfileEntry>();
            foreach (var profile in all.Where(p => p != null)
                         .OrderBy(p => p.BuildProfile == null)
                         .ThenBy(p => p.BuildProfile != null ? ForgeBuildWindow.PlatformName(p.BuildProfile) : "", StringComparer.OrdinalIgnoreCase)
                         .ThenBy(p => p.DisplayName))
            {
                var isActive = profile.BuildProfile != null && profile.BuildProfile == active;
                var problem = profile.BuildProfile == null ? "no Unity Build Profile"
                    : ForgeProfileLookup.SharedBuildProfileError(profile, all) != null ? "shares its Unity Build Profile" : null;
                entries.Add(new ProfileEntry(problem == null ? profile.DisplayName : $"{profile.DisplayName} ({problem})",
                    canMutate && problem == null && !isActive, isActive, profile));
            }
            return entries;
        }

        internal readonly struct ApplyEntry
        {
            public readonly string Label;
            public readonly bool Enabled;
            public readonly bool Checked;
            /// <summary>Revert to Baseline instead of applying <see cref="Variant"/>; drawn after a separator.</summary>
            public readonly bool Revert;
            public readonly string Variant;

            public ApplyEntry(string label, bool enabled, bool isChecked, bool revert, string variant)
            {
                Label = label;
                Enabled = enabled;
                Checked = isChecked;
                Revert = revert;
                Variant = variant;
            }
        }

        /// <summary>
        /// The apply menu. While the active profile can be applied
        /// (<paramref name="canApply"/>), with variants: each configured variant,
        /// the applied one checked; picking the checked one re-applies it, and a
        /// variant the active profile cannot take
        /// (<paramref name="errorFor"/> returns why) is listed disabled, as in the
        /// build window. Without variants: Apply, or Re-apply once applied.
        /// Revert to Baseline follows whenever anything is applied, the active
        /// profile or another, as in the window. Nothing is enabled while mutating
        /// is refused.
        /// </summary>
        internal static List<ApplyEntry> ApplyEntries(bool canApply, IReadOnlyList<string> variants, Func<string, string> errorFor,
            bool applied, string appliedVariant, bool anythingApplied, bool canMutate)
        {
            var entries = new List<ApplyEntry>();
            if (canApply && (variants == null || variants.Count == 0))
            {
                var label = applied ? "Re-apply" : "Apply";
                var available = errorFor(null) == null;
                entries.Add(new ApplyEntry(available ? label : $"{label} (unavailable for this profile)",
                    canMutate && available, false, false, null));
            }
            else if (canApply)
            {
                foreach (var variant in variants)
                {
                    var available = errorFor(variant) == null;
                    entries.Add(new ApplyEntry(available ? variant : $"{variant} (unavailable for this profile)",
                        canMutate && available, applied && appliedVariant == variant, false, variant));
                }
            }
            if (anythingApplied)
                entries.Add(new ApplyEntry("Revert to Baseline", canMutate, false, true, null));
            return entries;
        }

        /// <summary>
        /// <see cref="ApplyEntries"/> for the current state. The active profile can
        /// be applied when the window would enable its Apply: it is applied
        /// (Re-apply), variants exist, or it has editor-applicable plugin settings.
        /// </summary>
        static List<ApplyEntry> ApplyEntriesFor(State state)
        {
            var profile = state.ActiveForgeProfile;
            var variants = ForgeSettings.instance.BuildVariants;
            var canApply = profile != null && (state.Applied || variants.Count > 0 || ForgeEditorState.HasEditorApplicableSettings(profile));
            // A plugin or plugin settings that can't load stop Apply as they stop a build.
            var blocking = profile != null ? ForgePluginRegistry.BlockingError(profile) : null;
            return ApplyEntries(canApply, variants, v => BuildVariants.ProfileError(profile, v) ?? blocking,
                state.Applied, ForgeEditorState.AppliedVariant, state.AnythingApplied, state.CanMutate);
        }

        internal readonly struct BuildButton
        {
            public readonly string Label;
            public readonly string Tooltip;
            public readonly bool Enabled;

            public BuildButton(string label, string tooltip, bool enabled)
            {
                Label = label;
                Tooltip = tooltip;
                Enabled = enabled;
            }
        }

        /// <summary>
        /// The Build button, or Build and Run (<paramref name="run"/>). It builds the
        /// active profile (<paramref name="profileName"/>, null when the active Unity
        /// Build Profile has no single set of Build Forge settings, for
        /// <paramref name="noProfileReason"/>) with the build window's variant, and is
        /// enabled when the window's Build would be: a variant is picked once the project
        /// has variants, nothing stops the profile's build (<paramref name="profileError"/>
        /// says why otherwise: a variant it doesn't allow, or a plugin or plugin settings
        /// that can't load) and mutating is not refused. The tooltip names what it builds.
        /// </summary>
        internal static BuildButton BuildButtonFor(bool run, string profileName, string noProfileReason, string variant,
            bool variantsConfigured, string profileError, bool canMutate, string mutateReason)
        {
            var label = run ? "Build and Run" : "Build";
            if (!canMutate)
                return new BuildButton(label, mutateReason, false);
            if (profileName == null)
                return new BuildButton(label, noProfileReason, false);
            if (variantsConfigured && variant == null)
                return new BuildButton(label, PickVariantReason, false);
            if (profileError != null)
                return new BuildButton(label, profileError, false);
            var what = variantsConfigured ? $"'{profileName}' with variant '{variant}'" : $"'{profileName}'";
            var tooltip = $"Build {what} through Build Forge{(run ? ", then run it" : "")}.";
            if (variantsConfigured)
                tooltip += " The variant is the build window's, which the apply menu also sets.";
            return new BuildButton(label, tooltip, true);
        }

        internal const string PickVariantReason = "Pick a variant first, in the build window's details header or by applying one " +
                                                  "from the apply dropdown: once a project has variants, every build names one.";

        static BuildButton BuildButtonFor(bool run, State state)
        {
            var profile = state.ActiveForgeProfile;
            var variant = ForgeBuildWindow.SessionVariant;
            var noProfileReason = profile != null ? null
                : "Activate a Build Profile with Build Forge settings to build it. " + (state.Active == null
                    ? $"No Unity Build Profile is active; Unity is on the {EditorUserBuildSettings.activeBuildTarget} platform profile."
                    : state.ResolveError);
            return BuildButtonFor(run, profile?.DisplayName, noProfileReason, variant, ForgeSettings.instance.BuildVariants.Count > 0,
                profile != null ? BuildVariants.ProfileError(profile, variant) ?? ForgePluginRegistry.BlockingError(profile) : null,
                state.CanMutate, state.MutateReason);
        }

        static void ShowProfileMenu(Rect rect)
        {
            var state = State.Capture();
            var menu = new GenericMenu();
            if (!state.CanMutate)
                menu.AddDisabledItem(new GUIContent(state.MutateReason));
            var entries = ProfileEntries(state.All, state.Active, state.CanMutate);
            if (entries.Count == 0)
                menu.AddDisabledItem(new GUIContent("No Build Profiles with Build Forge settings (Assets > Build Forge > Add Build Forge Settings)"));
            foreach (var entry in entries)
            {
                var profile = entry.Profile;
                if (entry.Enabled)
                    menu.AddItem(new GUIContent(entry.Label), entry.Checked, () => Activate(profile));
                else
                    menu.AddDisabledItem(new GUIContent(entry.Label), entry.Checked);
            }
            menu.DropDown(rect);
        }

        static void ShowApplyMenu(Rect rect)
        {
            var state = State.Capture();
            var profile = state.ActiveForgeProfile;
            var entries = ApplyEntriesFor(state);
            // Nothing left to offer since the last refresh; the next poll hides the dropdown.
            if (entries.Count == 0)
                return;
            var menu = new GenericMenu();
            if (!state.CanMutate)
                menu.AddDisabledItem(new GUIContent(state.MutateReason));
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                // Revert to Baseline is set apart from the applies, when there are any.
                if (entry.Revert && i > 0)
                    menu.AddSeparator("");
                var variant = entry.Variant;
                var action = entry.Revert ? (GenericMenu.MenuFunction)Revert : () => Apply(profile, variant);
                if (entry.Enabled)
                    menu.AddItem(new GUIContent(entry.Label), entry.Checked, action);
                else
                    menu.AddDisabledItem(new GUIContent(entry.Label), entry.Checked);
            }
            menu.DropDown(rect);
        }

        // Activating switches platforms (domain reload) and applying saves assets,
        // so like the build window they run outside the menu callback.
        static void Activate(ForgeProfile profile)
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    ForgeEditorState.Activate(profile);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Build Forge] Activating '{profile.DisplayName}' failed: {e.Message}\n{e.StackTrace}");
                }
                s_StateKey = null;
            };
        }

        static void Apply(ForgeProfile profile, string variant)
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    ForgeEditorState.Apply(profile, variant);
                    // Apply and the Build buttons in the window now default to the variant picked here.
                    ForgeBuildWindow.SetSelectedVariant(variant);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Build Forge] Applying '{profile.DisplayName}' failed: {e.Message}\n{e.StackTrace}");
                    // Same recovery as the window's Apply.
                    ForgeBuildWindow.OfferToStopTracking(e);
                }
                s_StateKey = null;
            };
        }

        static void Revert()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    ForgeEditorState.RevertToBaseline();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Build Forge] Revert to Baseline failed: {e.Message}\n{e.StackTrace}");
                    // Same recovery as the window: settings that can never be restored can stop being tracked.
                    ForgeBuildWindow.OfferToStopTracking(e);
                }
                s_StateKey = null;
            };
        }

        // The buttons show the state of the last refresh, up to a second old, so
        // the click checks it again. Like the window's Build, the build itself runs
        // outside the click.
        static void Build(bool run)
        {
            var state = State.Capture();
            var button = BuildButtonFor(run, state);
            if (!button.Enabled)
            {
                Debug.LogWarning($"[Build Forge] {button.Label} is not available: {button.Tooltip}");
                s_StateKey = null;
                return;
            }
            var profile = state.ActiveForgeProfile;
            var variant = ForgeBuildWindow.SessionVariant;
            EditorApplication.delayCall += () =>
            {
                try
                {
                    ForgeBuildRunner.RunBuild(profile, variant, out _, runPlayer: run);
                }
                catch (Exception e)
                {
                    // Logged rather than thrown, like Activate and Apply: a throw out of
                    // a delayCall also drops the other callbacks queued with it.
                    Debug.LogError($"[Build Forge] Building '{profile.DisplayName}' failed: {e.Message}\n{e.StackTrace}");
                }
                finally
                {
                    s_StateKey = null;
                }
            };
        }
    }
}
