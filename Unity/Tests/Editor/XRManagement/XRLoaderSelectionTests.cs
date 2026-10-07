using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.XRManagement;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;

namespace BuildForge.Tests.XRManagement
{
    public class XRLoaderSelectionTests
    {
        const BuildTargetGroup Group = BuildTargetGroup.Standalone;
        // XR Management 4.7 added settingsKey (same value) and made k_SettingsKey obsolete.
        static readonly string SettingsKey =
#if BUILDFORGE_XR_MANAGEMENT_4_7
            XRGeneralSettings.settingsKey;
#else
            XRGeneralSettings.k_SettingsKey;
#endif
        string folder;
        XRGeneralSettingsPerBuildTarget original, settings;
        XRGeneralSettings general;
        XRManagerSettings manager;
        TestXRLoader first, second;
        BuildProfile native;
        ForgeProfile profile;
        XRLoaderSelectionPlugin plugin;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/ForgeXRTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            EditorBuildSettings.TryGetConfigObject(SettingsKey, out original);
            settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            general = ScriptableObject.CreateInstance<XRGeneralSettings>();
            manager = ScriptableObject.CreateInstance<XRManagerSettings>();
            AssetDatabase.CreateAsset(settings, folder + "/Settings.asset");
            AssetDatabase.AddObjectToAsset(general, settings);
            AssetDatabase.AddObjectToAsset(manager, settings);
            general.Manager = manager;
            settings.SetSettingsForBuildTarget(Group, general);
            EditorBuildSettings.AddConfigObject(SettingsKey, settings, true);
            first = ScriptableObject.CreateInstance<TestXRLoader>();
            second = ScriptableObject.CreateInstance<TestXRLoader>();
            AssetDatabase.CreateAsset(first, folder + "/First.asset");
            AssetDatabase.CreateAsset(second, folder + "/Second.asset");
            Assert.That(manager.TrySetLoaders(new List<XRLoader> { first, second }), Is.True);
            native = (BuildProfile)typeof(BuildProfile).GetMethod("CreateInstance", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(BuildTarget), typeof(StandaloneBuildSubtarget) }, null)
                .Invoke(null, new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
            profile = ScriptableObject.CreateInstance<ForgeProfile>();
            var so = new SerializedObject(profile);
            so.FindProperty("buildProfile").objectReferenceValue = native;
            so.ApplyModifiedPropertiesWithoutUndo();
            plugin = new XRLoaderSelectionPlugin();
        }
        [TearDown]
        public void TearDown()
        {
            if (original != null)
            {
                EditorBuildSettings.AddConfigObject(SettingsKey, original, true);
                original.SetSettingsForBuildTarget(Group, original.SettingsForBuildTarget(Group));
            }
            else EditorBuildSettings.RemoveConfigObject(SettingsKey);
            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(native);
            AssetDatabase.DeleteAsset(folder);
        }
        void Select(params XRLoader[] loaders)
            => profile.SetPluginConfig(XRLoaderSelectionPlugin.Key, new XRLoaderSelectionConfig { enabled = true, loaders = new List<XRLoader>(loaders) });

        [Test]
        public void ApplyEmptyAndRestore_PreservesOrderedBaseline()
        {
            var snapshot = plugin.CaptureEditorState(Group);
            Select();
            plugin.ApplyToEditor(profile, Group);
            Assert.That(manager.activeLoaders, Is.Empty);
            // A fresh plugin instance can restore the persisted JSON without in-memory state.
            new XRLoaderSelectionPlugin().RestoreEditorState(Group, snapshot);
            CollectionAssert.AreEqual(new XRLoader[] { first, second }, manager.activeLoaders);
            Select(second, first);
            plugin.ApplyToEditor(profile, Group);
            CollectionAssert.AreEqual(new XRLoader[] { second, first }, manager.activeLoaders);
        }
        [Test]
        public void BuildFailureRestoresOriginalLoaders()
        {
            Select(second);
            var context = new ForgeBuildContext(native, profile, BuildTarget.StandaloneWindows64, "unused", false);
            try
            {
                plugin.OnPreBuild(context);
                CollectionAssert.AreEqual(new XRLoader[] { second }, manager.activeLoaders);
                throw new InvalidOperationException("Injected downstream failure");
            }
            catch (InvalidOperationException) { }
            finally { plugin.OnPostBuild(context); }
            CollectionAssert.AreEqual(new XRLoader[] { first, second }, manager.activeLoaders);
        }
        [Test]
        public void InvalidSelectionIsRejectedBeforeMutation()
        {
            Select(first, first);
            Assert.That(plugin.Validate(profile), Is.Not.Empty);
            Assert.Throws<InvalidOperationException>(() => plugin.ApplyToEditor(profile, Group));
            CollectionAssert.AreEqual(new XRLoader[] { first, second }, manager.activeLoaders);
            Select((XRLoader)null);
            Assert.That(plugin.Validate(profile), Is.Not.Empty);
        }
        // Applied with XR Loaders on, then turned off for the profile: a build
        // suspends the applied profile (baseline back) and resumes it, and the
        // turned-off plugin must not write its stored selection again.
        [Test]
        public void TurnedOffSelection_IsNotReappliedAfterABuild()
        {
            Select(second);
            var entries = new List<EditorStateEntry>();
            var pair = new EditorStatePair { PluginTypeName = typeof(XRLoaderSelectionPlugin).FullName, Group = Group, Plugin = plugin };
            IForgeEditorApplicable Resolve(string name) => name == pair.PluginTypeName ? plugin : null;
            EditorStateApplier.Apply(entries, new[] { pair }, profile, Resolve, null);
            CollectionAssert.AreEqual(new XRLoader[] { second }, manager.activeLoaders);

            plugin.SetEnabled(profile, false);
            EditorStateApplier.SuspendForBuild(entries, Resolve);
            var result = EditorStateApplier.ResumeAfterBuild(entries, profile, Resolve);

            Assert.IsEmpty(result.Failures);
            CollectionAssert.AreEqual(new XRLoader[] { first, second }, manager.activeLoaders);
            Assert.AreEqual(entries.Single().baselineJson, entries.Single().expectedJson);
        }
        [Test]
        public void IntendedLoadersAreReportedBeforeApply()
        {
            Select(second);
            CollectionAssert.AreEqual(new XRLoader[] { second }, XRLoaderSelectionPlugin.EffectiveLoaders(profile, Group));
            CollectionAssert.AreEqual(new XRLoader[] { first, second }, manager.activeLoaders);
            plugin.SetEnabled(profile, false);
            CollectionAssert.AreEqual(manager.activeLoaders, XRLoaderSelectionPlugin.EffectiveLoaders(profile, Group));
        }
    }
}
