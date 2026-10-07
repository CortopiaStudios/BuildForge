using System;
using System.Collections.Generic;
using System.IO;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Plugins;
using NUnit.Framework;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// Plugin configs are stored as managed references ([SerializeReference])
    /// so they serialize as plain YAML fields. These tests lock the get/set
    /// contract on plain lists and on a ForgeProfile, and prove the round trip
    /// through Unity's text serializer for both a profile asset and the
    /// ForgeSettings singleton file.
    /// </summary>
    public class PluginConfigStorageTests
    {
        const string TempFolder = "Assets/BuildForgeTestTmp";
        const string TempAssetPath = TempFolder + "/Profile.asset";

        ForgeProfile profile;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<ForgeProfile>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void Get_Missing_ReturnsFreshDefault_AndStoresNothing()
        {
            var slots = new List<PluginConfigSlot>();

            var first = PluginConfigSlot.Get<BuildNumberConfig>(slots, "BuildNumber");
            first.Offset = 42;
            var second = PluginConfigSlot.Get<BuildNumberConfig>(slots, "BuildNumber");

            Assert.AreNotSame(first, second);
            Assert.AreEqual(0, second.Offset, "A default is never stored.");
            Assert.IsEmpty(slots);
        }

        [Test]
        public void Get_DoesNotDirty_Set_Dirties()
        {
            EditorUtility.ClearDirty(profile);
            profile.GetPluginConfig<BuildNumberConfig>("BuildNumber");
            Assert.IsFalse(EditorUtility.IsDirty(profile));

            profile.SetPluginConfig("BuildNumber", new BuildNumberConfig());
            Assert.IsTrue(EditorUtility.IsDirty(profile));
        }

        [Test]
        public void Set_ThenGet_ReturnsSameInstance()
        {
            var config = new BuildNumberConfig { Offset = 7 };
            profile.SetPluginConfig("BuildNumber", config);

            Assert.AreSame(config, profile.GetPluginConfig<BuildNumberConfig>("BuildNumber"));
        }

        [Test]
        public void Set_ReplacesExisting_KeepsOtherKeys()
        {
            var slots = new List<PluginConfigSlot>();
            PluginConfigSlot.Set(slots, "BuildNumber", new BuildNumberConfig { Offset = 1 });
            PluginConfigSlot.Set(slots, "Git", new GitMetadataConfig());
            PluginConfigSlot.Set(slots, "BuildNumber", new BuildNumberConfig { Offset = 2 });

            Assert.AreEqual(2, slots.Count);
            Assert.AreEqual(2, PluginConfigSlot.Get<BuildNumberConfig>(slots, "BuildNumber").Offset);
        }

        [Test]
        public void Get_WrongType_ReturnsDefault_LeavesStoredInstance()
        {
            var stored = new BuildNumberConfig { Offset = 5 };
            profile.SetPluginConfig("BuildNumber", stored);

            var wrong = profile.GetPluginConfig<GitMetadataConfig>("BuildNumber");
            Assert.IsNotNull(wrong);
            Assert.AreSame(stored, profile.GetPluginConfig<BuildNumberConfig>("BuildNumber"));
        }

        [Test]
        public void Set_Null_RemovesSlot()
        {
            var slots = new List<PluginConfigSlot>();
            PluginConfigSlot.Set(slots, "BuildNumber", new BuildNumberConfig());
            PluginConfigSlot.Set(slots, "BuildNumber", null);

            Assert.IsEmpty(slots);
            Assert.DoesNotThrow(() => PluginConfigSlot.Set(slots, "BuildNumber", null));
        }

        class NotSerializable { }

        [Test]
        public void Set_NonSerializableClass_Throws()
        {
            var slots = new List<PluginConfigSlot>();
            Assert.Throws<ArgumentException>(() => PluginConfigSlot.Set(slots, "X", new NotSerializable()));
        }

        [Test]
        public void Set_UnityObject_Throws()
        {
            var slots = new List<PluginConfigSlot>();
            var so = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                Assert.Throws<ArgumentException>(() => PluginConfigSlot.Set(slots, "X", so));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        [Test]
        public void Asset_YamlRoundTrip_IsNativeAndDiffable()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "BuildForgeTestTmp");

            try
            {
                var asset = ScriptableObject.CreateInstance<ForgeProfile>();
                asset.SetPluginConfig("BuildNumber", new BuildNumberConfig { Enabled = true, Offset = 42 });
                AssetDatabase.CreateAsset(asset, TempAssetPath);
                AssetDatabase.SaveAssets();

                var text = File.ReadAllText(TempAssetPath);
                StringAssert.Contains("key: BuildNumber", text);
                StringAssert.Contains("offset: 42", text);
                StringAssert.Contains("class: BuildNumberConfig", text);
                StringAssert.IsMatch(@"envVarName:[ 	]*?
", text,
                    "An untouched fallback string stores empty, not its default.");
                StringAssert.DoesNotContain("jsonData", text);
                StringAssert.DoesNotContain("{\"", text, "No JSON blobs anywhere in the profile asset.");

                // Reload from disk, bypassing the in-memory asset.
                var loaded = InternalEditorUtility.LoadSerializedFileAndForget(TempAssetPath);
                Assert.IsNotEmpty(loaded);
                var reloaded = (ForgeProfile)loaded[0];
                try
                {
                    var config = reloaded.GetPluginConfig<BuildNumberConfig>("BuildNumber");
                    Assert.IsTrue(config.Enabled);
                    Assert.AreEqual(42, config.Offset);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(reloaded);
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(TempAssetPath);
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        /// <summary>
        /// Proves ScriptableSingleton.Save writes managed references as plain YAML.
        /// Touches the host project's ProjectSettings/ForgeSettings.asset under a
        /// test-only key that is removed again. Reading the file back into a second
        /// ForgeSettings is not possible in-process (the singleton constructor
        /// logs an error); the asset test above covers the load path with the
        /// same serializer.
        /// </summary>
        [Test]
        public void ForgeSettings_GlobalConfig_IsWrittenAsNativeYaml()
        {
            const string key = "BuildForge.Tests.Tmp";
            const string path = "ProjectSettings/ForgeSettings.asset";

            try
            {
                ForgeSettings.instance.SetGlobalPluginConfig(key,
                    new AndroidKeystoreEnvGlobalConfig { KeystorePathEnvVar = "X_TEST" });

                var text = File.ReadAllText(path);
                StringAssert.Contains("key: " + key, text);
                StringAssert.Contains("keystorePathEnvVar: X_TEST", text);
                StringAssert.IsMatch(@"keystorePassEnvVar:[ 	]*?
", text,
                    "An untouched fallback string stores empty, not its default.");
                StringAssert.Contains("class: AndroidKeystoreEnvGlobalConfig", text);
                StringAssert.DoesNotContain("jsonData", text);
            }
            finally
            {
                ForgeSettings.instance.SetGlobalPluginConfig<AndroidKeystoreEnvGlobalConfig>(key, null);
                StringAssert.DoesNotContain(key, File.ReadAllText(path), "Cleanup must remove the test key.");
            }
        }
    }
}
