using System;
using System.IO;
using System.Linq;
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

        const string ConnectSettingsFile = "ProjectSettings/UnityConnectSettings.asset";

        /// <summary>The file Unity keeps crash reporting's project value in.</summary>
        internal static string ConnectSettingsPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), ConnectSettingsFile);

        bool originalEnabled;
        bool applied;
        byte[] connectSettingsBefore;

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
            connectSettingsBefore = File.Exists(ConnectSettingsPath) ? File.ReadAllBytes(ConnectSettingsPath) : null;
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
            if (connectSettingsBefore != null)
            {
                RestoreConnectSettingsFile(connectSettingsBefore);
                connectSettingsBefore = null;
            }

            Debug.Log("[Build Forge/CloudDiagnostics] Restored crash reporting setting.");
        }

        /// <summary>
        /// Puts ProjectSettings/UnityConnectSettings.asset back as it was before the build. A build that
        /// changes crash reporting can write the file: with crash reporting as the build had it, and with
        /// values Unity holds only for the session, such as m_Enabled, which a batch-mode editor that isn't
        /// signed in can have off. Setting crash reporting back doesn't reliably reach the file: in a project
        /// linked to Unity Cloud, Unity doesn't mark its settings changed. So the file gets its bytes back, and
        /// the settings object is marked unchanged, so that the runner's save after the plugins doesn't write
        /// this session's values again. Throws, failing the build's restore, where Unity has no such object.
        /// </summary>
        internal static void RestoreConnectSettingsFile(byte[] before)
        {
            if (!File.Exists(ConnectSettingsPath) || !File.ReadAllBytes(ConnectSettingsPath).SequenceEqual(before))
                File.WriteAllBytes(ConnectSettingsPath, before);
            var connectSettings = Unsupported.GetSerializedAssetInterfaceSingleton("UnityConnectSettings");
            if (connectSettings == null)
                throw new InvalidOperationException(
                    $"[Build Forge/CloudDiagnostics] Unity {Application.unityVersion} has no UnityConnectSettings object, so a " +
                    $"later save may write {ConnectSettingsFile} again with this session's values. Check the file before " +
                    "committing it, and update Build Forge for this Unity version.");
            EditorUtility.ClearDirty(connectSettings);
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
