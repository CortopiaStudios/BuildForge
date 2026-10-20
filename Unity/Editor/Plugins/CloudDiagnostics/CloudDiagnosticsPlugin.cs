using System.Runtime.CompilerServices;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor.Build.Profile;
using UnityEditor.CrashReporting;
using UnityEditor;
using UnityEngine;

[assembly: InternalsVisibleTo("BuildForge.Tests.Editor")]

namespace BuildForge.Editor.Plugins
{
    /// <summary>
    /// Controls Unity Cloud Diagnostics crash reporting per build:
    /// CrashReportingSettings.enabled is set on pre-build from the configured
    /// mode and restored on post-build. Crash reporting is one of the two
    /// settings that make an Android build upload its IL2CPP symbols; the other,
    /// Diagnostics Data in a project linked to Unity Cloud, is left alone.
    /// </summary>
    [ForgePlugin]
    internal class CloudDiagnosticsPlugin : IForgePlugin
    {
        const string ConfigKey = "CloudDiagnostics";

        bool originalEnabled;
        bool applied;

        public string DisplayName => "Cloud Diagnostics";
        public string Description => "Turn Unity Cloud Diagnostics crash reporting on only for CI builds, always, or never.";
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
                    "With crash reporting on, an Android build also uploads its IL2CPP symbols to Unity. " +
                    "With it off, the build still uploads them while Diagnostics Data (Project Settings > " +
                    "Services > Diagnostics) is on in a project linked to Unity Cloud. " +
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
