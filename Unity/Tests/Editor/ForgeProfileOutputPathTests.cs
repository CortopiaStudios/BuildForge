using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEngine;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class ForgeProfileOutputPathTests
    {
        ForgeProfile profile;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<ForgeProfile>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(profile);
        }

        void SetOutputPath(string path)
        {
            var so = new SerializedObject(profile);
            so.FindProperty("outputPath").stringValue = path;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TestCase(null, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void RemovePreviousBuildOutput_LoadsFromProfileAsset(bool? storedValue, bool expected)
        {
            Assert.IsTrue(profile.RemovePreviousBuildOutput, "New profiles remove previous build output by default.");
            var folderName = "BuildForgeRemoveOutputTest-" + System.Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            var source = Object.Instantiate(profile);
            try
            {
                var serialized = new SerializedObject(source);
                serialized.FindProperty("removePreviousBuildOutput").boolValue = storedValue ?? true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var sourcePath = folder + "/Source.asset";
                AssetDatabase.CreateAsset(source, sourcePath);
                AssetDatabase.SaveAssetIfDirty(source);
                var yaml = File.ReadAllText(sourcePath);
                if (!storedValue.HasValue)
                {
                    // An old asset has never serialized this field. Import a new
                    // asset from that YAML so an in-memory value cannot hide a
                    // missing-field migration error.
                    var legacyYaml = Regex.Replace(yaml, @"(?m)^  removePreviousBuildOutput:.*\r?\n", "");
                    Assert.AreNotEqual(yaml, legacyYaml, "The legacy fixture must omit the setting.");
                    yaml = legacyYaml;
                }

                var loadedPath = folder + "/Loaded.asset";
                File.WriteAllText(loadedPath, yaml);
                AssetDatabase.ImportAsset(loadedPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var loaded = AssetDatabase.LoadAssetAtPath<ForgeProfile>(loadedPath);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(expected, loaded.RemovePreviousBuildOutput);
                Assert.IsTrue(profile.RemovePreviousBuildOutput, "The setting belongs to one profile, not all profiles.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                if (source != null)
                    Object.DestroyImmediate(source);
            }
        }

        // Before 1.0 the field was called pruneBuildDestination; a profile that
        // turned it off must stay off after the rename.
        [Test]
        public void RemovePreviousBuildOutput_ReadsTheFormerFieldName()
        {
            var folderName = "BuildForgeRemoveOutputTest-" + System.Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            var source = Object.Instantiate(profile);
            try
            {
                var serialized = new SerializedObject(source);
                serialized.FindProperty("removePreviousBuildOutput").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var sourcePath = folder + "/Source.asset";
                AssetDatabase.CreateAsset(source, sourcePath);
                AssetDatabase.SaveAssetIfDirty(source);
                var yaml = File.ReadAllText(sourcePath);
                var formerYaml = Regex.Replace(yaml, @"(?m)^  removePreviousBuildOutput:", "  pruneBuildDestination:");
                Assert.AreNotEqual(yaml, formerYaml, "The fixture must use the former field name.");

                var loadedPath = folder + "/Loaded.asset";
                File.WriteAllText(loadedPath, formerYaml);
                AssetDatabase.ImportAsset(loadedPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var loaded = AssetDatabase.LoadAssetAtPath<ForgeProfile>(loadedPath);
                Assert.IsNotNull(loaded);
                Assert.IsFalse(loaded.RemovePreviousBuildOutput);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                if (source != null)
                    Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Default_OutputPath_ContainsAllPlaceholders()
        {
            Assert.That(profile.OutputPath, Does.Contain("{Target}"));
            Assert.That(profile.OutputPath, Does.Contain("{ProfileName}"));
            Assert.That(profile.OutputPath, Does.Contain("{ProjectName}"));
            Assert.That(profile.OutputPath, Does.Contain("{Variant}"));
        }

        [Test]
        public void OutputPath_StoresEmpty_UntilCustomized()
        {
            Assert.IsFalse(profile.IsOutputPathCustomized, "A new profile follows the default.");
            Assert.AreEqual(ForgeProfile.DefaultOutputPath, profile.OutputPath);

            SetOutputPath("Custom/{ProfileName}");
            Assert.IsTrue(profile.IsOutputPathCustomized);
            Assert.AreEqual("Custom/{ProfileName}", profile.OutputPath);

            SetOutputPath("");
            Assert.IsFalse(profile.IsOutputPathCustomized);
            Assert.AreEqual(ForgeProfile.DefaultOutputPath, profile.OutputPath);
        }

        [Test]
        public void NormalizeOutputPathInput_DefaultOrEmpty_StoresNothing()
        {
            Assert.AreEqual("", ForgeProfile.NormalizeOutputPathInput(""));
            Assert.AreEqual("", ForgeProfile.NormalizeOutputPathInput("   "));
            Assert.AreEqual("", ForgeProfile.NormalizeOutputPathInput(null));
            Assert.AreEqual("", ForgeProfile.NormalizeOutputPathInput(ForgeProfile.DefaultOutputPath));
            Assert.AreEqual("", ForgeProfile.NormalizeOutputPathInput(" " + ForgeProfile.DefaultOutputPath + " "));
            Assert.AreEqual("Custom/{ProfileName}", ForgeProfile.NormalizeOutputPathInput("Custom/{ProfileName}"));
        }

        [Test]
        public void Variant_Placeholder_IsDefaultOrVariant_WhenVariantsAreConfigured()
        {
            profile.name = "Quest";
            SetOutputPath("Builds/{ProfileName}/{Variant}/Game");

            var defaultBuild = profile.GetResolvedOutputPath(BuildTarget.StandaloneWindows64, null, variantsConfigured: true);
            var internalBuild = profile.GetResolvedOutputPath(BuildTarget.StandaloneWindows64, "Internal", variantsConfigured: true);

            StringAssert.StartsWith("Builds/Quest/Default/Game", defaultBuild, "Default and variant builds are siblings.");
            StringAssert.StartsWith("Builds/Quest/Internal/Game", internalBuild);
        }

        [Test]
        public void MarkedProductName_ReachesTheFileName_ThroughProjectName()
        {
            profile.name = "Quest";
            SetOutputPath("Builds/{Variant}/{ProjectName}");
            var previous = ForgeSettings.instance.MangleProductName;
            try
            {
                ForgeSettings.instance.MangleProductName = true;
                var path = profile.GetResolvedOutputPath(BuildTarget.StandaloneWindows64, "Internal", true, "Game (Internal)");
                StringAssert.StartsWith("Builds/Internal/Game_Internal", path);
            }
            finally
            {
                ForgeSettings.instance.MangleProductName = previous;
            }
        }

        [Test]
        public void Variant_Placeholder_Collapses_WhenNoVariantsAreConfigured()
        {
            profile.name = "Quest";
            SetOutputPath("Builds/{ProfileName}/{Variant}/Game");

            var plain = profile.GetResolvedOutputPath(BuildTarget.StandaloneWindows64, null, variantsConfigured: false);

            StringAssert.StartsWith("Builds/Quest/Game", plain);
            StringAssert.DoesNotContain("//", plain);
        }

        [Test]
        public void CollapseEmptySegments_RemovesDoubleAndTrailingSeparators()
        {
            Assert.AreEqual("a/b/c", ForgeProfile.CollapseEmptySegments("a//b///c/"));
            Assert.AreEqual("a/b", ForgeProfile.CollapseEmptySegments("a/b"));
        }

        [Test]
        public void ProfileName_Placeholder_IsTheUnityBuildProfileName()
        {
            var factory = typeof(UnityEditor.Build.Profile.BuildProfile)
                .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .First(m => m.Name == "CreateInstance" && m.GetParameters().Length == 2);
            var buildProfile = (UnityEditor.Build.Profile.BuildProfile)factory.Invoke(null,
                new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
            buildProfile.name = "Quest";
            try
            {
                profile.name = "QuestForge";
                var so = new SerializedObject(profile);
                so.FindProperty("buildProfile").objectReferenceValue = buildProfile;
                so.ApplyModifiedPropertiesWithoutUndo();
                SetOutputPath("Builds/{ProfileName}");

                Assert.AreEqual("Quest", profile.DisplayName);
                StringAssert.Contains("Builds/Quest", profile.GetResolvedOutputPath(BuildTarget.StandaloneWindows64));
                StringAssert.DoesNotContain("QuestForge", profile.GetResolvedOutputPath(BuildTarget.StandaloneWindows64));
            }
            finally
            {
                Object.DestroyImmediate(buildProfile);
            }
        }

        [Test]
        public void ProfileName_Placeholder_FallsBackToAssetName_WithoutBuildProfile()
        {
            profile.name = "QuestRelease";
            SetOutputPath("Builds/{ProfileName}");
            var result = profile.GetResolvedOutputPath(BuildTarget.Android);
            Assert.That(result, Does.Contain("QuestRelease"));
            Assert.That(result, Does.Not.Contain("{ProfileName}"));
        }

        [Test]
        public void Target_Placeholder_ReplacedWithBuildTarget()
        {
            SetOutputPath("Builds/{Target}/output");
            var result = profile.GetResolvedOutputPath(BuildTarget.Android);
            Assert.That(result, Does.Contain("Android"));
            Assert.That(result, Does.Not.Contain("{Target}"));
        }

        [Test]
        public void ProjectName_Placeholder_Replaced()
        {
            SetOutputPath("Builds/{ProjectName}");
            var result = profile.GetResolvedOutputPath(BuildTarget.Android);
            Assert.That(result, Does.Not.Contain("{ProjectName}"));
        }

        [Test]
        public void Android_AppendsApk()
        {
            SetOutputPath("Builds/output");
            var result = profile.GetResolvedOutputPath(BuildTarget.Android);
            Assert.That(result, Does.EndWith(".apk").Or.EndWith(".aab"));
        }

        [Test]
        public void Windows_AppendsExe()
        {
            SetOutputPath("Builds/output");
            var result = profile.GetResolvedOutputPath(BuildTarget.StandaloneWindows64);
            Assert.That(result, Does.EndWith(".exe"));
        }

        [Test]
        public void MacOS_AppendsApp()
        {
            SetOutputPath("Builds/output");
            var result = profile.GetResolvedOutputPath(BuildTarget.StandaloneOSX);
            Assert.That(result, Does.EndWith(".app"));
        }

        [Test]
        public void WebGL_NoExtension()
        {
            SetOutputPath("Builds/output");
            var result = profile.GetResolvedOutputPath(BuildTarget.WebGL);
            Assert.AreEqual("Builds/output", result);
        }

        [Test]
        public void ExistingExtension_NotDuplicated()
        {
            SetOutputPath("Builds/output.exe");
            var result = profile.GetResolvedOutputPath(BuildTarget.StandaloneWindows64);
            Assert.AreEqual("Builds/output.exe", result);
            Assert.That(result, Does.Not.EndWith(".exe.exe"));
        }

        [Test]
        public void ExistingExtension_CaseInsensitive()
        {
            SetOutputPath("Builds/output.EXE");
            var result = profile.GetResolvedOutputPath(BuildTarget.StandaloneWindows64);
            Assert.AreEqual("Builds/output.EXE", result);
        }

        [Test]
        public void AllPlaceholders_ResolvedTogether()
        {
            profile.name = "Release";
            SetOutputPath("Builds/{Target}/{ProfileName}/{ProjectName}");
            var result = profile.GetResolvedOutputPath(BuildTarget.StandaloneWindows64);
            Assert.That(result, Does.Contain("StandaloneWindows64"));
            Assert.That(result, Does.Contain("Release"));
            Assert.That(result, Does.EndWith(".exe"));
            Assert.That(result, Does.Not.Contain("{"));
        }
    }
}
