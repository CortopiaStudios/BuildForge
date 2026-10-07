using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.CrashReporting;
using UnityEngine;

namespace BuildForge.Editor.Plugins
{
    /// <summary>
    /// Controls Unity Cloud Diagnostics crash reporting per build. Crash reporting
    /// is what triggers the automatic IL2CPP symbol upload to Unity's symbol
    /// server at build time — a network round-trip that is pointless for local
    /// builds. CrashReportingSettings.enabled is set on pre-build from the
    /// configured mode and restored on post-build.
    /// </summary>
    [ForgePlugin]
    internal class CloudDiagnosticsPlugin : IForgePlugin
    {
        const string ConfigKey = "CloudDiagnostics";

        bool originalEnabled;
        bool applied;

        public string DisplayName => "Cloud Diagnostics";
        public string Description => "Enable crash reporting (and its symbol upload) only for CI builds, always, or never.";
        public int Order => 500;

        public bool IsApplicable(BuildProfile profile) => true;

        public bool? IsEnabled(ForgeProfile forgeProfile) => GetConfig(forgeProfile).Enabled;

        public void SetEnabled(ForgeProfile forgeProfile, bool enabled)
        {
            var config = GetConfig(forgeProfile);
            config.Enabled = enabled;
            SaveConfig(forgeProfile, config);
        }

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI)
        {
            var mode = GetConfig(forgeProfile).CrashReportingMode;
            var on = ResolveEnabled(mode, isCI) ? "on" : "off";
            var modeText = mode == CrashReportingMode.CIOnly ? "CI only" : mode.ToString().ToLowerInvariant();
            return $"crash reporting {on} ({modeText}; {(isCI ? "CI" : "local")} build)";
        }

        public void OnPreBuild(ForgeBuildContext context)
        {
            var config = GetConfig(context.ForgeProfile);
            var desired = ResolveEnabled(config.CrashReportingMode, context.IsCI);

            originalEnabled = CrashReportingSettings.enabled;
            CrashReportingSettings.enabled = desired;
            applied = true;

            Debug.Log($"[Build Forge/CloudDiagnostics] Crash reporting {(desired ? "enabled" : "disabled")} " +
                $"for this build (mode: {config.CrashReportingMode}, CI: {context.IsCI}).");
        }

        public void OnPostBuild(ForgeBuildContext context)
        {
            if (!applied)
                return;

            CrashReportingSettings.enabled = originalEnabled;
            applied = false;

            Debug.Log("[Build Forge/CloudDiagnostics] Restored crash reporting setting.");
        }

        public void OnDrawProfileGUI(ForgeProfile forgeProfile)
        {
            var config = GetConfig(forgeProfile);

            var newMode = (CrashReportingMode)EditorGUILayout.EnumPopup(
                new GUIContent("Crash Reporting",
                    "When crash reporting is enabled for a build, Unity also uploads the build's IL2CPP " +
                    "symbols to its symbol server. Disabled builds neither upload symbols nor report crashes. " +
                    "CI is detected via batch mode, -forgeCI, or the FORGE_CI environment variable."),
                config.CrashReportingMode);
            if (newMode != config.CrashReportingMode)
            {
                config.CrashReportingMode = newMode;
                SaveConfig(forgeProfile, config);
            }

            if (config.Enabled)
            {
                var local = ResolveEnabled(config.CrashReportingMode, false) ? "on" : "off";
                var ci = ResolveEnabled(config.CrashReportingMode, true) ? "on" : "off";
                var project = CrashReportingSettings.enabled ? "on" : "off";
                EditorGUILayout.LabelField(" ",
                    $"Local build: {local}, CI build: {ci} (project setting: {project})",
                    EditorStyles.miniLabel);
            }
        }

        internal static bool ResolveEnabled(CrashReportingMode mode, bool isCI)
        {
            switch (mode)
            {
                case CrashReportingMode.Always: return true;
                case CrashReportingMode.CIOnly: return isCI;
                default: return false;
            }
        }

        static CloudDiagnosticsConfig GetConfig(ForgeProfile profile)
        {
            return profile.GetPluginConfig<CloudDiagnosticsConfig>(ConfigKey);
        }

        static void SaveConfig(ForgeProfile profile, CloudDiagnosticsConfig config)
        {
            profile.SetPluginConfig(ConfigKey, config);
        }
    }
}
