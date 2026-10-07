using System;
using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Handle returned by <see cref="ForgeEditorState.SuspendForBuild"/>; pass it
    /// back to <see cref="ForgeEditorState.ResumeAfterBuild"/>.
    /// </summary>
    internal sealed class BuildSuspension
    {
        // Only a marker that a suspension happened. The applied profile is
        // re-resolved from the ledger at resume time: an object reference held
        // across a build is destroyed by the build's unload-unused-assets pass
        // (observed in batch mode on 6000.6.0), which would read as "asset gone".
        public string AppliedName;
    }

    /// <summary>
    /// The editor-facing coordinator for applied profiles: binds
    /// <see cref="EditorStateApplier"/> (pure) to <see cref="ForgeSettings"/>
    /// (persistence), the plugin registry, the asset database and the Unity
    /// Build Profile system. The only entry point the UI and the build runner
    /// use. See ARCHITECTURE.md "Editor State".
    /// </summary>
    internal static class ForgeEditorState
    {
        const double DriftCacheSeconds = 2.0;

        static List<string> cachedDrift;
        static double cachedDriftTime = -1;

        public static bool HasAppliedGuid => !string.IsNullOrEmpty(ForgeEditorStateStore.instance.AppliedBuildProfileGuid);

        /// <summary>The Unity Build Profile whose configuration is applied, or null when nothing is applied or the asset no longer exists.</summary>
        public static BuildProfile AppliedBuildProfile
        {
            get
            {
                var guid = ForgeEditorStateStore.instance.AppliedBuildProfileGuid;
                if (string.IsNullOrEmpty(guid))
                    return null;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
            }
        }

        /// <summary>
        /// The applied Build Forge profile, resolved from the applied Unity Build
        /// Profile through the 1:1 lookup; null when nothing is applied, the Unity
        /// profile is gone, or no single Build Forge profile references it.
        /// </summary>
        public static ForgeProfile AppliedProfile
        {
            get
            {
                var buildProfile = AppliedBuildProfile;
                if (buildProfile == null)
                    return null;
                return ForgeProfileLookup.ResolveForBuildProfile(buildProfile, ForgeProfileLookup.FindAll(), out _);
            }
        }

        public static IReadOnlyList<EditorStateEntry> Entries => ForgeEditorStateStore.instance.EditorState;

        /// <summary>The variant applied with the profile, or null.</summary>
        public static string AppliedVariant
        {
            get
            {
                var v = ForgeEditorStateStore.instance.AppliedVariant;
                return string.IsNullOrEmpty(v) ? null : v;
            }
        }

        /// <summary>Pseudo drift key reported when the applied variant's define is missing from the Unity Build Profile.</summary>
        public const string VariantDriftKey = "Variant";

        public static bool IsApplied(ForgeProfile profile)
        {
            if (profile == null || profile.BuildProfile == null || !HasAppliedGuid)
                return false;
            return GetGuid(profile.BuildProfile) == ForgeEditorStateStore.instance.AppliedBuildProfileGuid;
        }

        /// <summary>
        /// Unity's active Build Profile can change without Build Forge (its own
        /// Switch Profile, -activeBuildProfile), and a platform profile can become
        /// active (Unity's Platforms list, -buildTarget, a deleted Library/, which
        /// keeps no active Build Profile). Either leaves another profile's plugin
        /// settings applied underneath the active one. Returns a warning
        /// describing that, or null when nothing is applied or the two agree.
        /// <paramref name="activeForgeProfile"/> is the Build Forge profile of the
        /// active Unity profile when one resolves (the switch that fixes it).
        /// </summary>
        public static string GetActiveMismatch(out ForgeProfile activeForgeProfile)
        {
            activeForgeProfile = null;
            if (!HasAppliedGuid)
                return null;
            var active = BuildProfile.GetActiveBuildProfile();
            var applied = AppliedBuildProfile;
            if (active == applied)
                return null;
            var all = ForgeProfileLookup.FindAll();
            // Activate keeps the applied profile's settings; it needs that profile's Build Forge Profile.
            var canReactivate = applied != null && ForgeProfileLookup.ResolveForBuildProfile(applied, all, out _) != null;
            if (active == null)
                return applied != null
                    ? PlatformProfileMismatch(EditorUserBuildSettings.activeBuildTarget.ToString(), applied.name, canReactivate)
                    : null;

            activeForgeProfile = ForgeProfileLookup.ResolveForBuildProfile(active, all, out _);
            return SwitchedProfileMismatch(active.name, applied != null ? applied.name : null,
                activeForgeProfile != null ? activeForgeProfile.DisplayName : null, canReactivate);
        }

        /// <summary>
        /// The warning for Unity's active Build Profile <paramref name="activeName"/>
        /// while <paramref name="appliedName"/> is applied (null when its asset no
        /// longer exists). The fixes: apply the active profile's Build Forge
        /// Profile (<paramref name="activeForgeName"/>, null when none references
        /// it), activate the applied profile again, which keeps its settings, or
        /// revert. Pure so it can be tested.
        /// </summary>
        internal static string SwitchedProfileMismatch(string activeName, string appliedName, string activeForgeName, bool canReactivate)
        {
            var owner = appliedName != null ? $"'{appliedName}'" : "a Unity Build Profile that no longer exists";
            var fixes = new List<string>();
            if (activeForgeName != null)
                fixes.Add($"Apply '{activeForgeName}' to replace them with its own");
            if (canReactivate && appliedName != null)
                fixes.Add($"activate '{appliedName}' again to keep them");
            fixes.Add("Revert to Baseline");
            var fix = fixes.Count == 1 ? fixes[0] : string.Join(", ", fixes.Take(fixes.Count - 1)) + ", or " + fixes[fixes.Count - 1];
            fix = char.ToUpperInvariant(fix[0]) + fix.Substring(1);
            if (activeForgeName == null)
                fix += " (the active Unity Build Profile has no Build Forge settings)";
            return $"Unity switched to '{activeName}' without applying its Build Forge settings; " +
                   $"the plugin-managed editor settings still belong to {owner}. {fix}.";
        }

        /// <summary>
        /// The warning while something is applied that no Build Forge Profile can
        /// re-apply, shown by the build window and the main toolbar.
        /// </summary>
        public const string UnresolvedAppliedWarning =
            "The applied Unity Build Profile no longer exists, or no longer has exactly one set of " +
            "Build Forge settings. Revert to Baseline restores the settings it changed.";

        /// <summary>
        /// The warning for a platform profile that is active while
        /// <paramref name="appliedName"/> is applied. No Build Forge profile can
        /// reference a platform profile, so the fixes are to go back to the
        /// applied profile, which keeps its settings (with Activate when
        /// <paramref name="canReactivate"/>: a Build Forge Profile references it),
        /// or to revert. Pure so it can be tested.
        /// </summary>
        internal static string PlatformProfileMismatch(string target, string appliedName, bool canReactivate)
            => $"Unity is on the {target} platform profile, not on a Build Profile; the plugin-managed editor settings " +
               $"still belong to '{appliedName}'. " +
               (canReactivate
                   ? $"Activate '{appliedName}' again to keep them, or Revert to Baseline."
                   : $"Revert to Baseline, or switch back to '{appliedName}' in Unity's Build Profiles window to keep them.");

        /// <summary>
        /// False while applying/reverting would fight the editor: Play Mode
        /// (OpenXR refuses feature changes while its loader runs, and platform
        /// switches are disallowed), compilation, asset import, or a build.
        /// </summary>
        public static bool CanMutate(out string reason)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                reason = "Not available in Play Mode.";
            else if (EditorApplication.isCompiling)
                reason = "Not available while scripts are compiling.";
            else if (EditorApplication.isUpdating)
                reason = "Not available while assets are importing.";
            else if (ForgeBuildRunner.IsForgeBuildInProgress || BuildPipeline.isBuildingPlayer)
                reason = "Not available while a build is running.";
            else
            {
                reason = null;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The (plugin, group) pairs that would be written for the profile: the
        /// same plugin filter as the build runner, narrowed to editor-applicable
        /// plugins. The group comes from the profile's own Build Profile, not the
        /// active one.
        /// </summary>
        public static List<EditorStatePair> GetParticipatingPairs(ForgeProfile profile)
        {
            var pairs = new List<EditorStatePair>();
            if (profile == null || profile.BuildProfile == null)
                return pairs;

            var group = BuildPipeline.GetBuildTargetGroup(BuildProfileUtility.GetBuildTarget(profile.BuildProfile));

            foreach (var plugin in ForgePluginRegistry.GetPlugins())
            {
                if (!(plugin is IForgeEditorApplicable applicable))
                    continue;
                if (ForgeSettings.instance.IsPluginDisabledWithDefault(
                        plugin.GetType().FullName, ForgePluginRegistry.IsSafeToDefaultEnable(plugin)))
                    continue;
                if (!plugin.IsApplicable(profile.BuildProfile))
                    continue;
                if (plugin.IsEnabled(profile) == false)
                    continue;

                pairs.Add(new EditorStatePair
                {
                    PluginTypeName = plugin.GetType().FullName,
                    Group = group,
                    Plugin = applicable,
                });
            }

            // After the plugins, so the mirrors copy the platform as the plugins leave it.
            if (ForgeSettings.instance.PlayModeFollowsAppliedProfile && group != BuildTargetGroup.Standalone)
            {
                foreach (var mirror in PlayModeMirrors())
                {
                    if (!mirror.CanMirror(group))
                        continue;
                    pairs.Add(new EditorStatePair
                    {
                        PluginTypeName = mirror.GetType().FullName,
                        Group = BuildTargetGroup.Standalone,
                        Plugin = mirror,
                    });
                }
            }
            return pairs;
        }

        static IForgePlayModeMirror[] playModeMirrors;

        /// <summary>The Play Mode mirrors that the installed XR packages' assemblies define, found by type.</summary>
        internal static IReadOnlyList<IForgePlayModeMirror> PlayModeMirrors()
            => playModeMirrors ??= TypeCache.GetTypesDerivedFrom<IForgePlayModeMirror>()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => (IForgePlayModeMirror)Activator.CreateInstance(t))
                .ToArray();

        /// <summary>
        /// Applies the profile to the editor: reverts whatever is applied, writes
        /// the profile's plugin overrides on top of the baseline, records the
        /// state, then activates the profile's Unity Build Profile last (that call
        /// may trigger a domain reload; everything is on disk by then).
        /// </summary>
        public static void Apply(ForgeProfile profile, string variant = null)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (profile.BuildProfile == null)
                throw new InvalidOperationException(
                    $"ForgeProfile '{profile.ProfileName}' has no Unity Build Profile assigned.");
            if (!CanMutate(out var reason))
                throw new InvalidOperationException(reason);
            var resolvedVariant = BuildVariants.Resolve(variant, ForgeSettings.instance.BuildVariants, out var variantError);
            variantError ??= BuildVariants.ProfileError(profile, resolvedVariant);
            if (variantError != null)
                throw new InvalidOperationException(variantError);

            var guid = GetGuid(profile.BuildProfile);
            if (string.IsNullOrEmpty(guid))
                throw new InvalidOperationException(
                    $"Unity Build Profile '{profile.BuildProfile.name}' must be a saved asset to be applied.");

            var settings = ForgeEditorStateStore.instance;
            var pairs = GetParticipatingPairs(profile);

            Debug.Log($"[Build Forge] Applying profile '{profile.DisplayName}' to the editor " +
                $"({pairs.Count} plugin setting group(s)).");

            var previouslyApplied = AppliedBuildProfile;
            try
            {
                EditorStateApplier.Apply(settings.EditorState, pairs, profile, ResolvePlugin, () =>
                {
                    settings.AppliedBuildProfileGuid = guid;
                    settings.AppliedVariant = resolvedVariant ?? "";
                    settings.SaveState();
                }, resolvedVariant);
            }
            finally
            {
                // Reverting may have removed successful entries before a later
                // restore failed. Persist the remaining baselines for retry.
                settings.SaveState();
                InvalidateDriftCache();
            }

            // Keep the old profile's variant intact until its settings have
            // successfully reverted and the new apply can complete.
            if (previouslyApplied != null && previouslyApplied != profile.BuildProfile)
                BuildVariants.StripFromProfile(previouslyApplied);

            // The defines ride on the Build Profile, so they are in place before
            // the profile becomes active and the domain reloads with them.
            BuildProfileDefines.Ensure(profile.BuildProfile);
            BuildVariants.ApplyOnProfile(profile.BuildProfile, resolvedVariant);

            if (BuildProfile.GetActiveBuildProfile() != profile.BuildProfile)
                BuildProfile.SetActiveBuildProfile(profile.BuildProfile);
        }

        /// <summary>Restores every managed setting group to its baseline and forgets the applied profile.</summary>
        public static void RevertToBaseline()
        {
            if (!CanMutate(out var reason))
                throw new InvalidOperationException(reason);
            RevertCore();
            Debug.Log("[Build Forge] Reverted editor settings to baseline.");
        }

        /// <returns>True when a variant define was removed from the applied Build Profile.</returns>
        static bool RevertCore()
        {
            var settings = ForgeEditorStateStore.instance;
            try
            {
                EditorStateApplier.RevertAll(settings.EditorState, ResolvePlugin);
                // Forget the applied profile only after every baseline restored.
                var defineRemoved = BuildVariants.StripFromProfile(AppliedBuildProfile);
                settings.AppliedBuildProfileGuid = "";
                settings.AppliedVariant = "";
                return defineRemoved;
            }
            finally
            {
                settings.SaveState();
                InvalidateDriftCache();
            }
        }

        /// <summary>
        /// Forgets the recorded entries and the applied profile, leaving those
        /// settings at their current values. Meant for the entries a failed Revert
        /// to Baseline leaves behind, which are exactly the ones it could not
        /// restore (a removed plugin, a baseline that refers to something that no
        /// longer exists): while they are tracked, every Apply, Activate, revert
        /// and build stops on them. Returns the keys that were dropped.
        /// </summary>
        public static IReadOnlyList<string> StopTracking()
        {
            if (!CanMutate(out var reason))
                throw new InvalidOperationException(reason);
            var settings = ForgeEditorStateStore.instance;
            var keys = settings.EditorState.Select(entry => entry.key).ToList();
            try
            {
                settings.EditorState.Clear();
                BuildVariants.StripFromProfile(AppliedBuildProfile);
                settings.AppliedBuildProfileGuid = "";
                settings.AppliedVariant = "";
            }
            finally
            {
                settings.SaveState();
                InvalidateDriftCache();
            }
            Debug.LogWarning($"[Build Forge] Stopped tracking {keys.Count} setting group(s) ({string.Join(", ", keys)}). " +
                             "They keep their current values, and Build Forge no longer restores them.");
            return keys;
        }

        /// <summary>True when applying this profile would write anything: at least one enabled, applicable IForgeEditorApplicable plugin.</summary>
        public static bool HasEditorApplicableSettings(ForgeProfile profile)
            => GetParticipatingPairs(profile).Count > 0;

        /// <summary>
        /// Makes the profile's Unity Build Profile active without applying its
        /// plugin settings: Unity's own Switch Profile, plus the guarantee that
        /// nothing stale remains, because whatever another profile applied is
        /// reverted to its baseline first. Apply is a separate, explicit step
        /// afterwards. The profile that is still applied (Unity was switched
        /// away from it without Build Forge) keeps its settings and variant
        /// define: they are exactly what applying it writes, so reverting them
        /// would only force an Apply. The activation may trigger a domain
        /// reload; the ledger is on disk before that.
        /// </summary>
        public static void Activate(ForgeProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (profile.BuildProfile == null)
                throw new InvalidOperationException(
                    $"ForgeProfile '{profile.ProfileName}' has no Unity Build Profile assigned.");
            if (!CanMutate(out var reason))
                throw new InvalidOperationException(reason);

            var keepApplied = IsApplied(profile);
            string previousName = null, previousVariant = null;
            var defineRemoved = false;
            if (HasAppliedGuid && !keepApplied)
            {
                var previous = AppliedProfile;
                previousName = previous != null ? previous.DisplayName : "the previously applied profile";
                previousVariant = AppliedVariant;
                defineRemoved = RevertCore();
                Debug.Log("[Build Forge] Reverted the applied plugin settings to baseline before activating.");
            }

            // The define rides on the Build Profile; have it in place before the
            // reload. Activate applies no variant, so none may be left on it, not
            // even variant state saved on another machine; name that while it can
            // still be seen. The applied profile keeps its applied variant's define.
            var stray = BuildVariants.StrayWarning(profile, IsApplied(profile));
            BuildProfileDefines.Ensure(profile.BuildProfile);
            if (!keepApplied)
                BuildVariants.StripFromProfile(profile.BuildProfile);

            var summary = keepApplied
                ? ReactivationSummary(profile.DisplayName, AppliedVariant)
                : ActivationSummary(profile.DisplayName, previousName, previousVariant, defineRemoved);
            var warning = stray != null ? $"[Build Forge] {stray}" : null;
            if (BuildProfile.GetActiveBuildProfile() != profile.BuildProfile)
            {
                Debug.Log($"[Build Forge] Activating Unity Build Profile '{profile.BuildProfile.name}'.");
                // The switch reloads the domain, and a Console set to "Clear on
                // Recompile" drops everything logged so far. Park the summary and
                // log it once the new domain is up, so the outcome is visible.
                SessionState.SetString(PendingActivationSummaryKey, summary);
                if (warning != null)
                    SessionState.SetString(PendingActivationWarningKey, warning);
                BuildProfile.SetActiveBuildProfile(profile.BuildProfile);
            }
            else
            {
                Debug.Log(summary);
                if (warning != null)
                    Debug.LogWarning(warning);
            }
        }

        const string PendingActivationSummaryKey = "BuildForge.PendingActivationSummary";
        const string PendingActivationWarningKey = "BuildForge.PendingActivationWarning";

        [InitializeOnLoadMethod]
        static void FlushPendingActivationSummary()
        {
            var summary = SessionState.GetString(PendingActivationSummaryKey, "");
            var warning = SessionState.GetString(PendingActivationWarningKey, "");
            if (string.IsNullOrEmpty(summary) && string.IsNullOrEmpty(warning))
                return;
            SessionState.EraseString(PendingActivationSummaryKey);
            SessionState.EraseString(PendingActivationWarningKey);
            if (!string.IsNullOrEmpty(summary))
                Debug.Log(summary);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning(warning);
        }

        /// <summary>The command-line argument naming the Unity Build Profile to activate.</summary>
        internal const string BuildProfileArg = "-forgeBuildProfile";

        /// <summary>
        /// The Unity Build Profile path given to <see cref="BuildProfileArg"/>, or
        /// null with <paramref name="error"/> set. Strict like the build's
        /// arguments: the argument is required and needs a value, and any other
        /// argument starting with -forge is an error, because Activate takes no
        /// build arguments and a run that ignored one would still succeed. Other
        /// arguments, Unity's own included, are left alone.
        /// </summary>
        internal static string ParseActivateCommandLine(IReadOnlyList<string> args, out string error)
        {
            error = null;
            string path = null;
            for (int i = 0; args != null && i < args.Count; i++)
            {
                if (!string.Equals(args[i], BuildProfileArg, StringComparison.OrdinalIgnoreCase))
                {
                    if (args[i].StartsWith("-forge", StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"Unknown argument '{args[i]}'. Activate takes only {BuildProfileArg}.";
                        return null;
                    }
                    continue;
                }

                var value = i + 1 < args.Count ? args[i + 1] : null;
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("-"))
                {
                    error = $"{BuildProfileArg} needs a value: the Unity Build Profile's asset path, " +
                            "for example \"Assets/Settings/Build Profiles/Quest.asset\".";
                    return null;
                }
                path = value;
                i++;
            }
            if (path == null)
                error = $"Pass {BuildProfileArg} \"<path to the Unity Build Profile asset>\".";
            return path;
        }

        /// <summary>
        /// Command-line entry point: activates the Build Forge settings of the
        /// Unity Build Profile given to <see cref="BuildProfileArg"/>, like the
        /// build window's Activate, then exits the editor with 0, or with 1 and the
        /// reason in the log. Across a platform change, Unity 6000.3.23's
        /// -activeBuildProfile compiles the new profile's defines for the previous
        /// platform first (6000.3.0 and 6000.6.4 switch first), where code under
        /// them can fail to compile. Switching here, after a normal startup,
        /// changes platform and defines together, and the next run starts on the
        /// new profile.
        /// </summary>
        internal static void ActivateFromCommandLine()
        {
            var path = ParseActivateCommandLine(Environment.GetCommandLineArgs(), out var error);
            ForgeProfile profile = null;
            if (error == null)
            {
                var buildProfile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
                if (buildProfile == null)
                    error = $"No Unity Build Profile at '{path}'. Pass its asset path in the project, " +
                            "for example \"Assets/Settings/Build Profiles/Quest.asset\".";
                else
                    profile = ForgeProfileLookup.ResolveForBuildProfile(buildProfile, ForgeProfileLookup.FindAll(), out error);
            }
            if (error == null)
            {
                try
                {
                    Activate(profile);
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
            }
            if (error != null)
            {
                Debug.LogError($"[Build Forge] {error}");
                EditorApplication.Exit(1);
                return;
            }
            // A switch parks its summary for after the domain reload, which this run exits before.
            FlushPendingActivationSummary();
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// The one line that states what Activate did, meant to be read after the
        /// reload has cleared the console. Pure so it can be tested.
        /// </summary>
        internal static string ActivationSummary(string activatedName, string previousName, string previousVariant, bool defineRemoved)
        {
            var text = $"[Build Forge] Activated '{activatedName}'";
            if (previousName != null)
            {
                text += $"; reverted '{previousName}'"
                        + (string.IsNullOrEmpty(previousVariant) ? "" : $" (variant {previousVariant})")
                        + " to baseline"
                        + (defineRemoved ? " and removed its variant define" : "");
            }
            return text + $". Plugin settings are not applied; use Apply to write those of '{activatedName}'.";
        }

        /// <summary>
        /// What Activate logs for the profile that is still applied: nothing was
        /// reverted. Pure so it can be tested.
        /// </summary>
        internal static string ReactivationSummary(string activatedName, string appliedVariant)
            => $"[Build Forge] Activated '{activatedName}', which is still applied"
               + (string.IsNullOrEmpty(appliedVariant) ? "" : $" (variant {appliedVariant})")
               + ": its plugin settings were kept, not reverted.";

        /// <summary>
        /// Stops reporting the current (drifted) state: expected := current capture,
        /// baselines untouched, the profile asset untouched. Lasts until the next
        /// apply or build rewrites the settings from the profile.
        /// </summary>
        public static void DismissDrift()
        {
            var settings = ForgeEditorStateStore.instance;
            if (EditorStateApplier.DismissDrift(settings.EditorState, ResolvePlugin))
                settings.SaveState();
            InvalidateDriftCache();
        }

        /// <summary>True when the applied state includes Play Mode's XR, copied by a Play Mode mirror.</summary>
        public static bool IsPlayModeXRApplied()
        {
            foreach (var entry in ForgeEditorStateStore.instance.EditorState)
                if (EditorStateLedger.TryParseKey(entry.key, out var typeName, out _)
                    && PlayModeMirrors().Any(m => m.GetType().FullName == typeName))
                    return true;
            return false;
        }

        /// <summary>
        /// Keys whose current state differs from what was applied. Cached for a
        /// couple of seconds: capturing means serializing every OpenXR feature,
        /// and the build window repaints ~10 times a second.
        /// </summary>
        public static IReadOnlyList<string> GetDrift(bool force = false)
        {
            if (!HasAppliedGuid)
                return Array.Empty<string>();

            var now = EditorApplication.timeSinceStartup;
            if (force || cachedDrift == null || now - cachedDriftTime > DriftCacheSeconds)
            {
                cachedDrift = new List<string>(EditorStateApplier.Drift(ForgeEditorStateStore.instance.EditorState, ResolvePlugin));
                // The applied variant's define (or BUILD_VARIANT_DEFAULT) is expected
                // on the Unity profile; a hand edit that removes it is drift
                // (Re-apply restores it).
                if (ForgeSettings.instance.BuildVariants.Count > 0 &&
                    !BuildVariants.HasExpectedDefine(AppliedBuildProfile, AppliedVariant, true))
                    cachedDrift.Add(VariantDriftKey);
                cachedDriftTime = now;
            }
            return cachedDrift;
        }

        public static void InvalidateDriftCache()
        {
            cachedDrift = null;
            cachedDriftTime = -1;
        }

        /// <summary>
        /// Before a build while a profile is applied: restore baselines so the
        /// build is baseline + the built profile's deltas. This also holds when
        /// the built profile IS the applied one — the profile may have been
        /// edited since it was applied (an override changed to No Override
        /// leaves the old value in the editor, and drift cannot see it), so the
        /// build must not run on top of the applied state.
        /// Returns null when nothing is applied.
        /// </summary>
        public static BuildSuspension SuspendForBuild(ForgeProfile building)
        {
            if (!HasAppliedGuid)
                return null;

            var settings = ForgeEditorStateStore.instance;
            if (settings.EditorState.Count == 0)
                return null;

            var applied = AppliedProfile;

            Debug.Log($"[Build Forge] Suspending applied profile '{(applied != null ? applied.ProfileName : "(missing)")}' " +
                $"for the build of '{(building != null ? building.DisplayName : "?")}'.");
            try
            {
                EditorStateApplier.SuspendForBuild(settings.EditorState, ResolvePlugin);
            }
            finally
            {
                InvalidateDriftCache();
            }

            return new BuildSuspension { AppliedName = applied != null ? applied.DisplayName : null };
        }

        /// <summary>Re-applies the suspended profile after the build.</summary>
        public static void ResumeAfterBuild(BuildSuspension suspension)
        {
            if (suspension == null)
                return;

            var settings = ForgeEditorStateStore.instance;
            var applied = AppliedProfile; // fresh lookup through the ledger's GUID
            var result = EditorStateApplier.ResumeAfterBuild(settings.EditorState, applied, ResolvePlugin, AppliedVariant);
            if (result.Cleared)
            {
                // The profile really is gone: leave the baseline in place and drop
                // every trace of the applied state, the variant define included.
                BuildVariants.StripFromProfile(AppliedBuildProfile);
                settings.AppliedBuildProfileGuid = "";
                settings.AppliedVariant = "";
                settings.SaveState();
                Debug.LogWarning("[Build Forge] The applied profile no longer exists; editor settings were left at baseline.");
            }
            else if (result.Changed)
            {
                settings.SaveState();
            }
            InvalidateDriftCache();

            if (result.Failures.Count > 0)
                throw new AggregateException(
                    "Could not re-apply the editor profile after the build.", result.Failures);
            if (result.Cleared)
                return;

            Debug.Log("[Build Forge] Re-applied the editor profile after the build.");
        }

        /// <summary>
        /// Resolves by type name against the full registry (including globally
        /// disabled plugins) and the Play Mode mirrors, so a plugin disabled or a
        /// setting turned off after being applied can still restore what it
        /// wrote. Only a removed assembly yields null.
        /// </summary>
        static IForgeEditorApplicable ResolvePlugin(string typeName)
        {
            return ForgePluginRegistry.GetPlugins()
                       .FirstOrDefault(p => p.GetType().FullName == typeName) as IForgeEditorApplicable
                   ?? PlayModeMirrors().FirstOrDefault(m => m.GetType().FullName == typeName);
        }

        static string GetGuid(UnityEngine.Object asset)
        {
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long _)
                ? guid
                : "";
        }
    }
}
