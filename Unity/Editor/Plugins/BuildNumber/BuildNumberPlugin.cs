using System;
using System.Collections.Generic;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Runtime;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.Plugins
{
    /// <summary>
    /// Sets the platform build number from the git commit count, a
    /// configurable environment variable (default BUILD_NUMBER), or that
    /// variable on CI and the commit count locally.
    /// Android.bundleVersionCode and iOS.buildNumber are set on pre-build
    /// and restored on post-build. The bundleVersion string is not touched.
    /// </summary>
    [ForgePlugin]
    internal class BuildNumberPlugin : IForgePlugin, IForgeManifestContributor
    {
        const string ConfigKey = "BuildNumber";

        int originalAndroidVersionCode;
        string originalIOSBuildNumber;
        int appliedBuildNumber;
        bool applied;

        int cachedPreviewBase = -2;
        bool cachedPreviewShallow;
        double cachedPreviewTime;

        public string DisplayName => "Build Number";
        public string Description => "Set Android.bundleVersionCode and iOS.buildNumber automatically.";
        public int Order => 400;

        public bool IsApplicable(BuildProfile profile) => true;

        public bool? IsEnabled(ForgeProfile forgeProfile) => GetConfig(forgeProfile).Enabled;

        public void SetEnabled(ForgeProfile forgeProfile, bool enabled)
        {
            var config = GetConfig(forgeProfile);
            config.Enabled = enabled;
            SaveConfig(forgeProfile, config);
        }

        const string ShallowCloneMessage =
            "This is a shallow git clone — 'git rev-list --count HEAD' returns the truncated " +
            "fetch depth, not the real commit count. Fetch full history (git fetch --unshallow; " +
            "in GitHub Actions: actions/checkout with fetch-depth: 0) or switch the Build Number " +
            "source to an environment variable.";

        public IReadOnlyList<string> Validate(ForgeProfile forgeProfile)
            => Validate(forgeProfile, BuildEnvironment.IsCI);

        /// <param name="isCI">The CI detection a build from this editor sees.</param>
        internal IReadOnlyList<string> Validate(ForgeProfile forgeProfile, bool isCI)
        {
            var config = GetConfig(forgeProfile);
            var source = EffectiveSource(config.Source, isCI);
            if (source == BuildNumberSource.GitCommitCount && IsShallowRepository())
            {
                return new[] { ShallowCloneMessage };
            }

            // The build fails on it (OnPreBuild); say so before it starts.
            if (source == BuildNumberSource.EnvironmentVariable && GetBuildNumberEnvVar(config.EnvVarName) < 0)
            {
                return new[]
                {
                    $"The {config.EnvVarName} environment variable is missing or not a whole number of 0 or more, " +
                    "so a build of this profile will fail. Set it before starting Unity Hub or the editor, " +
                    "or disable Build Number for this profile."
                };
            }

            return Array.Empty<string>();
        }

        public void OnPreBuild(ForgeBuildContext context)
        {
            var config = GetConfig(context.ForgeProfile);
            var source = EffectiveSource(config.Source, context.IsCI);

            // A shallow clone yields a plausible-looking but wrong count (the
            // fetch depth) — the one failure mode worse than no number at all.
            if (source == BuildNumberSource.GitCommitCount && IsShallowRepository())
                throw new InvalidOperationException(
                    $"[Build Forge/BuildNumber] {ShallowCloneMessage}");

            var baseNumber = GetBaseNumber(source, config.EnvVarName);
            if (baseNumber < 0)
            {
                var sourceName = source == BuildNumberSource.GitCommitCount
                    ? "git commit count"
                    : $"{config.EnvVarName} environment variable";
                // The plugin is explicitly enabled: shipping a green build WITHOUT
                // the configured number is a silent failure, so fail loudly instead.
                throw new InvalidOperationException(
                    $"[Build Forge/BuildNumber] Build Number is enabled but {sourceName} could not " +
                    "be read. Fix the source or disable the plugin for this profile.");
            }

            if (!TryCalculateBuildNumber(baseNumber, config.Offset, out appliedBuildNumber))
                throw new InvalidOperationException(
                    $"[Build Forge/BuildNumber] Base {baseNumber} plus offset {config.Offset} " +
                    "must be between 0 and 2147483647. Fix the source or offset before building.");

            originalAndroidVersionCode = PlayerSettings.Android.bundleVersionCode;
            originalIOSBuildNumber = PlayerSettings.iOS.buildNumber;

            PlayerSettings.Android.bundleVersionCode = appliedBuildNumber;
            PlayerSettings.iOS.buildNumber = appliedBuildNumber.ToString();
            context.BuildNumber = appliedBuildNumber;
            applied = true;

            var sourceText = source == config.Source ? $"{source}" : $"{config.Source} → {source}";
            Debug.Log($"[Build Forge/BuildNumber] Set build number to {appliedBuildNumber} " +
                $"(source: {sourceText}, base: {baseNumber}, offset: {config.Offset}).");
        }

        public void OnPostBuild(ForgeBuildContext context)
        {
            if (!applied)
                return;

            PlayerSettings.Android.bundleVersionCode = originalAndroidVersionCode;
            PlayerSettings.iOS.buildNumber = originalIOSBuildNumber;
            applied = false;

            Debug.Log("[Build Forge/BuildNumber] Restored original build numbers.");
        }

        public void ContributeToManifest(BuildManifest manifest, ForgeBuildContext context)
        {
            if (!applied)
                return;

            var section = new ManifestSection("BuildNumber");
            section.Add("buildNumber", appliedBuildNumber.ToString());
            manifest.AddSection(section);
        }

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI)
        {
            var config = GetConfig(forgeProfile);
            var source = EffectiveSource(config.Source, isCI) == BuildNumberSource.GitCommitCount
                ? "git commit count"
                : $"${config.EnvVarName}";
            var description = config.Offset != 0
                ? $"build number from {source} + {config.Offset}"
                : $"build number from {source}";
            return config.Source == BuildNumberSource.EnvironmentVariableOnCI
                ? $"{description} ({(isCI ? "CI" : "local")} build)"
                : description;
        }

        public void OnDrawProfileGUI(ForgeProfile forgeProfile)
        {
            var config = GetConfig(forgeProfile);
            bool changed = false;

            var newSource = (BuildNumberSource)EditorGUILayout.EnumPopup(
                new GUIContent("Source", "Where to read the base build number from. For Env Var on CI, " +
                    "Git Count Locally, CI is detected via batch mode, -forgeCI, or the FORGE_CI environment variable."),
                config.Source);
            if (newSource != config.Source)
            {
                config.Source = newSource;
                changed = true;
            }

            if (config.Source != BuildNumberSource.GitCommitCount)
            {
                var newEnvVarName = EditorGUILayout.TextField(
                    new GUIContent("Env Var Name", "Name of the environment variable to read the base build number from."),
                    config.EnvVarName);
                if (newEnvVarName != config.EnvVarName)
                {
                    config.EnvVarName = newEnvVarName;
                    changed = true;
                }
            }

            var newOffset = EditorGUILayout.IntField(
                new GUIContent("Offset", "Added to the base number (use when migrating from another versioning scheme)."),
                config.Offset);
            if (newOffset != config.Offset)
            {
                config.Offset = newOffset;
                changed = true;
            }

            if (config.Enabled)
            {
                var isCI = BuildEnvironment.IsCI;
                var source = EffectiveSource(config.Source, isCI);
                if (cachedPreviewBase == -2 || EditorApplication.timeSinceStartup - cachedPreviewTime > 5)
                {
                    cachedPreviewBase = GetBaseNumber(source, config.EnvVarName);
                    cachedPreviewShallow = source == BuildNumberSource.GitCommitCount
                        && IsShallowRepository();
                    cachedPreviewTime = EditorApplication.timeSinceStartup;
                }
                var baseNumber = cachedPreviewBase;
                string current;
                if (cachedPreviewShallow)
                    current = "<shallow clone — commit count unreliable, build will fail>";
                else if (baseNumber >= 0)
                {
                    current = TryCalculateBuildNumber(baseNumber, config.Offset, out var number)
                        ? $"{number} (base: {baseNumber})"
                        : "<base + offset outside 0–2147483647, build will fail>";
                }
                else if (source == BuildNumberSource.GitCommitCount)
                    current = "<git not available>";
                else
                    current = $"<{config.EnvVarName} not set>";
                // Both outcomes, as Cloud Diagnostics shows them; only this
                // editor's side can be read here.
                var preview = config.Source != BuildNumberSource.EnvironmentVariableOnCI
                    ? $"Current: {current}"
                    : isCI
                        ? $"CI build: {current} · Local build: git commit count"
                        : $"Local build: {current} · CI build: ${config.EnvVarName}";
                EditorGUILayout.LabelField(" ", preview, EditorStyles.miniLabel);
            }

            if (changed)
            {
                cachedPreviewBase = -2;
                SaveConfig(forgeProfile, config);
            }
        }

        internal static bool TryCalculateBuildNumber(int baseNumber, int offset, out int number)
        {
            // Use a wider sum so neither the build nor its preview can wrap.
            var sum = (long)baseNumber + offset;
            number = 0;
            if (baseNumber < 0 || sum < 0 || sum > int.MaxValue)
                return false;
            number = (int)sum;
            return true;
        }

        /// <summary>
        /// The source a build reads: EnvironmentVariableOnCI is the environment
        /// variable on CI and the git commit count otherwise.
        /// </summary>
        internal static BuildNumberSource EffectiveSource(BuildNumberSource source, bool isCI)
        {
            if (source != BuildNumberSource.EnvironmentVariableOnCI)
                return source;
            return isCI ? BuildNumberSource.EnvironmentVariable : BuildNumberSource.GitCommitCount;
        }

        static int GetBaseNumber(BuildNumberSource source, string envVarName)
        {
            return source switch
            {
                BuildNumberSource.GitCommitCount => GetGitCommitCount(),
                BuildNumberSource.EnvironmentVariable => GetBuildNumberEnvVar(envVarName),
                _ => -1
            };
        }

        static int GetGitCommitCount()
        {
            return GitHelper.RunInt("rev-list --count HEAD") ?? -1;
        }

        static bool IsShallowRepository()
        {
            // "true"/"false" from git >= 2.15; null (not a repo / no git) is
            // treated as not-shallow — the commit-count read fails on its own then.
            return GitHelper.Run("rev-parse --is-shallow-repository") == "true";
        }

        static int GetBuildNumberEnvVar(string envVarName)
        {
            return ParseBuildNumber(Environment.GetEnvironmentVariable(envVarName));
        }

        /// <summary>
        /// The base number in an environment variable's value; negative when
        /// the value is missing, empty or not a whole number of 0 or more.
        /// </summary>
        internal static int ParseBuildNumber(string value)
        {
            if (!string.IsNullOrEmpty(value) && int.TryParse(value, out var number))
                return number;
            return -1;
        }

        static BuildNumberConfig GetConfig(ForgeProfile profile)
        {
            return profile.GetPluginConfig<BuildNumberConfig>(ConfigKey);
        }

        static void SaveConfig(ForgeProfile profile, BuildNumberConfig config)
        {
            profile.SetPluginConfig(ConfigKey, config);
        }
    }
}
