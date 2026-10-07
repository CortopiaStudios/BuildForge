using System;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.Plugins
{
    [ForgePlugin]
    internal class AndroidKeystoreEnvPlugin : IForgePlugin
    {
        const string ConfigKey = "AndroidKeystoreEnv";
        const string GlobalConfigKey = "AndroidKeystoreEnv";

        bool applied;
        bool originalUseCustomKeystore;
        string originalKeystoreName;
        string originalKeystorePass;
        string originalKeyAliasName;
        string originalKeyAliasPass;

        public string DisplayName => "Android Keystore Env";
        public string Description => "Override Android signing settings from environment variables at build time.";
        public int Order => 600;

        public bool IsApplicable(BuildProfile profile)
        {
            var target = BuildProfileUtility.GetBuildTarget(profile);
            return target == BuildTarget.Android;
        }

        public string NotApplicableReason(BuildProfile profile) =>
            "Only applicable to Android build profiles.";

        public bool? IsEnabled(ForgeProfile forgeProfile) => GetProfileConfig(forgeProfile).Enabled;

        public void SetEnabled(ForgeProfile forgeProfile, bool enabled)
        {
            var config = GetProfileConfig(forgeProfile);
            config.Enabled = enabled;
            SaveProfileConfig(forgeProfile, config);
        }

        public void OnPreBuild(ForgeBuildContext context)
        {
            var profile = GetProfileConfig(context.ForgeProfile);
            var global = GetGlobalConfig();

            var keystorePathVar = profile.ResolveEnvVar(profile.KeystorePathMode, profile.KeystorePathEnvVar, global.KeystorePathEnvVar);
            var keystorePassVar = profile.ResolveEnvVar(profile.KeystorePassMode, profile.KeystorePassEnvVar, global.KeystorePassEnvVar);
            var keyAliasVar = profile.ResolveEnvVar(profile.KeyAliasMode, profile.KeyAliasEnvVar, global.KeyAliasEnvVar);
            var keyAliasPassVar = profile.ResolveEnvVar(profile.KeyAliasPassMode, profile.KeyAliasPassEnvVar, global.KeyAliasPassEnvVar);

            var keystorePath = GetEnvValue(keystorePathVar);
            var keystorePass = GetEnvValue(keystorePassVar);
            var keyAlias = GetEnvValue(keyAliasVar);
            var keyAliasPass = GetEnvValue(keyAliasPassVar);

            // Validate before mutating any PlayerSettings so a throw here leaves
            // nothing to restore. Env signing is an explicit per-profile opt-in:
            // a misconfiguration that would sign the build wrongly must fail the
            // build, not degrade silently.
            ThrowIfError(EmptyEnvVarError(keystorePathVar, keystorePath));
            ThrowIfError(EmptyEnvVarError(keystorePassVar, keystorePass));
            ThrowIfError(EmptyEnvVarError(keyAliasVar, keyAlias));
            ThrowIfError(EmptyEnvVarError(keyAliasPassVar, keyAliasPass));
            ThrowIfError(SigningStateError(
                PlayerSettings.Android.useCustomKeystore,
                PlayerSettings.Android.keystoreName,
                keystorePath,
                keystorePass != null || keyAlias != null || keyAliasPass != null,
                keystorePathVar));

            originalUseCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            originalKeystoreName = SerializedKeystoreName;
            originalKeystorePass = PlayerSettings.Android.keystorePass;
            originalKeyAliasName = PlayerSettings.Android.keyaliasName;
            originalKeyAliasPass = PlayerSettings.Android.keyaliasPass;

            int overrideCount = 0;

            overrideCount += Apply(keystorePath, keystorePathVar, v => {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = v;
            });
            overrideCount += Apply(keystorePass, keystorePassVar, v => PlayerSettings.Android.keystorePass = v);
            overrideCount += Apply(keyAlias, keyAliasVar, v => PlayerSettings.Android.keyaliasName = v);
            overrideCount += Apply(keyAliasPass, keyAliasPassVar, v => PlayerSettings.Android.keyaliasPass = v);

            // Nothing was overridden, so there is nothing to restore: writing the
            // same values back would still re-serialize the keystore name.
            applied = overrideCount > 0;

            if (overrideCount > 0)
                Debug.Log($"[Build Forge/AndroidKeystoreEnv] Applied {overrideCount} signing override(s) from environment variables.");
            else
                Debug.Log("[Build Forge/AndroidKeystoreEnv] Enabled but no signing environment variables are set. Using existing settings.");
        }

        public void OnPostBuild(ForgeBuildContext context)
        {
            if (!applied)
                return;

            PlayerSettings.Android.useCustomKeystore = originalUseCustomKeystore;
            SerializedKeystoreName = originalKeystoreName;
            PlayerSettings.Android.keystorePass = originalKeystorePass;
            PlayerSettings.Android.keyaliasName = originalKeyAliasName;
            PlayerSettings.Android.keyaliasPass = originalKeyAliasPass;
            applied = false;

            Debug.Log("[Build Forge/AndroidKeystoreEnv] Restored original signing settings.");
        }

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI)
        {
            var profile = GetProfileConfig(forgeProfile);
            var global = GetGlobalConfig();
            string Var(EnvVarOverride mode, string profileValue, string globalValue)
            {
                var name = profile.ResolveEnvVar(mode, profileValue, globalValue);
                return string.IsNullOrEmpty(name) ? "skip" : "$" + name;
            }
            return "keystore path / keystore password / key alias / key password from " +
                   Var(profile.KeystorePathMode, profile.KeystorePathEnvVar, global.KeystorePathEnvVar) + ", " +
                   Var(profile.KeystorePassMode, profile.KeystorePassEnvVar, global.KeystorePassEnvVar) + ", " +
                   Var(profile.KeyAliasMode, profile.KeyAliasEnvVar, global.KeyAliasEnvVar) + ", " +
                   Var(profile.KeyAliasPassMode, profile.KeyAliasPassEnvVar, global.KeyAliasPassEnvVar);
        }

        public void OnDrawProfileGUI(ForgeProfile forgeProfile)
        {
            var global = GetGlobalConfig();
            var profile = GetProfileConfig(forgeProfile);
            bool changed = false;

            changed |= DrawEnvVarOverrideField("Keystore Path", profile.KeystorePathMode, profile.KeystorePathEnvVar, global.KeystorePathEnvVar,
                (m, v) => { profile.KeystorePathMode = m; profile.KeystorePathEnvVar = v; });
            changed |= DrawEnvVarOverrideField("Keystore Password", profile.KeystorePassMode, profile.KeystorePassEnvVar, global.KeystorePassEnvVar,
                (m, v) => { profile.KeystorePassMode = m; profile.KeystorePassEnvVar = v; });
            changed |= DrawEnvVarOverrideField("Key Alias", profile.KeyAliasMode, profile.KeyAliasEnvVar, global.KeyAliasEnvVar,
                (m, v) => { profile.KeyAliasMode = m; profile.KeyAliasEnvVar = v; });
            changed |= DrawEnvVarOverrideField("Key Password", profile.KeyAliasPassMode, profile.KeyAliasPassEnvVar, global.KeyAliasPassEnvVar,
                (m, v) => { profile.KeyAliasPassMode = m; profile.KeyAliasPassEnvVar = v; });

            if (profile.Enabled)
            {
                EditorGUILayout.Space(2);
                var resolvedVars = new[]
                {
                    profile.ResolveEnvVar(profile.KeystorePathMode, profile.KeystorePathEnvVar, global.KeystorePathEnvVar),
                    profile.ResolveEnvVar(profile.KeystorePassMode, profile.KeystorePassEnvVar, global.KeystorePassEnvVar),
                    profile.ResolveEnvVar(profile.KeyAliasMode, profile.KeyAliasEnvVar, global.KeyAliasEnvVar),
                    profile.ResolveEnvVar(profile.KeyAliasPassMode, profile.KeyAliasPassEnvVar, global.KeyAliasPassEnvVar),
                };
                int setCount = 0;
                foreach (var v in resolvedVars)
                {
                    if (!string.IsNullOrEmpty(v) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v)))
                        setCount++;
                }

                var status = setCount == 0
                    ? "No env vars detected — existing settings will be used"
                    : $"{setCount}/4 env var(s) detected — will override at build time";
                EditorGUILayout.LabelField(" ", status, EditorStyles.miniLabel);
            }

            if (changed)
                SaveProfileConfig(forgeProfile, profile);
        }

        public void OnDrawSettingsGUI()
        {
            var global = GetGlobalConfig();
            bool changed = false;

            EditorGUILayout.LabelField("Default env var names (profiles inherit these unless overridden):", EditorStyles.miniLabel);
            changed |= DrawGlobalField("Keystore Path", global.KeystorePathEnvVar,
                v => { global.KeystorePathEnvVar = v; });
            changed |= DrawGlobalField("Keystore Password", global.KeystorePassEnvVar,
                v => { global.KeystorePassEnvVar = v; });
            changed |= DrawGlobalField("Key Alias", global.KeyAliasEnvVar,
                v => { global.KeyAliasEnvVar = v; });
            changed |= DrawGlobalField("Key Password", global.KeyAliasPassEnvVar,
                v => { global.KeyAliasPassEnvVar = v; });

            if (changed)
                SaveGlobalConfig(global);
        }

        static bool DrawEnvVarOverrideField(string label, EnvVarOverride mode, string profileValue,
            string globalValue, Action<EnvVarOverride, string> setter)
        {
            EditorGUILayout.BeginHorizontal();

            if (mode == EnvVarOverride.Inherit)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField(label, globalValue);
                if (GUILayout.Button("Override", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    setter(EnvVarOverride.Custom, globalValue);
                    EditorGUILayout.EndHorizontal();
                    return true;
                }
            }
            else
            {
                var newValue = EditorGUILayout.TextField(label, profileValue);
                bool changed = false;
                if (newValue != profileValue)
                {
                    setter(EnvVarOverride.Custom, newValue);
                    changed = true;
                }
                if (GUILayout.Button("Inherit", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    setter(EnvVarOverride.Inherit, null);
                    changed = true;
                }
                EditorGUILayout.EndHorizontal();
                return changed;
            }

            EditorGUILayout.EndHorizontal();
            return false;
        }

        /// <summary>
        /// Reads the env var. Returns null when the var name is empty or the
        /// variable is unset; an empty-string value is returned as-is so the
        /// caller can flag it (a CI secret that failed to populate).
        /// </summary>
        static string GetEnvValue(string envVarName)
        {
            if (string.IsNullOrEmpty(envVarName)) return null;
            return Environment.GetEnvironmentVariable(envVarName);
        }

        internal static string EmptyEnvVarError(string envVarName, string value)
        {
            if (value != null && value.Length == 0)
                return $"[Build Forge/AndroidKeystoreEnv] Environment variable {envVarName} is set but " +
                    "empty — this usually means a CI secret failed to populate. Give it a value or unset it.";
            return null;
        }

        /// <summary>
        /// Validates the signing state the build would end up with after env
        /// overrides are applied. Deliberately does NOT require passwords or a
        /// key alias — those can legitimately come from Player Settings (or be
        /// empty for exotic passwordless keystores) and gradle fails loudly on
        /// a bad value. Only unambiguous misconfigurations are rejected.
        /// </summary>
        internal static string SigningStateError(bool settingsUseCustomKeystore, string settingsKeystorePath,
            string envKeystorePath, bool otherEnvValuesPresent, string keystorePathVarName)
        {
            var useCustomKeystore = envKeystorePath != null || settingsUseCustomKeystore;

            if (useCustomKeystore && string.IsNullOrEmpty(envKeystorePath ?? settingsKeystorePath))
                return "[Build Forge/AndroidKeystoreEnv] A custom keystore is in use but the keystore path " +
                    $"is empty — the build would fail at the gradle signing step. Set ${keystorePathVarName} " +
                    "or the Keystore path in Player Settings.";

            if (!useCustomKeystore && otherEnvValuesPresent)
                return "[Build Forge/AndroidKeystoreEnv] Signing environment variables are set, but the " +
                    $"build would not use a custom keystore (${keystorePathVarName} is not set and Custom " +
                    "Keystore is off in Player Settings) — the output would be debug-signed. Set " +
                    $"${keystorePathVarName}, enable Custom Keystore, or disable this plugin for this profile.";

            return null;
        }

        static void ThrowIfError(string error)
        {
            if (error != null)
                throw new InvalidOperationException(error);
        }

        /// <summary>
        /// The keystore path exactly as ProjectSettings.asset stores it. The
        /// PlayerSettings.Android.keystoreName setter rewrites an empty or null
        /// path to "{inproject}: ", so restoring through it would leave that in
        /// ProjectSettings after every build.
        /// </summary>
        internal static string SerializedKeystoreName
        {
            get => PlayerSettingsProperty().stringValue;
            set
            {
                var property = PlayerSettingsProperty();
                property.stringValue = value;
                property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static SerializedProperty PlayerSettingsProperty()
        {
            // The object the static PlayerSettings API writes: the active Build
            // Profile's PlayerSettings, which falls back to the project's.
            var target = BuildProfile.GetActiveBuildProfile()?.GetComponent<PlayerSettings>()
                ?? Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            var playerSettings = new SerializedObject(target);
            return playerSettings.FindProperty("AndroidKeystoreName")
                ?? throw new InvalidOperationException("[Build Forge/AndroidKeystoreEnv] PlayerSettings has no AndroidKeystoreName property.");
        }

        static int Apply(string value, string envVarName, Action<string> apply)
        {
            if (value == null) return 0;
            apply(value);
            Debug.Log($"[Build Forge/AndroidKeystoreEnv] Set from ${envVarName}.");
            return 1;
        }

        static bool DrawGlobalField(string label, string value, Action<string> setter)
        {
            var newValue = EditorGUILayout.TextField(label, value);
            if (newValue != value)
            {
                setter(newValue);
                return true;
            }
            return false;
        }

        static AndroidKeystoreEnvGlobalConfig GetGlobalConfig()
        {
            return ForgeSettings.instance.GetGlobalPluginConfig<AndroidKeystoreEnvGlobalConfig>(GlobalConfigKey);
        }

        static void SaveGlobalConfig(AndroidKeystoreEnvGlobalConfig config)
        {
            ForgeSettings.instance.SetGlobalPluginConfig(GlobalConfigKey, config);
        }

        static AndroidKeystoreEnvProfileConfig GetProfileConfig(ForgeProfile profile)
        {
            return profile.GetPluginConfig<AndroidKeystoreEnvProfileConfig>(ConfigKey);
        }

        static void SaveProfileConfig(ForgeProfile profile, AndroidKeystoreEnvProfileConfig config)
        {
            profile.SetPluginConfig(ConfigKey, config);
        }
    }
}
