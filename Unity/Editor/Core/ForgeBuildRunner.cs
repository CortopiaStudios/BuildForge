using System;
using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Central build orchestrator. Runs builds through BuildPipeline.BuildPlayer
    /// with the profile-aware API (BuildPlayerWithProfileOptions) and a
    /// try/catch/finally pattern to guarantee restoration of plugin-applied settings.
    /// </summary>
    public static class ForgeBuildRunner
    {
        /// <summary>
        /// Set to true while a Build Forge build is in progress.
        /// Used by the interceptor to allow builds initiated through Build Forge.
        /// </summary>
        const string BuildInProgressKey = "BuildForge.BuildInProgress";

        public static bool IsForgeBuildInProgress
        {
            get => SessionState.GetBool(BuildInProgressKey, false);
            private set => SessionState.SetBool(BuildInProgressKey, value);
        }

        /// <summary>
        /// The build whose player build is running, for Unity callbacks during
        /// BuildPlayer (the Gradle step); null otherwise.
        /// </summary>
        internal static ForgeBuildContext CurrentContext { get; private set; }

        /// <summary>The plugins whose OnPreBuild ran for <see cref="CurrentContext"/>, in plugin order.</summary>
        internal static IReadOnlyList<IForgePlugin> CurrentPlugins { get; private set; } = Array.Empty<IForgePlugin>();

        /// <summary>
        /// Executes a build of the Unity Build Profile that stores the given Build Forge settings.
        /// Uses BuildPlayerWithProfileOptions so the Build Profile handles
        /// scenes, scripting defines, player settings, and build options natively.
        /// </summary>
        public static BuildReport RunBuild(ForgeProfile forgeProfile)
            => RunBuild(forgeProfile, null, out _);

        /// <summary>
        /// Builds the profile with the selected variant's define and optional
        /// development, alias, product-name and plugin rules. Null or empty
        /// selects Default. Invalid rules or unsupported selections throw before
        /// settings are changed.
        /// </summary>
        public static BuildReport RunBuild(ForgeProfile forgeProfile, string variant)
            => RunBuild(forgeProfile, variant, out _);

        /// <summary>
        /// Build overload that also exposes the context, so callers (the CLI
        /// entry point) can inspect post-build restore/cleanup failures that
        /// would otherwise be swallowed after a successful build.
        /// </summary>
        internal static BuildReport RunBuild(ForgeProfile forgeProfile, string variant, out ForgeBuildContext context)
        {
            context = null;

            if (forgeProfile == null)
                throw new ArgumentNullException(nameof(forgeProfile));

            if (forgeProfile.BuildProfile == null)
                throw new InvalidOperationException(
                    $"ForgeProfile '{forgeProfile.ProfileName}' has no Unity Build Profile assigned.");

            var resolvedVariant = BuildVariants.Resolve(variant, ForgeSettings.instance.BuildVariants, out var variantError);
            variantError ??= BuildVariants.ProfileError(forgeProfile, resolvedVariant);
            if (variantError != null)
                throw new InvalidOperationException(variantError);
            var buildName = resolvedVariant != null ? $"{forgeProfile.DisplayName} ({resolvedVariant})" : forgeProfile.DisplayName;

            // The static PlayerSettings API reads and writes the ACTIVE profile's
            // settings, so building a non-active profile would let its Player
            // Settings overrides silently shadow plugin and CLI writes.
            var activeProfile = BuildProfile.GetActiveBuildProfile();
            if (ForgeSettings.instance.MaintainBuildProfileDefines && BuildProfileDefines.NeedsUpdate(forgeProfile.BuildProfile))
            {
                // Writing defines here would recompile mid-build; the editor paths
                // keep the list current, so at build time only report it.
                Debug.LogWarning($"[Build Forge] Unity Build Profile '{forgeProfile.BuildProfile.name}' is missing its " +
                                 $"'{BuildProfileDefines.DefineFor(forgeProfile.BuildProfile.name)}' scripting define " +
                                 "(or carries a stale one). Open the Build Forge window once and commit the profile.");
            }
            if (activeProfile != forgeProfile.BuildProfile)
            {
                var activeName = activeProfile != null
                    ? $"'{activeProfile.name}'"
                    : "a platform profile (no Unity Build Profile is active)";
                throw new InvalidOperationException(
                    $"Unity Build Profile '{forgeProfile.BuildProfile.name}' must be active before building; " +
                    $"the active profile is {activeName}. Activate it via the Build Forge window's " +
                    "'Activate <profile>' button, BuildProfile.SetActiveBuildProfile(), or the -activeBuildProfile " +
                    "command line argument.");
            }

            // Variant state saved on another machine: the build recomputes the
            // defines, so this is for the log (CI has no window to show it in).
            var stray = BuildVariants.StrayWarning(forgeProfile, ForgeEditorState.IsApplied(forgeProfile));
            if (stray != null)
                Debug.LogWarning($"[Build Forge] {stray}");

            var buildProfile = forgeProfile.BuildProfile;
            var buildTarget = BuildProfileUtility.GetBuildTarget(buildProfile);
            var outputPath = forgeProfile.GetResolvedOutputPath(buildTarget, resolvedVariant);

            // With Remove Previous Build Output on for this profile, remove the
            // previous build's files before plugin discovery or preparation can
            // fail, in both editor and CI; this build's files are recorded in
            // the finally block.
            BuildOutputDirectory.Preparation outputPreparation = null;
            if (forgeProfile.RemovePreviousBuildOutput)
                outputPreparation = BuildOutputDirectory.Prepare(
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(buildProfile)), resolvedVariant, outputPath, buildTarget);
            else
                Debug.Log($"[Build Forge] Remove Previous Build Output is off for '{forgeProfile.DisplayName}'; " +
                          "files from previous builds stay in the destination.");

            context = new ForgeBuildContext(buildProfile, forgeProfile, buildTarget, outputPath,
                BuildEnvironment.IsCI, resolvedVariant);

            // Discover applicable plugins (excluding globally disabled ones and
            // ones switched off for this profile — plugins don't guard their
            // own hooks against being disabled)
            var plugins = ForgePluginRegistry.GetPlugins()
                .Where(p => !ForgeSettings.instance.IsPluginDisabledWithDefault(
                    p.GetType().FullName, ForgePluginRegistry.IsSafeToDefaultEnable(p)))
                .Where(p => p.IsApplicable(buildProfile))
                .Where(p => p.IsEnabled(forgeProfile) != false)
                .ToList();

            var appliedPlugins = new List<IForgePlugin>();

            BuildReport report = null;
            BuildSuspension suspension = null;
            string[] definesBeforeVariant = null;
            string buildSettingsBeforeVariant = null;
            VariantPlayerSettings.Snapshot playerSettingsBeforeVariant = null;
            string productNameBeforeMark = null;
            string versionBeforeRule = null;
            IsForgeBuildInProgress = true;

            try
            {
                // Mark a variant build in its product name ("Game (Internal)") for
                // the duration of the build: display name, window title, file name
                // and manifest all follow it. Restored in the finally, after the
                // plugins' post-build hooks.
                var markedName = BuildVariants.MarkedProductName(PlayerSettings.productName, resolvedVariant,
                    BuildVariants.ShouldMark(resolvedVariant));
                if (markedName != PlayerSettings.productName)
                {
                    productNameBeforeMark = PlayerSettings.productName;
                    PlayerSettings.productName = markedName;
                }

                // If a profile is applied to the editor, build from the baseline
                // instead of on top of its deltas (even when it is the profile
                // being built: it may have been edited since it was applied);
                // it is re-applied in the finally block. See ARCHITECTURE.md
                // "Editor State".
                suspension = ForgeEditorState.SuspendForBuild(forgeProfile);

                buildSettingsBeforeVariant = VariantBuildSettings.Capture(buildProfile);
                VariantDefineLedger.instance.UndoBuildSettings(buildProfile);
                var rule = BuildVariants.RuleFor(resolvedVariant);
                VariantBuildSettings.Apply(buildProfile, rule);
                playerSettingsBeforeVariant = new VariantPlayerSettings.Snapshot
                {
                    Target = BuildProfileUtility.IsDedicatedServer(buildProfile)
                        ? UnityEditor.Build.NamedBuildTarget.Server
                        : UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(buildTarget))
                };
                VariantPlayerSettings.Apply(rule?.BuildConfiguration, playerSettingsBeforeVariant);
                context.DevelopmentBuild = VariantBuildSettings.IsDevelopment(buildProfile);
                LogBuildConfiguration(rule, buildProfile);
                definesBeforeVariant = BuildVariants.BeginBuild(buildProfile, resolvedVariant);

                foreach (var plugin in plugins)
                {
                    Debug.Log($"[Build Forge] Running pre-build: {plugin.DisplayName}");
                    appliedPlugins.Add(plugin);
                    plugin.OnPreBuild(context);
                }
                CurrentContext = context;
                CurrentPlugins = appliedPlugins.ToArray();

                // The variant rule's Version: after the plugins, so {BuildNumber}
                // is the number Build Number set, and before the manifest, which
                // records the version as built. Restored first in the finally.
                versionBeforeRule = ApplyVersionRule(context, resolvedVariant);

                if (ForgeSettings.instance.WriteBuildManifest)
                {
                    var manifest = BuildManifestWriter.CreateManifest(forgeProfile, context);

                    foreach (var plugin in plugins)
                    {
                        if (plugin is IForgeManifestContributor contributor)
                        {
                            contributor.ContributeToManifest(manifest, context);
                        }
                    }

                    BuildManifestWriter.WriteManifest(manifest);
                }

                Debug.Log($"[Build Forge] Starting build: {buildName} -> {outputPath}");
                // Unity skips native postprocessors when a build fails. XR uses
                // those callbacks to remove its temporary preloaded settings.
                // Capture after Forge pre-hooks, so their own additions remain
                // available to their post-hooks and normal restoration.
                var preloadedAssets = PlayerSettings.GetPreloadedAssets();
                try
                {
                    report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
                    {
                        buildProfile = buildProfile,
                        locationPathName = outputPath,
                        options = context.DevelopmentBuild ? BuildOptions.Development : BuildOptions.None,
                    });
                }
                finally
                {
                    try { PlayerSettings.SetPreloadedAssets(preloadedAssets); }
                    catch (Exception e)
                    {
                        var msg = $"Restoring preloaded assets failed: {e.Message}";
                        context.PostBuildFailures.Add(msg);
                        Debug.LogError($"[Build Forge] {msg}\n{e.StackTrace}");
                    }
                }

                context.Succeeded = report.summary.result == BuildResult.Succeeded;

                LogBuildSummary(buildName, report);
            }
            catch (Exception e)
            {
                context.Succeeded = false;
                Debug.LogError($"[Build Forge] Build exception: {e.Message}\n{e.StackTrace}");
                throw;
            }
            finally
            {
                CurrentContext = null;
                CurrentPlugins = Array.Empty<IForgePlugin>();

                if (versionBeforeRule != null)
                {
                    try
                    {
                        PlayerSettings.bundleVersion = versionBeforeRule;
                    }
                    catch (Exception e)
                    {
                        var msg = $"Restoring the version failed: {e.Message}";
                        context.PostBuildFailures.Add(msg);
                        Debug.LogError($"[Build Forge] {msg}\n{e.StackTrace}");
                    }
                }

                for (int i = appliedPlugins.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        Debug.Log($"[Build Forge] Running post-build: {appliedPlugins[i].DisplayName}");
                        appliedPlugins[i].OnPostBuild(context);
                    }
                    catch (Exception e)
                    {
                        var msg = $"Post-build restore failed in {appliedPlugins[i].DisplayName}: {e.Message}";
                        context.PostBuildFailures.Add(msg);
                        Debug.LogError($"[Build Forge] {msg}\n{e.StackTrace}");
                    }
                }

                // Undo in reverse order of apply: the plugins above ran with the
                // variant applied, so one that snapshotted a setting in OnPreBuild
                // restores that state, and only then is the variant undone.
                try
                {
                    VariantPlayerSettings.Restore(playerSettingsBeforeVariant);
                }
                catch (Exception e)
                {
                    var msg = $"Restoring the variant rule's Player Settings failed: {e.Message}";
                    context.PostBuildFailures.Add(msg);
                    Debug.LogError($"[Build Forge] {msg}\n{e.StackTrace}");
                }
                try
                {
                    BuildVariants.EndBuild(buildProfile, definesBeforeVariant);
                    VariantBuildSettings.Restore(buildProfile, buildSettingsBeforeVariant);
                    if (productNameBeforeMark != null)
                        PlayerSettings.productName = productNameBeforeMark;
                    EditorUtility.SetDirty(buildProfile);
                    AssetDatabase.SaveAssetIfDirty(buildProfile);
                }
                catch (Exception e)
                {
                    var msg = $"Restoring variant settings failed: {e.Message}";
                    context.PostBuildFailures.Add(msg);
                    Debug.LogError($"[Build Forge] {msg}\n{e.StackTrace}");
                }

                // After every hook that can write into the destination. Only the
                // next cleanup depends on it, so a failure is a warning.
                if (outputPreparation != null)
                {
                    try
                    {
                        BuildOutputDirectory.Record(outputPreparation, report);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Build Forge] Recording this build's output files failed: {e.Message}. " +
                                         "The next build removes only the named artifact.");
                    }
                }

                if (suspension != null)
                {
                    try
                    {
                        ForgeEditorState.ResumeAfterBuild(suspension);
                    }
                    catch (Exception e)
                    {
                        var msg = $"Re-applying the editor profile after the build failed: {e.Message}";
                        context.PostBuildFailures.Add(msg);
                        Debug.LogError($"[Build Forge] {msg}\n{e.StackTrace}");
                    }
                }

                // Persist the restored live settings and the native profile's
                // separate embedded copy, after plugin restoration and resume.
                if (appliedPlugins.Count > 0 || suspension != null || productNameBeforeMark != null || buildSettingsBeforeVariant != null
                    || versionBeforeRule != null || playerSettingsBeforeVariant?.Changed == true)
                {
                    try
                    {
                        PlayerSettingsPersistence.SaveRestoredSettings();
                    }
                    catch (Exception e)
                    {
                        var msg = $"Saving restored settings to disk failed: {e.Message}";
                        context.PostBuildFailures.Add(msg);
                        Debug.LogError($"[Build Forge] {msg}\n{e.StackTrace}");
                    }
                }

                if (ForgeSettings.instance.WriteBuildManifest)
                {
                    try
                    {
                        BuildManifestWriter.CleanupManifest();
                    }
                    catch (Exception e)
                    {
                        var msg = $"Manifest cleanup failed: {e.Message}";
                        context.PostBuildFailures.Add(msg);
                        Debug.LogError($"[Build Forge] {msg}\n{e.StackTrace}");
                    }
                }

                IsForgeBuildInProgress = false;

                RevealBuildIfWanted(context, report);

                if (context.PostBuildFailures.Count > 0)
                {
                    Debug.LogError(
                        $"[Build Forge] {context.PostBuildFailures.Count} post-build restore/cleanup step(s) failed. " +
                        "The build artifact is complete, but the project may be left in a modified state — " +
                        "review the errors above and revert affected files before committing.");
                }

                Debug.Log("[Build Forge] Build process complete.");
            }

            return report;
        }

        const string VariantArg = "-forgeVariant";
        const string VersionArg = "-forgeVersion";
        const string VersionCodeArg = "-forgeVersionCode";
        const string FormerVersionCodeArg = "-versionCode";
        static readonly string[] ValueArgs = { VariantArg, VersionArg, VersionCodeArg };

        /// <summary>
        /// The values of the CLI build's arguments, keyed by their spelling above;
        /// names match case-insensitively, like -forgeCI. Strict, because an
        /// argument the build ignores still yields a successful build of the wrong
        /// thing: an argument starting with -forge that the build does not take
        /// (a typo, the removed -forgeProfile, or Activate's -forgeBuildProfile),
        /// the former -versionCode, and one of these without a value (last on the
        /// line, followed by another flag, or empty, as an unset CI variable
        /// produces) are errors.
        /// </summary>
        internal static Dictionary<string, string> ParseCommandLine(IReadOnlyList<string> args, out string error)
        {
            error = null;
            var values = new Dictionary<string, string>();
            for (int i = 0; args != null && i < args.Count; i++)
            {
                var name = ValueArgs.FirstOrDefault(arg => string.Equals(arg, args[i], StringComparison.OrdinalIgnoreCase));
                if (name == null)
                {
                    if (string.Equals(args[i], FormerVersionCodeArg, StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"{FormerVersionCodeArg} was renamed to {VersionCodeArg}.";
                        return null;
                    }
                    if (string.Equals(args[i], ForgeEditorState.BuildProfileArg, StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"{ForgeEditorState.BuildProfileArg} is BuildForge.CommandLine.Activate's argument. " +
                                "The build builds the active Unity Build Profile; pass -activeBuildProfile.";
                        return null;
                    }
                    if (args[i].StartsWith("-forge", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(args[i], BuildEnvironment.CommandLineArg, StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"Unknown argument '{args[i]}'. Build Forge takes {VariantArg}, {VersionArg}, " +
                                $"{VersionCodeArg} and {BuildEnvironment.CommandLineArg}.";
                        return null;
                    }
                    continue;
                }

                var value = i + 1 < args.Count ? args[i + 1] : null;
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("-"))
                {
                    error = $"{name} needs a value" + (name == VariantArg
                        ? $"; for the default build pass {VariantArg} {BuildVariants.DefaultVariantName} or leave the argument out."
                        : ".");
                    return null;
                }
                values[name] = value;
                i++;
            }
            return values;
        }

        /// <summary>
        /// CI entry point. Builds the Build Forge profile that references the active
        /// Unity Build Profile (exactly one, see ForgeProfileLookup); pass
        /// -activeBuildProfile to activate it at editor startup (see GetCICommand
        /// for the full command line).
        /// </summary>
        internal static void BuildFromCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            var values = ParseCommandLine(args, out var argumentError);
            argumentError ??= BuildEnvironment.ValueError(
                args, System.Environment.GetEnvironmentVariable(BuildEnvironment.EnvVarName));
            if (argumentError != null)
            {
                Debug.LogError($"[Build Forge] {argumentError}");
                EditorApplication.Exit(1);
                return;
            }
            values.TryGetValue(VariantArg, out var variant);
            values.TryGetValue(VersionArg, out var version);
            values.TryGetValue(VersionCodeArg, out var versionCode);

            // The active Build Profile identifies the Build Forge Profile. Resolution
            // is strict: none or more than one is an error, never a silent pick.
            var activeBuildProfile = BuildProfile.GetActiveBuildProfile();
            if (activeBuildProfile == null)
            {
                Debug.LogError("[Build Forge] No Unity Build Profile is active. Pass " +
                               "-activeBuildProfile \"<path to the Unity Build Profile asset>\" to activate " +
                               "it at editor startup.");
                EditorApplication.Exit(1);
                return;
            }

            var profile = ForgeProfileLookup.ResolveForBuildProfile(
                activeBuildProfile, ForgeProfileLookup.FindAll(), out var resolveError);
            if (profile == null)
            {
                Debug.LogError($"[Build Forge] {resolveError}");
                EditorApplication.Exit(1);
                return;
            }

            // Strict like the profile: an unknown variant never silently builds the plain profile.
            var resolvedVariant = BuildVariants.Resolve(variant, ForgeSettings.instance.BuildVariants, out var variantError);
            variantError ??= BuildVariants.ProfileError(profile, resolvedVariant);
            if (variantError != null)
            {
                Debug.LogError($"[Build Forge] {variantError}");
                EditorApplication.Exit(1);
                return;
            }

            int parsedVersionCode = 0;
            if (!string.IsNullOrEmpty(versionCode) && !int.TryParse(versionCode, out parsedVersionCode))
            {
                Debug.LogError($"[Build Forge] Invalid {VersionCodeArg}: {versionCode} (must be an integer).");
                EditorApplication.Exit(1);
                return;
            }

            string originalVersion = null;
            bool versionApplied = false;
            int originalVersionCode = 0;
            bool versionCodeApplied = false;

            int exitCode = 1;
            try
            {
                if (!string.IsNullOrEmpty(version))
                {
                    Debug.Log($"[Build Forge] Overriding bundleVersion: {version}");
                    originalVersion = PlayerSettings.bundleVersion;
                    versionApplied = true;
                    PlayerSettings.bundleVersion = version;
                }

                if (!string.IsNullOrEmpty(versionCode))
                {
                    Debug.Log($"[Build Forge] Overriding Android versionCode: {parsedVersionCode}");
                    originalVersionCode = PlayerSettings.Android.bundleVersionCode;
                    versionCodeApplied = true;
                    PlayerSettings.Android.bundleVersionCode = parsedVersionCode;
                }

                var report = RunBuild(profile, variant, out var context);
                var built = report != null && report.summary.result == BuildResult.Succeeded;
                var restoreClean = context.PostBuildFailures.Count == 0;

                if (built && !restoreClean)
                    Debug.LogError(
                        "[Build Forge] Build succeeded but post-build restoration failed; failing the run so " +
                        "CI does not proceed with a modified project. See the errors above.");

                exitCode = built && restoreClean ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Build Forge] Build failed with exception: {e.Message}\n{e.StackTrace}");
                exitCode = 1;
            }
            finally
            {
                // RunBuild's own post-build flush ran while these overrides were
                // still in memory, so they are on disk now: restore in memory and
                // flush again — otherwise a CLI run against a non-ephemeral checkout
                // permanently modifies the profile's Player Settings.
                try
                {
                    if (versionApplied)
                        PlayerSettings.bundleVersion = originalVersion;
                    if (versionCodeApplied)
                        PlayerSettings.Android.bundleVersionCode = originalVersionCode;
                    if (versionApplied || versionCodeApplied)
                    {
                        PlayerSettingsPersistence.SaveRestoredSettings();
                        Debug.Log("[Build Forge] Restored original version settings.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        $"[Build Forge] Restoring version settings failed: {e.Message}\n{e.StackTrace}");
                    exitCode = 1;
                }
            }

            EditorApplication.Exit(exitCode);
        }

        /// <summary>
        /// Returns the CI command line string to build the given profile.
        /// </summary>
        public static string GetCICommand(ForgeProfile profile, string variant = null)
        {
            if (profile == null || profile.BuildProfile == null)
                return "";

            return FormatCICommand(CIProjectPath(), AssetDatabase.GetAssetPath(profile.BuildProfile), variant);
        }

        /// <summary>
        /// The profile's CI commands, with one project path lookup: the build
        /// command, for a workspace that only builds profiles of this platform,
        /// and Activate followed by the build, for a workspace that also builds
        /// other platforms' profiles.
        /// </summary>
        internal static (string OneWorkspace, string SharedWorkspace) GetCICommands(ForgeProfile profile, string variant)
        {
            if (profile == null || profile.BuildProfile == null)
                return ("", "");

            var projectPath = CIProjectPath();
            var buildProfilePath = AssetDatabase.GetAssetPath(profile.BuildProfile);
            return (FormatCICommand(projectPath, buildProfilePath, variant),
                    FormatSharedWorkspaceCICommand(projectPath, buildProfilePath, variant));
        }

        static string CIProjectPath() =>
            ProjectPathForCI() ?? System.IO.Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');

        internal static string FormatCICommand(string projectPath, string buildProfilePath, string variant)
        {
            var variantArg = string.IsNullOrEmpty(variant) ? "" : $" -forgeVariant \"{variant}\"";
            return $"unity run \"{projectPath}\" --non-interactive -- {ForwardedCIArguments}" +
                   $" -activeBuildProfile \"{buildProfilePath}\"{variantArg}";
        }

        internal static string FormatActivateCommand(string projectPath, string buildProfilePath) =>
            $"unity run \"{projectPath}\" --non-interactive -- {ForwardedActivateArguments}" +
            $" {ForgeEditorState.BuildProfileArg} \"{buildProfilePath}\"";

        /// <summary>Activate, then the build: one command per line.</summary>
        internal static string FormatSharedWorkspaceCICommand(string projectPath, string buildProfilePath, string variant) =>
            FormatActivateCommand(projectPath, buildProfilePath) + "\n" + FormatCICommand(projectPath, buildProfilePath, variant);

        /// <summary>
        /// Editor arguments forwarded by Unity CLI. The CLI supplies batch mode,
        /// project path and quit. This synchronous entry point finishes cleanup
        /// and explicitly exits with its result before returning to Unity.
        /// </summary>
        public const string ForwardedCIArguments = ForwardedEditorArguments + " -executeMethod BuildForge.CommandLine.Build";

        /// <summary>The arguments ForwardedCIArguments forwards, for BuildForge.CommandLine.Activate.</summary>
        internal const string ForwardedActivateArguments = ForwardedEditorArguments + " -executeMethod BuildForge.CommandLine.Activate";

        const string ForwardedEditorArguments = "-nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion";

        /// <summary>
        /// Legacy direct-Editor arguments retained for existing callers.
        /// The generated CI command uses ForwardedCIArguments instead.
        /// No -quit is needed when directly launching the Editor.
        /// No -accept-apiupdate either: it would let the API Updater rewrite
        /// source on the agent and build from code nobody committed; a compile
        /// failure is the right outcome there.
        /// </summary>
        public const string StaticCIArguments =
            "-batchmode -nographics -silent-crashes -logFile - -executeMethod BuildForge.CommandLine.Build";

        /// <summary>
        /// The project directory relative to the git repository root, for
        /// unity run: "." when the project is the repository root, "./Client"
        /// for a project in a subfolder, null outside a git repository (the
        /// generated command then uses the absolute project path). CI runs at
        /// the repository root, which is often not the Unity project. Git runs
        /// once per domain load: the build window asks on every repaint, about
        /// 20 times a second while it is open, and the project's place in its
        /// repository doesn't change while the editor runs.
        /// </summary>
        internal static string ProjectPathForCI()
        {
            if (!projectPathForCIResolved)
            {
                var projectDir = System.IO.Path.GetDirectoryName(Application.dataPath);
                var repoRoot = Plugins.GitHelper.Run("rev-parse --show-toplevel");
                projectPathForCI = RelativeProjectPath(projectDir, repoRoot);
                projectPathForCIResolved = true;
            }
            return projectPathForCI;
        }

        static string projectPathForCI;
        static bool projectPathForCIResolved;

        /// <summary>Pure part of ProjectPathForCI: explicit relative paths with forward slashes, "." for the root itself, null when not inside the repository.</summary>
        internal static string RelativeProjectPath(string projectDir, string repoRoot)
        {
            if (string.IsNullOrWhiteSpace(projectDir) || string.IsNullOrWhiteSpace(repoRoot))
                return null;
            var project = Normalize(projectDir);
            var root = Normalize(repoRoot);
            if (string.Equals(project, root, StringComparison.OrdinalIgnoreCase))
                return ".";
            if (!project.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return null;
            // Unity CLI also accepts registered project names. The prefix makes
            // this a path relative to the checkout on every supported host OS.
            return "./" + project.Substring(root.Length + 1);

            static string Normalize(string path)
                => System.IO.Path.GetFullPath(path.Trim()).Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// Logs the variant rule's Build Configuration for this build, and warns
        /// about development options it turns on that this build doesn't use.
        /// </summary>
        static void LogBuildConfiguration(BuildVariantRule rule, BuildProfile buildProfile)
        {
            var settings = BuildVariants.DescribeBuildConfiguration(rule, buildProfile);
            if (settings.Count > 0)
                Debug.Log($"[Build Forge] Build configuration from variant rule '{rule.Variant}': {string.Join(", ", settings)}.");
            var unused = VariantBuildSettings.UnusedDevelopmentOptions(buildProfile, rule);
            if (unused.Count > 0)
                Debug.LogWarning($"[Build Forge] Variant rule '{rule.Variant}' turns on {string.Join(", ", unused)}, which this build " +
                                 "doesn't use: Unity uses them only in development builds, and Wait For Managed Debugger only with Script Debugging.");
        }

        /// <summary>
        /// Sets Player Settings > Version from the build's variant rule for the
        /// build. Returns the version to restore, or null when the rule left it
        /// alone; a rule that cannot be filled in warns and keeps the version.
        /// </summary>
        static string ApplyVersionRule(ForgeBuildContext context, string variant)
        {
            var template = BuildVariants.RuleFor(variant)?.Version;
            if (string.IsNullOrWhiteSpace(template))
                return null;
            var current = PlayerSettings.bundleVersion;
            var version = BuildVariants.FormatVersion(template, current, BuildManifestWriter.BuildNumberFor(context),
                BuildVariants.EffectiveName(variant, BuildVariants.Configured.Count > 0), out var error);
            if (error != null)
            {
                Debug.LogWarning($"[Build Forge] {error} The build keeps version {current}.");
                return null;
            }
            if (version == current)
                return null;
            PlayerSettings.bundleVersion = version;
            Debug.Log($"[Build Forge] Version {version} for this build, from the variant rule's {template}.");
            return current;
        }

        /// <summary>
        /// What Unity's own Build button does afterwards: select the built file in
        /// the platform's file browser (EditorUtility.RevealInFinder is Unity's
        /// cross-platform call: Explorer, Finder, or the Linux file manager).
        /// Only for successful, non-CI builds, and only when the setting is on;
        /// a failure to open a window never fails the build.
        /// </summary>
        static void RevealBuildIfWanted(ForgeBuildContext context, BuildReport report)
        {
            if (report == null || !context.Succeeded || context.IsCI ||
                !ForgeSettings.instance.RevealBuildInFileBrowser)
                return;

            var path = report.summary.outputPath;
            if (string.IsNullOrEmpty(path))
                return;
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                path = System.IO.Path.GetDirectoryName(path);

            try
            {
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Build Forge] Could not reveal the build in the file browser: {e.Message}");
            }
        }

        static void LogBuildSummary(string profileName, BuildReport report)
        {
            var s = report.summary;
            var time = s.totalTime;
            var timeStr = time.TotalMinutes >= 1
                ? $"{(int)time.TotalMinutes}m {time.Seconds}s"
                : $"{time.TotalSeconds:F1}s";
            var size = FormatBytes(s.totalSize);

            if (s.result == BuildResult.Succeeded)
                Debug.Log($"[Build Forge] {profileName} — Succeeded | {timeStr} | {size} | {s.totalWarnings} warnings");
            else
                Debug.LogError($"[Build Forge] {profileName} — {s.result} | {timeStr} | {s.totalErrors} errors | {s.totalWarnings} warnings");
        }

        static string FormatBytes(ulong bytes)
        {
            if (bytes >= 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F1} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }
    }

    /// <summary>
    /// Utility to extract build target from a BuildProfile.
    /// </summary>
    internal static class BuildProfileUtility
    {
        public static BuildTarget GetBuildTarget(BuildProfile profile)
        {
            var so = new SerializedObject(profile);
            var platformProp = so.FindProperty("m_BuildTarget");
            if (platformProp != null)
            {
                return (BuildTarget)platformProp.intValue;
            }

            return EditorUserBuildSettings.activeBuildTarget;
        }

        /// <summary>
        /// True for a Dedicated Server profile, whose per-platform Player Settings
        /// Unity keys by NamedBuildTarget.Server rather than the Standalone group.
        /// </summary>
        public static bool IsDedicatedServer(BuildProfile profile)
        {
            var subtarget = new SerializedObject(profile).FindProperty("m_Subtarget");
            return subtarget != null
                && BuildPipeline.GetBuildTargetGroup(GetBuildTarget(profile)) == BuildTargetGroup.Standalone
                && subtarget.intValue == (int)StandaloneBuildSubtarget.Server;
        }
    }
}
