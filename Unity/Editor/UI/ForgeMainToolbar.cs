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
    /// Build Profile and apply dropdowns in Unity's main toolbar (the
    /// MainToolbarElement API, Unity 6.3+), one element that the user places.
    /// Picking a profile activates it, like the build window's Activate. The
    /// second dropdown shows what is applied to the active profile, worded like
    /// the window's Editor line, and applies it like the window's Apply: it lists
    /// the variants when the project has any and offers Apply or Re-apply when it
    /// has none. Revert to Baseline follows whenever anything is applied, as in
    /// the window.
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
            return new MainToolbarContent(ProfileText(state.Active != null ? state.Active.name : null),
                ForgeBuildWindow.PlatformIcon(target) as Texture2D, tooltip);
        }

        static MainToolbarContent ApplyContent(State state)
        {
            var appliedVariant = ForgeEditorState.AppliedVariant;
            var variantsConfigured = ForgeSettings.instance.BuildVariants.Count > 0;
            var drift = state.Applied ? ForgeEditorState.GetDrift() : Array.Empty<string>();
            string tooltip;
            if (!state.CanMutate)
                tooltip = state.MutateReason;
            else if (drift.Count > 0)
                // The window's drift warning, and how the toolbar re-applies.
                tooltip = $"Editor settings differ from '{state.ActiveForgeProfile.DisplayName}' as applied: " +
                          string.Join(", ", drift.Select(ForgeBuildWindow.DescribeDriftKey)) + ". " +
                          (variantsConfigured ? "Re-apply (pick the checked variant)" : "Re-apply") +
                          " restores the profile's state; Revert to Baseline undoes it.";
            else if (state.Applied)
                tooltip = variantsConfigured
                    ? $"Applied with {(appliedVariant == null ? "the default variant" : $"variant '{appliedVariant}'")}. " +
                      "Pick another variant, re-apply this one after editing the profile, or Revert to Baseline."
                    : "Plugin settings applied. Re-apply after editing the profile, or Revert to Baseline.";
            else if (state.AnythingApplied)
                // Another profile's settings are applied: the window's warning for that.
                tooltip = ForgeEditorState.GetActiveMismatch(out _) ?? ForgeEditorState.UnresolvedAppliedWarning;
            else
                tooltip = variantsConfigured
                    ? "Pick a variant to apply the profile with it: its defines and the profile's plugin settings, so Play Mode matches."
                    : "Apply the profile's plugin settings (such as OpenXR features), so Play Mode matches.";
            return new MainToolbarContent(ApplyText(variantsConfigured, state.Applied, drift.Count > 0, appliedVariant), tooltip);
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
        /// The apply dropdown's label, worded like the build window's Editor line:
        /// "Not applied", or "Applied" (", drifted" when the editor settings
        /// changed since) with the applied variant in parentheses, "default
        /// variant" for the default build once the project has variants. The
        /// state comes first because Unity 6.6 cuts toolbar dropdown text at
        /// 120 px: a long variant name ends in an ellipsis there.
        /// </summary>
        internal static string ApplyText(bool variantsConfigured, bool applied, bool drifted, string appliedVariant)
        {
            if (!applied)
                return "Not applied";
            var text = drifted ? "Applied, drifted" : "Applied";
            return appliedVariant != null ? $"{text} ({appliedVariant})" : variantsConfigured ? $"{text} (default variant)" : text;
        }

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
                         .ThenBy(p => p.BuildProfile != null ? BuildProfileUtility.GetBuildTarget(p.BuildProfile).ToString() : "", StringComparer.OrdinalIgnoreCase)
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
        /// (<paramref name="canApply"/>), with variants: the default build, then
        /// each configured variant, the applied one checked; picking the checked
        /// one re-applies it, and a variant the active profile cannot take
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
                foreach (var variant in new string[] { null }.Concat(variants))
                {
                    var name = variant ?? BuildVariants.DefaultLabel;
                    var available = errorFor(variant) == null;
                    entries.Add(new ApplyEntry(available ? name : $"{name} (unavailable for this profile)",
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
            return ApplyEntries(canApply, variants, v => BuildVariants.ProfileError(profile, v),
                state.Applied, ForgeEditorState.AppliedVariant, state.AnythingApplied, state.CanMutate);
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
                    // Build and Apply in the window now default to the variant picked here.
                    ForgeBuildWindow.SetSelectedVariant(variant);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Build Forge] Applying '{profile.DisplayName}' failed: {e.Message}\n{e.StackTrace}");
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
    }
}
