using System;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.Plugins;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    // PlayerSettings.Android.keystoreName rewrites an empty path to
    // "{inproject}: " in ProjectSettings.asset, so a build must leave the
    // stored value exactly as it found it.
    public class AndroidKeystoreEnvPluginTests
    {
        string envName;
        ForgeProfile profile;
        bool originalUseCustomKeystore;
        string originalKeystoreName;

        [SetUp]
        public void SetUp()
        {
            envName = "BUILDFORGE_TEST_KEYSTORE_" + Guid.NewGuid().ToString("N");
            originalUseCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            originalKeystoreName = AndroidKeystoreEnvPlugin.SerializedKeystoreName;

            profile = ScriptableObject.CreateInstance<ForgeProfile>();
            // Only the keystore path comes from the environment; the other
            // names resolve empty, so the machine's own variables are ignored.
            profile.SetPluginConfig("AndroidKeystoreEnv", new AndroidKeystoreEnvProfileConfig
            {
                Enabled = true,
                KeystorePathMode = EnvVarOverride.Custom, KeystorePathEnvVar = envName,
                KeystorePassMode = EnvVarOverride.Custom, KeystorePassEnvVar = "",
                KeyAliasMode = EnvVarOverride.Custom, KeyAliasEnvVar = "",
                KeyAliasPassMode = EnvVarOverride.Custom, KeyAliasPassEnvVar = "",
            });
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(envName, null);
            PlayerSettings.Android.useCustomKeystore = originalUseCustomKeystore;
            AndroidKeystoreEnvPlugin.SerializedKeystoreName = originalKeystoreName;
            UnityEngine.Object.DestroyImmediate(profile);
        }

        ForgeBuildContext Context() =>
            new ForgeBuildContext(null, profile, BuildTarget.Android, "unused", true);

        [Test]
        public void Build_WithoutVariables_LeavesEmptyKeystoreNameEmpty()
        {
            PlayerSettings.Android.useCustomKeystore = false;
            AndroidKeystoreEnvPlugin.SerializedKeystoreName = "";
            var plugin = new AndroidKeystoreEnvPlugin();

            plugin.OnPreBuild(Context());
            plugin.OnPostBuild(Context());

            Assert.AreEqual("", AndroidKeystoreEnvPlugin.SerializedKeystoreName);
            Assert.IsFalse(PlayerSettings.Android.useCustomKeystore);
        }

        [Test]
        public void Build_WithKeystorePathVariable_RestoresEmptyKeystoreName()
        {
            PlayerSettings.Android.useCustomKeystore = false;
            AndroidKeystoreEnvPlugin.SerializedKeystoreName = "";
            Environment.SetEnvironmentVariable(envName, "C:/ci/release.keystore");
            var plugin = new AndroidKeystoreEnvPlugin();

            plugin.OnPreBuild(Context());
            Assert.AreEqual("C:/ci/release.keystore", PlayerSettings.Android.keystoreName);
            Assert.IsTrue(PlayerSettings.Android.useCustomKeystore);
            plugin.OnPostBuild(Context());

            Assert.AreEqual("", AndroidKeystoreEnvPlugin.SerializedKeystoreName);
            Assert.IsFalse(PlayerSettings.Android.useCustomKeystore);
        }

        [Test]
        public void Build_WithKeystorePathVariable_RestoresProjectRelativeKeystoreName()
        {
            PlayerSettings.Android.useCustomKeystore = true;
            AndroidKeystoreEnvPlugin.SerializedKeystoreName = "{inproject}: Keys/user.keystore";
            Environment.SetEnvironmentVariable(envName, "C:/ci/release.keystore");
            var plugin = new AndroidKeystoreEnvPlugin();

            plugin.OnPreBuild(Context());
            plugin.OnPostBuild(Context());

            Assert.AreEqual("{inproject}: Keys/user.keystore", AndroidKeystoreEnvPlugin.SerializedKeystoreName);
            Assert.IsTrue(PlayerSettings.Android.useCustomKeystore);
        }
    }
}
