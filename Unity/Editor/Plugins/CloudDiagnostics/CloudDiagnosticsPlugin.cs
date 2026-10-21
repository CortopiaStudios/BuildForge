using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor.Build.Profile;
using UnityEditor.CrashReporting;
using UnityEditor.EngineDiagnostics;
using UnityEditor;
using UnityEngine;

[assembly: InternalsVisibleTo("BuildForge.Tests.Editor")]

namespace BuildForge.Editor.Plugins
{
    /// <summary>
    /// Sets both of Unity's crash reporting settings per build from the configured
    /// mode, on pre-build, and restores them on post-build: Cloud Diagnostics crash
    /// reporting (CrashReportingSettings.enabled) and Diagnostics Data. Either one
    /// makes an Android build upload its IL2CPP symbols.
    /// </summary>
    [ForgePlugin]
    internal class CloudDiagnosticsPlugin : IForgePlugin
    {
        const string ConfigKey = "CloudDiagnostics";

        /// <summary>
        /// The Unity Build Profile's own Diagnostics Data, which a build follows
        /// (BuildProfileContext.GetActiveBuildProfileEngineDiagnosticsState). Unity's API
        /// sets only the project default, EngineDiagnosticsSettings.enabled, which a profile
        /// set to Enabled or Disabled overrides, so the plugin sets the profile's value. The
        /// values are those of Unity's internal BuildProfileEngineDiagnosticsState.
        /// </summary>
        internal const string DiagnosticsDataPath = "m_PlatformBuildProfile.m_InsightsSettingsContainer.m_BuildProfileEngineDiagnosticsState";
        internal const int DiagnosticsDisabled = 0;
        internal const int DiagnosticsEnabled = 1;
        internal const int DiagnosticsProjectDefault = 2;

        const string ConnectSettingsFile = "ProjectSettings/UnityConnectSettings.asset";

        /// <summary>The file Unity keeps both crash reporting settings' project values in.</summary>
        internal static string ConnectSettingsPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), ConnectSettingsFile);

        bool originalEnabled;
        bool applied;
        byte[] connectSettingsBefore;
        BuildProfile diagnosticsProfile;
        int originalDiagnostics;

        public string DisplayName => "Cloud Diagnostics";
        public string Description => "Turn Unity's crash reporting and Diagnostics Data on only for CI builds, always, or never.";
        public int Order => 500;

        public bool IsApplicable(BuildProfile buildProfile) => true;

        public bool? IsEnabled(ForgeProfile forgeProfile) => GetConfig(forgeProfile).Enabled;

        public void SetEnabled(ForgeProfile forgeProfile, bool enabled)
        {
            var config = GetConfig(forgeProfile);
            config.Enabled = enabled;
            SaveConfig(forgeProfile, config);
        }

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI, string variant)
        {
            var mode = GetConfig(forgeProfile).CrashReportingMode;
            var on = ResolveEnabled(mode, isCI) ? "on" : "off";
            var modeText = mode == CrashReportingMode.CIOnly ? "CI only" : mode.ToString().ToLowerInvariant();
            return $"crash reporting and Diagnostics Data {on} ({modeText}; {(isCI ? "CI" : "local")} build)";
        }

        public void OnPreBuild(ForgeBuildContext context)
        {
            var config = GetConfig(context.ForgeProfile);
            var desired = ResolveEnabled(config.CrashReportingMode, context.IsCI);

            // First, since it stops the build where Unity stores no such setting, before anything changed.
            originalDiagnostics = SetDiagnosticsData(context.BuildProfile, desired ? DiagnosticsEnabled : DiagnosticsDisabled);
            diagnosticsProfile = context.BuildProfile;
            originalEnabled = CrashReportingSettings.enabled;
            connectSettingsBefore = File.Exists(ConnectSettingsPath) ? File.ReadAllBytes(ConnectSettingsPath) : null;
            CrashReportingSettings.enabled = desired;
            applied = true;

            Debug.Log($"[Build Forge/CloudDiagnostics] Crash reporting and Diagnostics Data {(desired ? "enabled" : "disabled")} " +
                $"for this build (mode: {config.CrashReportingMode}, CI: {context.IsCI}).");
        }

        public void OnPostBuild(ForgeBuildContext context)
        {
            // Each one is put back even if another fails, and the fields are cleared either
            // way, so the next build doesn't take a leftover build value for the project's.
            if (diagnosticsProfile == null && !applied)
                return;
            try
            {
                if (diagnosticsProfile != null)
                    SetDiagnosticsData(diagnosticsProfile, originalDiagnostics);
            }
            finally
            {
                diagnosticsProfile = null;
                if (applied)
                {
                    applied = false;
                    var before = connectSettingsBefore;
                    connectSettingsBefore = null;
                    try
                    {
                        CrashReportingSettings.enabled = originalEnabled;
                    }
                    finally
                    {
                        if (before != null)
                            RestoreConnectSettingsFile(before);
                    }
                }
            }

            Debug.Log("[Build Forge/CloudDiagnostics] Restored crash reporting and Diagnostics Data.");
        }

        /// <summary>
        /// Puts ProjectSettings/UnityConnectSettings.asset back as it was before the build. A build with
        /// the settings changed can write the file: with crash reporting as the build had it, and with values
        /// Unity holds only for the session, such as m_Enabled, which a batch-mode editor that isn't signed in
        /// has off while both settings are off. Setting them back doesn't reliably reach the file: in a project
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
                    "Sets both of Unity's crash reporting settings for the build, and restores them afterwards: " +
                    "Cloud Diagnostics crash reporting, and this Unity Build Profile's Diagnostics Data. Either one " +
                    "makes an Android build upload its IL2CPP symbols to Unity, and Diagnostics Data makes the built " +
                    "player report crashes in a project linked to Unity Cloud. " +
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
                var crashReporting = CrashReportingSettings.enabled ? "on" : "off";
                EditorGUILayout.LabelField(" ",
                    $"Local build: {local}, CI build: {ci} (now: crash reporting {crashReporting}, " +
                    $"Diagnostics Data {DescribeDiagnosticsData(forgeProfile.BuildProfile)})",
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

        /// <summary>
        /// Sets the profile's own Diagnostics Data and returns the value it had. Throws where
        /// Unity stores no such setting in the profile, as a version that renamed it would,
        /// since the build would otherwise go out with Diagnostics Data as the profile has it.
        /// </summary>
        internal static int SetDiagnosticsData(BuildProfile profile, int state)
        {
            var so = new SerializedObject(profile);
            var property = so.FindProperty(DiagnosticsDataPath);
            if (property == null)
                throw new InvalidOperationException(
                    $"[Build Forge/CloudDiagnostics] Unity Build Profile '{profile.name}' stores no Diagnostics Data " +
                    $"setting ({DiagnosticsDataPath}) in Unity {Application.unityVersion}, so Build Forge cannot set it " +
                    "for this build. Update Build Forge for this Unity version.");
            var previous = property.intValue;
            property.intValue = state;
            so.ApplyModifiedPropertiesWithoutUndo();
            return previous;
        }

        /// <summary>The profile's own Diagnostics Data for the foldout, with the project default it may defer to.</summary>
        static string DescribeDiagnosticsData(BuildProfile profile)
        {
            var property = profile != null ? new SerializedObject(profile).FindProperty(DiagnosticsDataPath) : null;
            switch (property?.intValue)
            {
                case DiagnosticsEnabled: return "on";
                case DiagnosticsDisabled: return "off";
                case DiagnosticsProjectDefault: return (EngineDiagnosticsSettings.enabled ? "on" : "off") + " (project default)";
                default: return "unknown";
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
