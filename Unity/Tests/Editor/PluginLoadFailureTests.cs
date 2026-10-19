using System;
using System.Collections.Generic;
using System.IO;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// A plugin whose creation failed doesn't run, so builds stop while it is on in
    /// Project Settings instead of going out without it. The same for plugin settings
    /// whose type is gone or renamed, which would read as defaults.
    /// </summary>
    public class PluginLoadFailureTests
    {
        [Serializable] public class FakeConfig { public int value; }
        [Serializable] public class NestedConfig { [SerializeReference] public object inner; public int value; }

        // A profile asset saved with settings of a class that no longer exists, as after
        // a package was removed or a Build Forge update renamed the class: its YAML names
        // the class, and Unity cannot load it when the asset is imported.
        static ForgeProfile ImportWithMissingSettingsType(string folder, Action<ForgeProfile> configure = null)
        {
            var source = ScriptableObject.CreateInstance<ForgeProfile>();
            if (configure != null)
                configure(source);
            else
                source.SetPluginConfig("Fake", new FakeConfig { value = 3 });
            var sourcePath = folder + "/Source.asset";
            AssetDatabase.CreateAsset(source, sourcePath);
            AssetDatabase.SaveAssetIfDirty(source);
            var yaml = File.ReadAllText(sourcePath);
            StringAssert.Contains("FakeConfig", yaml);
            var stalePath = folder + "/Stale.asset";
            File.WriteAllText(stalePath, yaml.Replace("FakeConfig", "RemovedConfig"));
            AssetDatabase.ImportAsset(stalePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var stale = AssetDatabase.LoadAssetAtPath<ForgeProfile>(stalePath);
            Assert.IsNotNull(stale);
            return stale;
        }

        static string NewTempFolder()
        {
            var name = "BuildForgeMissingTypeTest-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            return "Assets/" + name;
        }

        [Test]
        public void SettingsWhoseTypeIsMissing_AreListedWithTheirPlugin()
        {
            var folder = NewTempFolder();
            var current = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var stale = ImportWithMissingSettingsType(folder);
                current.SetPluginConfig("Fake", new FakeConfig { value = 3 });
                var missing = PluginConfigSlot.MissingTypes(stale, "pluginConfigs");
                StringAssert.StartsWith("'Fake' (", missing);
                StringAssert.Contains("PluginLoadFailureTests/RemovedConfig in BuildForge.Tests.Editor", missing);
                Assert.IsNull(PluginConfigSlot.MissingTypes(current, "pluginConfigs"));
                Assert.IsNull(ForgePluginRegistry.MissingSettingsError(current));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(current);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void SettingsReplacedOrRemovedInTheEditor_NoLongerCount()
        {
            var folder = NewTempFolder();
            try
            {
                // Unity goes on reporting the missing type for a while, but no setting holds it.
                var replaced = ImportWithMissingSettingsType(folder);
                replaced.SetPluginConfig("Fake", new FakeConfig { value = 5 });
                Assert.IsNull(PluginConfigSlot.MissingTypes(replaced, "pluginConfigs"));
                AssetDatabase.SaveAssetIfDirty(replaced);
                Assert.IsNull(PluginConfigSlot.MissingTypes(replaced, "pluginConfigs"));
                Assert.AreEqual(5, replaced.GetPluginConfig<FakeConfig>("Fake").value);

                AssetDatabase.DeleteAsset(folder + "/Source.asset");
                AssetDatabase.DeleteAsset(folder + "/Stale.asset");
                var removed = ImportWithMissingSettingsType(folder);
                removed.SetPluginConfig<FakeConfig>("Fake", null);
                Assert.IsNull(PluginConfigSlot.MissingTypes(removed, "pluginConfigs"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void ASettingsFieldWhoseTypeIsMissing_IsListedWithItsPlugin()
        {
            var folder = NewTempFolder();
            try
            {
                var stale = ImportWithMissingSettingsType(folder,
                    p => p.SetPluginConfig("Nested", new NestedConfig { inner = new FakeConfig { value = 3 }, value = 4 }));
                Assert.AreEqual(4, stale.GetPluginConfig<NestedConfig>("Nested").value, "The settings load, without the field.");
                Assert.IsNull(stale.GetPluginConfig<NestedConfig>("Nested").inner);
                var missing = PluginConfigSlot.MissingTypes(stale, "pluginConfigs");
                StringAssert.StartsWith("'Nested' (", missing);
                StringAssert.Contains("PluginLoadFailureTests/RemovedConfig in BuildForge.Tests.Editor", missing);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void CorrectingOrRemovingTheSettingsInTheFile_ClearsTheError()
        {
            var folder = NewTempFolder();
            try
            {
                var stale = ImportWithMissingSettingsType(folder);
                var path = AssetDatabase.GetAssetPath(stale);
                File.WriteAllText(path, File.ReadAllText(path).Replace("RemovedConfig", "FakeConfig"));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var corrected = AssetDatabase.LoadAssetAtPath<ForgeProfile>(path);
                Assert.IsNull(PluginConfigSlot.MissingTypes(corrected, "pluginConfigs"));
                Assert.AreEqual(3, corrected.GetPluginConfig<FakeConfig>("Fake").value);

                // The troubleshooting steps: the entry under pluginConfigs, here the only one,
                // and the one under references with its rid.
                AssetDatabase.DeleteAsset(folder + "/Source.asset");
                AssetDatabase.DeleteAsset(path);
                stale = ImportWithMissingSettingsType(folder);
                var lines = new List<string>(File.ReadAllLines(path));
                var slot = lines.FindIndex(l => l.Trim() == "- key: Fake");
                var rid = lines[slot + 2].Trim();
                StringAssert.StartsWith("rid: ", rid);
                lines.RemoveRange(slot, 3);
                var reference = lines.FindIndex(l => l.Trim() == "- " + rid);
                var next = lines.FindIndex(reference + 1, l => l.TrimStart().StartsWith("- rid: "));
                lines.RemoveRange(reference, (next < 0 ? lines.Count : next) - reference);
                File.WriteAllLines(path, lines);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var removed = AssetDatabase.LoadAssetAtPath<ForgeProfile>(path);
                Assert.IsFalse(UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(removed));
                Assert.IsNull(PluginConfigSlot.MissingTypes(removed, "pluginConfigs"));
                Assert.AreEqual(0, removed.GetPluginConfig<FakeConfig>("Fake").value);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void ABuild_StopsBeforeItStarts_WhileSettingsCannotBeLoaded()
        {
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var folder = NewTempFolder();
            // An asset, so the profile asset can reference it.
            var native = VariantRulesTests.CreateNativeProfile();
            AssetDatabase.CreateAsset(native, folder + "/Native.asset");
            try
            {
                var stale = ImportWithMissingSettingsType(folder);
                var fp = new SerializedObject(stale);
                fp.FindProperty("buildProfile").objectReferenceValue = native;
                fp.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreSame(native, stale.BuildProfile);
                StringAssert.Contains("RemovedConfig", PluginConfigSlot.MissingTypes(stale, "pluginConfigs"),
                    "Editing the profile in memory keeps the setting Unity cannot load.");
                var settings = new SerializedObject(ForgeSettings.instance);
                settings.FindProperty("buildVariants").arraySize = 0;
                settings.FindProperty("variantRules").arraySize = 0;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var before = EditorJsonUtility.ToJson(native);

                var message = Assert.Throws<InvalidOperationException>(() => ForgeBuildRunner.RunBuild(stale)).Message;
                StringAssert.Contains("cannot load the plugin settings 'Fake'", message);
                StringAssert.Contains("RemovedConfig", message);
                Assert.That(EditorJsonUtility.ToJson(native), Is.EqualTo(before));
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void APluginThatFailedToLoad_StopsBuilds_UnlessItIsOff()
        {
            var failures = new[] { (typeof(PluginLoadFailureTests), "Its constructor threw.") };
            var error = ForgePluginRegistry.LoadError(failures, _ => false);
            StringAssert.Contains(typeof(PluginLoadFailureTests).FullName, error);
            StringAssert.Contains("Its constructor threw.", error);
            StringAssert.Contains("Installed Plugins", error);
            Assert.IsNull(ForgePluginRegistry.LoadError(failures, _ => true), "Off in Project Settings: it wouldn't run anyway.");
            Assert.IsNull(ForgePluginRegistry.LoadError(Array.Empty<(Type, string)>(), _ => false));
        }

        [Test]
        public void ABuild_StopsBeforeItStarts_WhileAPluginFailedToLoad()
        {
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var native = VariantRulesTests.CreateNativeProfile();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            ForgePluginRegistry.GetPlugins();
            var failure = (typeof(PluginLoadFailureTests), "Its constructor threw.");
            ForgePluginRegistry.LoadFailures.Add(failure);
            try
            {
                var settings = new SerializedObject(ForgeSettings.instance);
                settings.FindProperty("buildVariants").arraySize = 0;
                settings.FindProperty("variantRules").arraySize = 0;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var fp = new SerializedObject(forge);
                fp.FindProperty("buildProfile").objectReferenceValue = native;
                fp.ApplyModifiedPropertiesWithoutUndo();
                var before = EditorJsonUtility.ToJson(native);

                var message = Assert.Throws<InvalidOperationException>(() => ForgeBuildRunner.RunBuild(forge)).Message;
                StringAssert.Contains("could not be loaded", message);
                StringAssert.Contains(typeof(PluginLoadFailureTests).FullName, message);
                Assert.That(EditorJsonUtility.ToJson(native), Is.EqualTo(before));
            }
            finally
            {
                ForgePluginRegistry.LoadFailures.Remove(failure);
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                UnityEngine.Object.DestroyImmediate(forge);
                UnityEngine.Object.DestroyImmediate(native);
            }
        }
    }
}
