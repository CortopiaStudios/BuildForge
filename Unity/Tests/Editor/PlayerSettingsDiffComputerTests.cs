using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace BuildForge.Tests.Editor
{
    public class PlayerSettingsDiffComputerTests
    {
        [Test]
        public void IdenticalMaps_ProducesNoDiffs()
        {
            var profile = new Dictionary<string, string>
            {
                ["productName"] = "MyGame",
                ["bundleVersion"] = "1.0",
            };
            var platform = new Dictionary<string, string>(profile);

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64);

            Assert.AreEqual(0, diffs.Count);
        }

        [Test]
        public void DifferentValues_ProducesDiff()
        {
            var profile = new Dictionary<string, string>
            {
                ["productName"] = "MyGame",
                ["bundleVersion"] = "2.0",
            };
            var platform = new Dictionary<string, string>
            {
                ["productName"] = "MyGame",
                ["bundleVersion"] = "1.0",
            };

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64);

            Assert.AreEqual(1, diffs.Count);
            Assert.AreEqual("bundleVersion", diffs[0].PropertyPath);
            Assert.AreEqual("1.0", diffs[0].BaseValue);
            Assert.AreEqual("2.0", diffs[0].ProfileValue);
        }

        [Test]
        public void NullRefsNormalized_NoDiff()
        {
            var profile = new Dictionary<string, string>
            {
                ["defaultCursor"] = "{instanceID: 0}",
            };
            var platform = new Dictionary<string, string>
            {
                ["defaultCursor"] = "{fileID: 0}",
            };

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64);

            Assert.AreEqual(0, diffs.Count);
        }

        [Test]
        public void EmptyAndBrackets_NoDiff()
        {
            var profile = new Dictionary<string, string>
            {
                ["preloadedAssets"] = "",
            };
            var platform = new Dictionary<string, string>
            {
                ["preloadedAssets"] = "[]",
            };

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64);

            Assert.AreEqual(0, diffs.Count);
        }

        [Test]
        public void NoisyProperties_Filtered()
        {
            var profile = new Dictionary<string, string>
            {
                ["productGUID"] = "abc123",
                ["serializedVersion"] = "28",
                ["m_ObjectHideFlags"] = "0",
                ["productName"] = "MyGame",
            };
            var platform = new Dictionary<string, string>
            {
                ["productGUID"] = "different",
                ["serializedVersion"] = "27",
                ["m_ObjectHideFlags"] = "1",
                ["productName"] = "MyGame",
            };

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64);

            Assert.AreEqual(0, diffs.Count);
        }

        [Test]
        public void PerPlatformProperty_OtherTargetsIgnored()
        {
            var profile = new Dictionary<string, string>
            {
                ["scriptingBackend"] = "  Android: 1",
            };
            var platform = new Dictionary<string, string>
            {
                ["scriptingBackend"] = "  Android: 1\n  Standalone: 0",
            };

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.Android);

            Assert.AreEqual(0, diffs.Count);
        }

        [TestCase("applicationIdentifier", "com.example.base", "com.example.profile")]
        [TestCase("scriptingBackend", "0", "1")]
        [TestCase("managedStrippingLevel", "1", "3")]
        public void PerPlatformDictionary_SelectedTargetDifferenceReported(string property, string before, string after)
        {
            var profile = new Dictionary<string, string> { [property] = $"    Standalone: {after}" };
            var platform = new Dictionary<string, string>
            {
                [property] = $"    Android: 1\n    Standalone: {before}\n    iPhone: 1",
            };

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64);

            Assert.AreEqual(1, diffs.Count);
            Assert.AreEqual(property, diffs[0].PropertyPath);
            StringAssert.Contains(before, diffs[0].BaseValue);
            StringAssert.Contains(after, diffs[0].ProfileValue);
            StringAssert.DoesNotContain("Android", diffs[0].BaseValue);
        }

        [TestCase("  - m_BuildTarget: Android\n    m_Encoding: ")]
        [TestCase("  - serializedVersion: 2\n    m_BuildTarget: Android\n    m_Encoding: ")]
        [TestCase("  - first: Android\n    second: ")]
        public void PerPlatformSequence_CompleteSelectedBlockCompared(string prefix)
        {
            var profile = new Dictionary<string, string> { ["setting"] = prefix + "1" };
            var platform = new Dictionary<string, string>
            {
                ["setting"] = prefix + "0\n  - m_BuildTarget: Standalone\n    m_Encoding: 7",
            };

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.Android);

            Assert.AreEqual(1, diffs.Count);
            StringAssert.Contains("0", diffs[0].BaseValue);
            StringAssert.Contains("1", diffs[0].ProfileValue);
            StringAssert.DoesNotContain("Standalone", diffs[0].BaseValue);
        }

        [TestCase(BuildTarget.iOS, "iPhone")]
        [TestCase(BuildTarget.iOS, "iOSSupport")]
        [TestCase(BuildTarget.Android, "AndroidPlayer")]
        [TestCase(BuildTarget.tvOS, "AppleTVSupport")]
        [TestCase(BuildTarget.WebGL, "WebGLSupport")]
        [TestCase(BuildTarget.StandaloneWindows64, "WindowsStandaloneSupport")]
        [TestCase(BuildTarget.StandaloneLinux64, "LinuxStandaloneSupport")]
        [TestCase(BuildTarget.StandaloneOSX, "MacStandaloneSupport")]
        public void PerPlatformSequence_TargetAliasesIncluded(BuildTarget target, string key)
        {
            var profile = new Dictionary<string, string> { ["setting"] = $"  - m_BuildTarget: {key}\n    m_Automatic: 0" };
            var platform = new Dictionary<string, string> { ["setting"] = $"  - m_BuildTarget: {key}\n    m_Automatic: 1" };

            Assert.AreEqual(1, PlayerSettingsDiffComputer.ComputeDiff(profile, platform, target).Count);
        }

        [Test]
        public void PerPlatformSequence_OtherStandaloneOperatingSystemsIgnored()
        {
            var profile = new Dictionary<string, string> { ["setting"] = "[]" };
            var platform = new Dictionary<string, string>
            {
                ["setting"] = "  - m_BuildTarget: MacStandaloneSupport\n    m_Automatic: 0",
            };

            Assert.IsEmpty(PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64));
        }

        [Test]
        public void PerPlatformDictionary_MissingTargetShownAsDefault()
        {
            var profile = new Dictionary<string, string> { ["scriptingBackend"] = "  Android: 1" };
            var platform = new Dictionary<string, string> { ["scriptingBackend"] = "{}" };

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.Android);

            Assert.AreEqual(1, diffs.Count);
            Assert.AreEqual("(default)", diffs[0].BaseValue);
        }

        [Test]
        public void NativeUnityProfile_ReportsSelectedTargetValues()
        {
            // Extracted from native Unity 6000.3.0f1 PlayerSettings YAML; the
            // same extraction and comparison were verified on 6000.6.0f1.
            const string fixtures = "Packages/com.cortopiastudios.buildforge/Tests/Editor/Fixtures/";
            var profile = UnityYamlParser.ParseToPropertyMap(File.ReadAllText(fixtures + "PerPlatformProfile.txt"));
            var platform = UnityYamlParser.ParseToPropertyMap(File.ReadAllText(fixtures + "PerPlatformGlobal.txt"));

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64);

            CollectionAssert.AreEquivalent(new[] { "applicationIdentifier", "scriptingBackend", "managedStrippingLevel" },
                diffs.Select(diff => diff.PropertyPath));
            Assert.IsEmpty(PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.Android));
        }

        [Test]
        public void ProfileKeyNotInPlatform_Skipped()
        {
            var profile = new Dictionary<string, string>
            {
                ["unknownKey"] = "someValue",
            };
            var platform = new Dictionary<string, string>();

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64);

            Assert.AreEqual(0, diffs.Count);
        }

        [Test]
        public void WhitespaceNormalized_NoDiff()
        {
            var profile = new Dictionary<string, string>
            {
                ["splashScreen"] = "  show: 1\n    mode: 0",
            };
            var platform = new Dictionary<string, string>
            {
                ["splashScreen"] = "show: 1 mode: 0",
            };

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64);

            Assert.AreEqual(0, diffs.Count);
        }

        [Test]
        public void NormalizeValue_NullRef()
        {
            Assert.AreEqual("__null_ref__", PlayerSettingsDiffComputer.NormalizeValue("{fileID: 0}"));
            Assert.AreEqual("__null_ref__", PlayerSettingsDiffComputer.NormalizeValue("{instanceID: 0}"));
        }

        [Test]
        public void NormalizeValue_Empty()
        {
            Assert.AreEqual("__empty__", PlayerSettingsDiffComputer.NormalizeValue(""));
            Assert.AreEqual("__empty__", PlayerSettingsDiffComputer.NormalizeValue("[]"));
        }

        [Test]
        public void IsPerPlatformProperty_Detects()
        {
            Assert.IsTrue(PlayerSettingsDiffComputer.IsPerPlatformProperty("  Android: 1\n  iPhone: 1"));
            Assert.IsTrue(PlayerSettingsDiffComputer.IsPerPlatformProperty("  m_BuildTarget: Android\n  m_APIs: 15"));
            Assert.IsTrue(PlayerSettingsDiffComputer.IsPerPlatformProperty("  - m_BuildTarget: iOSSupport\n  m_APIs: 10"));
        }

        [Test]
        public void IsPerPlatformProperty_SingleLine_ReturnsFalse()
        {
            Assert.IsFalse(PlayerSettingsDiffComputer.IsPerPlatformProperty("1920"));
            Assert.IsFalse(PlayerSettingsDiffComputer.IsPerPlatformProperty("{x: 0, y: 0}"));
        }

        [Test]
        public void IsNoisyProperty_Filters()
        {
            Assert.IsTrue(PlayerSettingsDiffComputer.IsNoisyProperty("productGUID"));
            Assert.IsTrue(PlayerSettingsDiffComputer.IsNoisyProperty("serializedVersion"));
            Assert.IsTrue(PlayerSettingsDiffComputer.IsNoisyProperty("clonedFromGUID"));
            Assert.IsTrue(PlayerSettingsDiffComputer.IsNoisyProperty("m_ObjectHideFlags"));
            Assert.IsFalse(PlayerSettingsDiffComputer.IsNoisyProperty("productName"));
        }

        [Test]
        public void NestedNullRef_DifferentForms_NoDiff()
        {
            // {fileID: 0} (ProjectSettings) and {instanceID: 0} (Build Profile YAML)
            // are both null references — a struct differing only in that form is equal.
            var profile = new Dictionary<string, string>
            {
                ["m_AndroidBanners"] = "    height: 180\n    banner: {instanceID: 0}",
            };
            var platform = new Dictionary<string, string>
            {
                ["m_AndroidBanners"] = "    height: 180\n    banner: {fileID: 0}",
            };

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64);
            Assert.AreEqual(0, diffs.Count);
        }

        [Test]
        public void NestedNullRef_CanonicalizedInNormalize()
        {
            Assert.AreEqual(
                PlayerSettingsDiffComputer.NormalizeValue("height: 180 banner: {fileID: 0}"),
                PlayerSettingsDiffComputer.NormalizeValue("height: 180\n    banner: {instanceID: 0}"));
        }

        [Test]
        public void StructWithRealDifference_StillDiffs()
        {
            var profile = new Dictionary<string, string>
            {
                ["m_AndroidBanners"] = "    height: 320\n    banner: {instanceID: 0}",
            };
            var platform = new Dictionary<string, string>
            {
                ["m_AndroidBanners"] = "    height: 180\n    banner: {fileID: 0}",
            };

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64);
            Assert.AreEqual(1, diffs.Count);
        }

        [Test]
        public void FormatDisplayName_StripsMPrefix()
        {
            Assert.AreEqual("Active Color Space", PlayerSettingsDiffComputer.FormatDisplayName("m_ActiveColorSpace"));
        }

        [Test]
        public void FormatDisplayName_CamelCaseSpaces()
        {
            Assert.AreEqual("Bundle Version", PlayerSettingsDiffComputer.FormatDisplayName("bundleVersion"));
        }

        [Test]
        public void FormatDisplayName_Override()
        {
            Assert.AreEqual("GPU Skinning", PlayerSettingsDiffComputer.FormatDisplayName("meshDeformation"));
        }

        [Test]
        public void FormatYamlValue_Empty()
        {
            Assert.AreEqual("(empty)", PlayerSettingsDiffComputer.FormatYamlValue(""));
            Assert.AreEqual("(empty)", PlayerSettingsDiffComputer.FormatYamlValue(null));
        }

        [Test]
        public void FormatYamlValue_EmptyArray()
        {
            Assert.AreEqual("(default)", PlayerSettingsDiffComputer.FormatYamlValue("[]"));
        }

        [Test]
        public void FormatYamlValue_SimpleScalar()
        {
            Assert.AreEqual("1920", PlayerSettingsDiffComputer.FormatYamlValue("1920"));
        }

        [Test]
        public void FormatYamlValue_MultiLine()
        {
            var value = "m_StaticBatching: 1\n    m_DynamicBatching: 0";
            var result = PlayerSettingsDiffComputer.FormatYamlValue(value);
            Assert.That(result, Does.Contain("Static Batching: 1"));
            Assert.That(result, Does.Contain("Dynamic Batching: 0"));
        }

        // Unity's ProjectSettings format: a compact sequence, one entry per platform.
        static Dictionary<string, string> Batching(int standaloneStatic, int androidStatic) =>
            UnityYamlParser.ParseToPropertyMap(
                "PlayerSettings:\n" +
                "  m_BuildTargetBatching:\n" +
                "  - m_BuildTarget: Standalone\n" +
                $"    m_StaticBatching: {standaloneStatic}\n" +
                "    m_DynamicBatching: 0\n" +
                "  - m_BuildTarget: Android\n" +
                $"    m_StaticBatching: {androidStatic}\n" +
                "    m_DynamicBatching: 0\n");

        [Test]
        public void Batching_IsComparedForTheProfilesPlatform_AsStaticAndDynamicRows()
        {
            // The profile changes the Standalone entry only.
            var diffs = PlayerSettingsDiffComputer.ComputeDiff(Batching(0, 0), Batching(1, 0), BuildTarget.StandaloneWindows64);
            Assert.AreEqual(1, diffs.Count);
            Assert.AreEqual("m_StaticBatching", diffs[0].PropertyPath);
            Assert.AreEqual("Static Batching", diffs[0].DisplayName);
            Assert.AreEqual("1", diffs[0].BaseValue);
            Assert.AreEqual("0", diffs[0].ProfileValue);
            Assert.IsEmpty(PlayerSettingsDiffComputer.ComputeDiff(Batching(0, 0), Batching(1, 0), BuildTarget.Android));

            // The profile changes the Android entry only.
            Assert.IsEmpty(PlayerSettingsDiffComputer.ComputeDiff(Batching(1, 1), Batching(1, 0), BuildTarget.StandaloneWindows64));
            Assert.AreEqual(1, PlayerSettingsDiffComputer.ComputeDiff(Batching(1, 1), Batching(1, 0), BuildTarget.Android).Count);
        }

        [Test]
        public void Batching_EmptyOnBothSides_NoDifference()
        {
            var empty = new Dictionary<string, string> { ["m_BuildTargetBatching"] = "[]" };
            Assert.IsEmpty(PlayerSettingsDiffComputer.ComputeDiff(empty, new Dictionary<string, string>(empty), BuildTarget.Android));
        }

        [Test]
        public void DedicatedServerProfile_IsComparedOnServerEntries()
        {
            var profile = new Dictionary<string, string> { ["scriptingBackend"] = "    Server: 1\n    Standalone: 0" };
            var platform = new Dictionary<string, string> { ["scriptingBackend"] = "    Server: 0\n    Standalone: 0" };

            var diffs = PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneLinux64, dedicatedServer: true);
            Assert.AreEqual(1, diffs.Count);
            StringAssert.Contains("Server: 1", diffs[0].ProfileValue);
            Assert.IsEmpty(PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneLinux64),
                "A player profile compares the Standalone entries.");
        }

        [Test]
        public void ServerOnlyEntry_IsIgnoredForAPlayerProfile()
        {
            var profile = new Dictionary<string, string> { ["scriptingBackend"] = "    Server: 1" };
            var platform = new Dictionary<string, string> { ["scriptingBackend"] = "    Server: 0" };
            Assert.IsEmpty(PlayerSettingsDiffComputer.ComputeDiff(profile, platform, BuildTarget.StandaloneWindows64));
        }

        [Test]
        public void IsDedicatedServer_ReadsTheProfilesStandaloneSubtarget()
        {
            var profile = (BuildProfile)typeof(BuildProfile).GetMethod("CreateInstance", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(BuildTarget), typeof(StandaloneBuildSubtarget) }, null)
                .Invoke(null, new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
            try
            {
                Assert.IsFalse(BuildProfileUtility.IsDedicatedServer(profile));
                var serialized = new SerializedObject(profile);
                serialized.FindProperty("m_Subtarget").intValue = (int)StandaloneBuildSubtarget.Server;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsTrue(BuildProfileUtility.IsDedicatedServer(profile));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }
    }
}
