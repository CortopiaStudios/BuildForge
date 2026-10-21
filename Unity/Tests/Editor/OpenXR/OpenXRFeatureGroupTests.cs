using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.OpenXR;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace BuildForge.Tests.OpenXR
{
    /// <summary>
    /// A feature group as a vendor package declares one: for Standalone, requiring the Khronos simple
    /// controller profile. OpenXR and Build Forge find it through its attribute, as they find a
    /// vendor's; it is ticked only while a test ticks it.
    /// </summary>
    [OpenXRFeatureSet(
        FeatureSetId = TestFeatureGroup.Id,
        UiName = TestFeatureGroup.Name,
        Description = "A feature group Build Forge's tests tick.",
        SupportedBuildTargets = new[] { BuildTargetGroup.Standalone },
        FeatureIds = new[] { KHRSimpleControllerProfile.featureId },
        RequiredFeatureIds = new[] { KHRSimpleControllerProfile.featureId })]
    sealed class TestFeatureGroup
    {
        public const string Id = "com.cortopiastudios.buildforge.tests.featuregroup";
        public const string Name = "Build Forge Test Group";
    }

    /// <summary>
    /// The feature group checks of builds, Apply and the Play Mode copy, with the group ticked where
    /// Build Forge reads it, in OpenXR's editor settings, while OpenXR's own state stays as it was,
    /// so that OpenXR's setter refuses nothing for it.
    /// </summary>
    public class OpenXRFeatureGroupTests
    {
        Object editorSettings;
        string editorSettingsBefore;
        bool ticked;

        [SetUp] public void SetUp()
        {
            // Through OpenXR's own accessor, which creates the asset where the project has none yet,
            // as OpenXR does when it first reads a group.
            var type = typeof(OpenXRFeatureSetManager).Assembly.GetType("UnityEditor.XR.OpenXR.OpenXREditorSettings");
            Assert.IsNotNull(type, "OpenXR keeps the ticked feature groups elsewhere; update Build Forge's reader.");
            editorSettings = (Object)type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            Assert.IsNotNull(editorSettings, "OpenXR's editor settings can't be had.");
            editorSettingsBefore = EditorJsonUtility.ToJson(editorSettings);
            ticked = false;
        }

        [TearDown] public void TearDown()
        {
            if (ticked)
                RestoreEditorSettings();
        }

        void Tick()
        {
            OpenXROverrideTests.SetFeatureGroupTicked(editorSettings, BuildTargetGroup.Standalone, TestFeatureGroup.Id, true);
            ticked = true;
        }

        // Saved too, so that a test whose writes save assets leaves nothing ticked on disk.
        void RestoreEditorSettings()
        {
            EditorJsonUtility.FromJsonOverwrite(editorSettingsBefore, editorSettings);
            EditorUtility.SetDirty(editorSettings);
            AssetDatabase.SaveAssetIfDirty(editorSettings);
        }

        // A Disable override of a feature the ticked group requires stops a build and an Apply before
        // they write anything, although OpenXR itself would let it through.
        [Test] public void ATickedGroupsRequiredFeature_StopsTheBuildAndApply_BeforeTheyWrite()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var original = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                // The group requires an interaction profile, which Build Forge manages with this option on.
                using (new OpenXROverrideTests.GlobalOptions(showInteractionProfiles: true, matchInPlayMode: false))
                {
                    OpenXROverrideTests.SetEnabledAsData(standalone.GetFeature<KHRSimpleControllerProfile>(), true);
                    Tick();
                    var config = new OpenXRProfileConfig();
                    config.DisabledFeatures.Add(typeof(KHRSimpleControllerProfile).FullName);
                    forge.SetPluginConfig("OpenXR", config);
                    var plugin = new OpenXRPlugin();
                    var before = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true).ToJson();

                    var build = Assert.Throws<System.InvalidOperationException>(() => plugin.OnPreBuild(
                        new ForgeBuildContext(null, forge, BuildTarget.StandaloneWindows64, "unused", true)));
                    var apply = Assert.Throws<System.InvalidOperationException>(
                        () => plugin.ApplyToEditor(forge, BuildTargetGroup.Standalone, null));

                    foreach (var message in new[] { build.Message, apply.Message })
                    {
                        StringAssert.Contains("with a Disable override", message);
                        StringAssert.Contains($"'{TestFeatureGroup.Name}', ticked for Standalone", message);
                    }
                    Assert.AreEqual(before, OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true).ToJson(),
                        "Nothing is written.");
                }
            }
            finally
            {
                Object.DestroyImmediate(forge);
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, original);
            }
        }

        // Play Mode Follows Applied Profile, matching the platform's features, would turn off the feature
        // the group ticked for Standalone requires: the Apply stops before the copy writes anything.
        [Test] public void ATickedGroupsRequiredFeature_StopsTheMatchingPlayModeCopy_BeforeItWrites()
        {
            Assume.That(BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android),
                "This editor has no Android Build Support.");
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            var android = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            Assert.That(android, Is.Not.Null, "The OpenXR test host must contain Android settings.");
            var standaloneOriginal = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            var androidOriginal = OpenXRSettingsState.Capture(BuildTargetGroup.Android, true);
            var native = OpenXROverrideTests.CreateNativeProfile(BuildTarget.Android);
            var forge = OpenXROverrideTests.ForgeProfileFor(native);
            try
            {
                using (new OpenXROverrideTests.GlobalOptions(showInteractionProfiles: false, matchInPlayMode: true))
                {
                    OpenXROverrideTests.SetEnabledAsData(standalone.GetFeature<KHRSimpleControllerProfile>(), true);
                    OpenXROverrideTests.SetEnabledAsData(android.GetFeature<KHRSimpleControllerProfile>(), false);
                    Tick();
                    var before = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true).ToJson();

                    var stopped = Assert.Throws<System.InvalidOperationException>(
                        () => new PlayModeOpenXRFeatures().ApplyToEditor(forge, BuildTargetGroup.Standalone, null));

                    StringAssert.Contains("would turn off OpenXR feature", stopped.Message);
                    StringAssert.Contains($"'{TestFeatureGroup.Name}', ticked for Standalone", stopped.Message);
                    Assert.AreEqual(before, OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true).ToJson(),
                        "Nothing is copied.");
                }
            }
            finally
            {
                Object.DestroyImmediate(forge);
                Object.DestroyImmediate(native);
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, standaloneOriginal);
                OpenXRSettingsState.Restore(BuildTargetGroup.Android, androidOriginal);
            }
        }

        // OpenXR initializes its feature groups on the editor's first update, which a batch build can
        // start before. Reading the groups, for the warnings a build logs before it starts or for its
        // checks, must not initialize them instead: that refreshes and saves the OpenXR settings and
        // turns on what ticked groups require, before the build's marker and snapshot.
        [Test] public void ReadingTheGroups_LeavesOpenXRsGroupsUninitialized_AndChangesNoAsset()
        {
            var cache = typeof(OpenXRFeatureSetManager).GetField("s_AllFeatureSets", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(cache, "OpenXR's feature group cache is an OpenXR internal; update the test.");
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            Tick();
            AssetDatabase.SaveAssetIfDirty(editorSettings);
            var files = new[] { AssetDatabase.GetAssetPath(standalone), AssetDatabase.GetAssetPath(editorSettings) };
            var contents = files.Select(f => File.ReadAllText(f)).ToArray();
            var assets = files.SelectMany(f => AssetDatabase.LoadAllAssetsAtPath(f)).Where(a => a != null).ToList();
            var dirty = assets.Where(a => EditorUtility.IsDirty(a)).ToList();
            var native = OpenXROverrideTests.CreateNativeProfile(BuildTarget.StandaloneWindows64);
            var forge = OpenXROverrideTests.ForgeProfileFor(native);
            var initialized = cache.GetValue(null);
            try
            {
                // As in a batch build before the editor's first update.
                cache.SetValue(null, null);

                CollectionAssert.Contains(OpenXRPlugin.TickedFeatureGroups(BuildTargetGroup.Standalone).Select(s => s.featureSetId).ToList(),
                    TestFeatureGroup.Id);
                OpenXRPlugin.TickedFeatureGroups(BuildTargetGroup.Android);
                var warnings = ForgeBuildRunner.BuildWarnings(forge, null, new IForgePlugin[] { new OpenXRPlugin() });

                Assert.IsTrue(warnings.Any(w => w.Contains($"'{TestFeatureGroup.Name}' are enabled for Standalone")),
                    string.Join("\n", warnings));
                Assert.IsNull(cache.GetValue(null), "Reading the groups initialized OpenXR's.");
            }
            finally
            {
                cache.SetValue(null, initialized);
                Object.DestroyImmediate(forge);
                Object.DestroyImmediate(native);
            }
            CollectionAssert.AreEquivalent(dirty, assets.Where(a => EditorUtility.IsDirty(a)).ToList(), "Reading the groups changed an asset.");
            CollectionAssert.AreEqual(contents, files.Select(f => File.ReadAllText(f)).ToArray(), "Reading the groups saved an asset.");
        }

        // Build Forge reads the groups as OpenXR does: once OpenXR has read them itself, the two agree
        // on each group, whether it is ticked, its features and the ones it requires.
        [Test] public void TheGroupsBuildForgeReads_AreTheOnesOpenXRReads()
        {
            var original = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            Tick();
            try
            {
                // Reads the ticked groups from the editor settings, and turns on what they require.
                OpenXRFeatureSetManager.InitializeFeatureSets();
                foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
                {
                    var tickedIds = OpenXRPlugin.TickedFeatureGroupIds(OpenXRPlugin.FindEditorSettings(), group);
                    Assert.AreEqual(Describe(OpenXRFeatureSetManager.FeatureSetsForBuildTarget(group)),
                        Describe(OpenXRPlugin.DeclaredFeatureGroups(group, tickedIds)), $"The groups for {group}.");
                    Assert.AreEqual(Describe(OpenXRFeatureSetManager.FeatureSetsForBuildTarget(group).Where(s => s.isEnabled)),
                        Describe(OpenXRPlugin.TickedFeatureGroups(group)), $"The groups ticked for {group}.");
                }
                StringAssert.Contains(TestFeatureGroup.Id, Describe(OpenXRPlugin.TickedFeatureGroups(BuildTargetGroup.Standalone)));
            }
            finally
            {
                RestoreEditorSettings();
                // OpenXR reads them again, so that its setter refuses nothing for the group in later tests.
                OpenXRFeatureSetManager.InitializeFeatureSets();
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, original);
            }
        }

        static string Describe(IEnumerable<OpenXRFeatureSetManager.FeatureSet> sets)
            => string.Join("\n", sets
                .Where(s => s.featureIds != null)
                .Select(s => $"{s.featureSetId} '{s.name}' {(s.isEnabled ? "ticked" : "unticked")}: {string.Join(",", s.featureIds)}; " +
                             $"required {string.Join(",", s.requiredFeatureIds ?? new string[0])}")
                .OrderBy(d => d, System.StringComparer.Ordinal));
    }
}
