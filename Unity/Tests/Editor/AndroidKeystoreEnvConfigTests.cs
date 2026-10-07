using BuildForge.Editor.Plugins;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class AndroidKeystoreEnvConfigTests
    {
        [Test]
        public void ResolveEnvVar_Inherit_ReturnsGlobalValue()
        {
            var config = new AndroidKeystoreEnvProfileConfig();
            var result = config.ResolveEnvVar(EnvVarOverride.Inherit, "PROFILE_VAR", "GLOBAL_VAR");
            Assert.AreEqual("GLOBAL_VAR", result);
        }

        [Test]
        public void ResolveEnvVar_Custom_ReturnsProfileValue()
        {
            var config = new AndroidKeystoreEnvProfileConfig();
            var result = config.ResolveEnvVar(EnvVarOverride.Custom, "PROFILE_VAR", "GLOBAL_VAR");
            Assert.AreEqual("PROFILE_VAR", result);
        }

        [Test]
        public void ResolveEnvVar_Custom_EmptyString_ReturnsEmpty()
        {
            var config = new AndroidKeystoreEnvProfileConfig();
            var result = config.ResolveEnvVar(EnvVarOverride.Custom, "", "GLOBAL_VAR");
            Assert.AreEqual("", result);
        }

        [Test]
        public void ResolveEnvVar_Inherit_NullGlobal_ReturnsNull()
        {
            var config = new AndroidKeystoreEnvProfileConfig();
            var result = config.ResolveEnvVar(EnvVarOverride.Inherit, "PROFILE_VAR", null);
            Assert.IsNull(result);
        }

        [Test]
        public void ProfileConfig_DefaultsToDisabled()
        {
            // Env signing modifies PlayerSettings, so it must be a per-profile
            // opt-in (see ARCHITECTURE.md "Plugin Default States").
            var config = new AndroidKeystoreEnvProfileConfig();
            Assert.IsFalse(config.Enabled);
        }

        [Test]
        public void EmptyEnvVarError_UnsetVariable_IsNotAnError()
        {
            Assert.IsNull(AndroidKeystoreEnvPlugin.EmptyEnvVarError("MY_VAR", null));
        }

        [Test]
        public void EmptyEnvVarError_ValuePresent_IsNotAnError()
        {
            Assert.IsNull(AndroidKeystoreEnvPlugin.EmptyEnvVarError("MY_VAR", "secret"));
        }

        [Test]
        public void EmptyEnvVarError_SetButEmpty_IsAnError()
        {
            // The classic failed-to-populate CI secret.
            var error = AndroidKeystoreEnvPlugin.EmptyEnvVarError("MY_VAR", "");
            Assert.IsNotNull(error);
            StringAssert.Contains("MY_VAR", error);
        }

        [Test]
        public void SigningStateError_PathFromEnv_IsValid()
        {
            Assert.IsNull(AndroidKeystoreEnvPlugin.SigningStateError(
                settingsUseCustomKeystore: false, settingsKeystorePath: "",
                envKeystorePath: "release.keystore", otherEnvValuesPresent: true,
                keystorePathVarName: "ANDROID_KEYSTORE_PATH"));
        }

        [Test]
        public void SigningStateError_PathFromSettingsPasswordsFromEnv_IsValid()
        {
            // The common CI setup: keystore path and alias committed in Player
            // Settings, only the secrets injected via env.
            Assert.IsNull(AndroidKeystoreEnvPlugin.SigningStateError(
                settingsUseCustomKeystore: true, settingsKeystorePath: "release.keystore",
                envKeystorePath: null, otherEnvValuesPresent: true,
                keystorePathVarName: "ANDROID_KEYSTORE_PATH"));
        }

        [Test]
        public void SigningStateError_NoEnvValuesNoCustomKeystore_IsValid()
        {
            // Enabled profile built locally without CI env vars: debug signing
            // via the existing settings is intentional, not an error.
            Assert.IsNull(AndroidKeystoreEnvPlugin.SigningStateError(
                settingsUseCustomKeystore: false, settingsKeystorePath: "",
                envKeystorePath: null, otherEnvValuesPresent: false,
                keystorePathVarName: "ANDROID_KEYSTORE_PATH"));
        }

        [Test]
        public void SigningStateError_CustomKeystoreWithoutPath_IsAnError()
        {
            var error = AndroidKeystoreEnvPlugin.SigningStateError(
                settingsUseCustomKeystore: true, settingsKeystorePath: "",
                envKeystorePath: null, otherEnvValuesPresent: false,
                keystorePathVarName: "ANDROID_KEYSTORE_PATH");
            Assert.IsNotNull(error);
            StringAssert.Contains("ANDROID_KEYSTORE_PATH", error);
        }

        [Test]
        public void SigningStateError_EnvValuesButNoCustomKeystore_IsAnError()
        {
            // Passwords injected but no keystore in play — the build would be
            // silently debug-signed.
            var error = AndroidKeystoreEnvPlugin.SigningStateError(
                settingsUseCustomKeystore: false, settingsKeystorePath: "",
                envKeystorePath: null, otherEnvValuesPresent: true,
                keystorePathVarName: "ANDROID_KEYSTORE_PATH");
            Assert.IsNotNull(error);
            StringAssert.Contains("debug-signed", error);
        }

        [Test]
        public void GlobalConfig_DefaultEnvVarNames()
        {
            var config = new AndroidKeystoreEnvGlobalConfig();
            Assert.AreEqual("ANDROID_KEYSTORE_PATH", config.KeystorePathEnvVar);
            Assert.AreEqual("ANDROID_KEYSTORE_PASSWORD", config.KeystorePassEnvVar);
            Assert.AreEqual("ANDROID_KEY_ALIAS", config.KeyAliasEnvVar);
            Assert.AreEqual("ANDROID_KEY_PASSWORD", config.KeyAliasPassEnvVar);
        }

        [Test]
        public void GlobalConfig_EmptyField_ReturnsFallbackDefault()
        {
            var config = new AndroidKeystoreEnvGlobalConfig();
            config.KeystorePathEnvVar = "";
            Assert.AreEqual("ANDROID_KEYSTORE_PATH", config.KeystorePathEnvVar);

            config.KeystorePassEnvVar = "";
            Assert.AreEqual("ANDROID_KEYSTORE_PASSWORD", config.KeystorePassEnvVar);

            config.KeyAliasEnvVar = "";
            Assert.AreEqual("ANDROID_KEY_ALIAS", config.KeyAliasEnvVar);

            config.KeyAliasPassEnvVar = "";
            Assert.AreEqual("ANDROID_KEY_PASSWORD", config.KeyAliasPassEnvVar);
        }
    }
}
