using System.Collections.Generic;
using System.IO;
using BuildForge.Editor.Core;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class UnityYamlParserTests
    {
        [Test]
        public void EmptyInput_ReturnsEmptyDictionary()
        {
            var result = UnityYamlParser.ParseToPropertyMap("");
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void NullInput_ReturnsEmptyDictionary()
        {
            var result = UnityYamlParser.ParseToPropertyMap((string)null);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void SimpleScalars_UnderPlayerSettingsRoot()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  productName: MyGame\n" +
                "  companyName: MyCompany\n" +
                "  bundleVersion: 1.0.0\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.AreEqual("MyGame", result["productName"]);
            Assert.AreEqual("MyCompany", result["companyName"]);
            Assert.AreEqual("1.0.0", result["bundleVersion"]);
        }

        [Test]
        public void NumericAndBooleanValues()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  defaultScreenWidth: 1920\n" +
                "  defaultScreenHeight: 1080\n" +
                "  runInBackground: 1\n" +
                "  forceSingleInstance: 0\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.AreEqual("1920", result["defaultScreenWidth"]);
            Assert.AreEqual("1080", result["defaultScreenHeight"]);
            Assert.AreEqual("1", result["runInBackground"]);
            Assert.AreEqual("0", result["forceSingleInstance"]);
        }

        [Test]
        public void PlayerSettingsRoot_IsStripped()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  productName: Test\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.IsFalse(result.ContainsKey("PlayerSettings"));
            Assert.IsTrue(result.ContainsKey("productName"));
        }

        [Test]
        public void TopLevelKeys_WithoutPlayerSettingsRoot()
        {
            var yaml =
                "someKey: someValue\n" +
                "anotherKey: anotherValue\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.AreEqual("someValue", result["someKey"]);
            Assert.AreEqual("anotherValue", result["anotherKey"]);
        }

        [Test]
        public void NestedMapping_CollectedAsText()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  iPhoneSplashScreen:\n" +
                "    m_ShowUnitySplashLogo: 1\n" +
                "    m_ShowUnitySplashScreen: 1\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.IsTrue(result.ContainsKey("iPhoneSplashScreen"));
            var value = result["iPhoneSplashScreen"];
            Assert.That(value, Does.Contain("m_ShowUnitySplashLogo"));
            Assert.That(value, Does.Contain("m_ShowUnitySplashScreen"));
        }

        [Test]
        public void EmptyInlineValue()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  preloadedAssets: []\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.AreEqual("[]", result["preloadedAssets"]);
        }

        [Test]
        public void Sequence_UnityStyle_DashesAtKeyIndent_PreservedInValue()
        {
            // Unity's YAML emitter writes block-sequence dashes at the SAME
            // indentation as the parent key. These lines must survive parsing —
            // per-platform filtering matches on them.
            var yaml =
                "PlayerSettings:\n" +
                "  m_BuildTargetGraphicsAPIs:\n" +
                "  - m_BuildTarget: iOSSupport\n" +
                "    m_APIs: 10000000\n" +
                "    m_Automatic: 1\n" +
                "  - m_BuildTarget: AndroidPlayer\n" +
                "    m_APIs: 150000000b000000\n" +
                "    m_Automatic: 0\n" +
                "  bundleVersion: 1.0\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            var value = result["m_BuildTargetGraphicsAPIs"];
            Assert.That(value, Does.Contain("- m_BuildTarget: iOSSupport"));
            Assert.That(value, Does.Contain("- m_BuildTarget: AndroidPlayer"));
            Assert.That(value, Does.Contain("m_APIs: 10000000"));

            // The dash lines must not break the boundary to the next key.
            Assert.AreEqual("1.0", result["bundleVersion"]);
            Assert.AreEqual(2, result.Count);
        }

        [Test]
        public void Sequence_UnityStyle_ParsedValueIsRecognizedAsPerPlatform()
        {
            // Integration with the diff computer: per-platform arrays must be
            // recognized (and thus suppressed from diffs) from real parser output.
            var yaml =
                "PlayerSettings:\n" +
                "  m_BuildTargetGraphicsAPIs:\n" +
                "  - m_BuildTarget: iOSSupport\n" +
                "    m_APIs: 10000000\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.IsTrue(PlayerSettingsDiffComputer.IsPerPlatformProperty(
                result["m_BuildTargetGraphicsAPIs"]));
        }

        [Test]
        public void Sequence_WithScalarItems()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  scriptingDefineSymbols:\n" +
                "    - ENABLE_FEATURE_A\n" +
                "    - ENABLE_FEATURE_B\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.IsTrue(result.ContainsKey("scriptingDefineSymbols"));
            var value = result["scriptingDefineSymbols"];
            Assert.That(value, Does.Contain("ENABLE_FEATURE_A"));
            Assert.That(value, Does.Contain("ENABLE_FEATURE_B"));
        }

        [Test]
        public void ListInput_ParsesSameAsString()
        {
            var lines = new List<string>
            {
                "PlayerSettings:",
                "  productName: FromList",
                "  companyName: ListCo"
            };

            var result = UnityYamlParser.ParseToPropertyMap(lines);

            Assert.AreEqual("FromList", result["productName"]);
            Assert.AreEqual("ListCo", result["companyName"]);
        }

        [Test]
        public void RealisticPlayerSettings_ParsesAllProperties()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  productName: BuildForgeTest\n" +
                "  companyName: TestCompany\n" +
                "  defaultScreenWidth: 1920\n" +
                "  defaultScreenHeight: 1080\n" +
                "  runInBackground: 1\n" +
                "  muteOtherAudioSources: 0\n" +
                "  allowedAutorotateToPortrait: 1\n" +
                "  allowedAutorotateToPortraitUpsideDown: 1\n" +
                "  allowedAutorotateToLandscapeRight: 1\n" +
                "  allowedAutorotateToLandscapeLeft: 1\n" +
                "  useOnDemandResources: 0\n" +
                "  accelerometerFrequency: 60\n" +
                "  activeColorSpace: 1\n" +
                "  stripUnusedMeshComponents: 1\n" +
                "  vertexChannelCompressionMask: 214\n" +
                "  bundleVersion: 0.1\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.AreEqual("BuildForgeTest", result["productName"]);
            Assert.AreEqual("TestCompany", result["companyName"]);
            Assert.AreEqual("1920", result["defaultScreenWidth"]);
            Assert.AreEqual("1080", result["defaultScreenHeight"]);
            Assert.AreEqual("1", result["runInBackground"]);
            Assert.AreEqual("60", result["accelerometerFrequency"]);
            Assert.AreEqual("1", result["activeColorSpace"]);
            Assert.AreEqual("214", result["vertexChannelCompressionMask"]);
            Assert.AreEqual("0.1", result["bundleVersion"]);
            Assert.AreEqual(16, result.Count);
        }

        [Test]
        public void NestedMapping_WithMultipleLevels()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  splashScreenSettings:\n" +
                "    m_ShowUnitySplashLogo: 1\n" +
                "    m_DrawMode: 0\n" +
                "    m_BackgroundColor:\n" +
                "      r: 0.13\n" +
                "      g: 0.17\n" +
                "      b: 0.26\n" +
                "      a: 1\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.IsTrue(result.ContainsKey("splashScreenSettings"));
            var value = result["splashScreenSettings"];
            Assert.That(value, Does.Contain("m_ShowUnitySplashLogo"));
            Assert.That(value, Does.Contain("m_DrawMode"));
            Assert.That(value, Does.Contain("m_BackgroundColor"));
            Assert.That(value, Does.Contain("0.13"));
        }

        [Test]
        public void Sequence_WithMappingItems()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  preloadedAssets:\n" +
                "    - m_Name: Shader1\n" +
                "      m_Type: 0\n" +
                "    - m_Name: Shader2\n" +
                "      m_Type: 0\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.IsTrue(result.ContainsKey("preloadedAssets"));
            var value = result["preloadedAssets"];
            Assert.That(value, Does.Contain("Shader1"));
            Assert.That(value, Does.Contain("Shader2"));
        }

        [Test]
        public void EmptyValueProperty()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  applicationIdentifier: \n" +
                "  productName: HasValue\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.IsTrue(result.ContainsKey("applicationIdentifier"));
            Assert.AreEqual("HasValue", result["productName"]);
        }

        [Test]
        public void QuotedStringValues_PreservedAsIs()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  productName: 'My Game'\n" +
                "  companyName: \"My Company\"\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.AreEqual("'My Game'", result["productName"]);
            Assert.AreEqual("\"My Company\"", result["companyName"]);
        }

        [Test]
        public void PropertyCount_MatchesInput()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  a: 1\n" +
                "  b: 2\n" +
                "  c: 3\n" +
                "  d: 4\n" +
                "  e: 5\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.AreEqual(5, result.Count);
        }

        [Test]
        public void DuplicateKeys_LastWins()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  productName: First\n" +
                "  productName: Second\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.AreEqual("Second", result["productName"]);
        }

        [Test]
        public void InlineFlowMapping()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  cursorHotspot: {x: 0, y: 0}\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.IsTrue(result.ContainsKey("cursorHotspot"));
            var value = result["cursorHotspot"];
            Assert.That(value, Does.Contain("x"));
            Assert.That(value, Does.Contain("y"));
        }

        [Test]
        public void MixedPropertyTypes()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  productName: TestApp\n" +
                "  defaultScreenWidth: 1280\n" +
                "  preloadedAssets: []\n" +
                "  cursorHotspot: {x: 0, y: 0}\n" +
                "  splashScreen:\n" +
                "    show: 1\n" +
                "  bundleVersion: 2.0\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.AreEqual("TestApp", result["productName"]);
            Assert.AreEqual("1280", result["defaultScreenWidth"]);
            Assert.AreEqual("[]", result["preloadedAssets"]);
            Assert.IsTrue(result.ContainsKey("cursorHotspot"));
            Assert.IsTrue(result.ContainsKey("splashScreen"));
            Assert.AreEqual("2.0", result["bundleVersion"]);
            Assert.AreEqual(6, result.Count);
        }

        [Test]
        public void UnterminatedQuotes_StillParses()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  productName: MyGame\n" +
                "  webGLTemplate: APPLICATION:{Create|{0}}\n" +
                "  templateDefaultScene: 'unterminated\n" +
                "  bundleVersion: 1.0\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.AreEqual("MyGame", result["productName"]);
            Assert.AreEqual("1.0", result["bundleVersion"]);
        }

        [Test]
        public void ValuesWithColons()
        {
            var yaml =
                "PlayerSettings:\n" +
                "  webGLTemplate: APPLICATION:{Create|{0}}\n" +
                "  bundleVersion: 1.0\n";

            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            Assert.AreEqual("APPLICATION:{Create|{0}}", result["webGLTemplate"]);
            Assert.AreEqual("1.0", result["bundleVersion"]);
        }

        static string LoadFixture(string filename)
        {
            var packagePath = Path.GetFullPath("Packages/com.cortopiastudios.buildforge");
            var fixturePath = Path.Combine(packagePath, "Tests", "Editor", "Fixtures", filename);
            return File.ReadAllText(fixturePath);
        }

        // The fixture is a real ProjectSettings.asset without the console platforms' keys.
        [Test]
        public void RealProjectSettings_ParsesAllTopLevelKeys()
        {
            var yaml = LoadFixture("ProjectSettings.txt");
            var result = UnityYamlParser.ParseToPropertyMap(yaml);

            // Scalars
            Assert.AreEqual("DefaultCompany", result["companyName"]);
            Assert.AreEqual("bpp-dev", result["productName"]);
            Assert.AreEqual("0.1.0", result["bundleVersion"]);
            Assert.AreEqual("60", result["accelerometerFrequency"]);
            Assert.AreEqual("1", result["m_ActiveColorSpace"]);
            Assert.AreEqual("0", result["runInBackground"]);

            // Flow mappings
            Assert.AreEqual("{fileID: 0}", result["defaultCursor"]);
            Assert.AreEqual("{x: 0, y: 0}", result["cursorHotspot"]);

            // Empty sequences
            Assert.AreEqual("[]", result["preloadedAssets"]);
            Assert.AreEqual("[]", result["m_BuildTargetBatching"]);

            // Empty flow mappings
            Assert.AreEqual("{}", result["m_TemplateCustomTags"]);
            Assert.AreEqual("{}", result["scriptingDefineSymbols"]);

            // Nested mappings
            Assert.IsTrue(result.ContainsKey("applicationIdentifier"));
            Assert.That(result["applicationIdentifier"], Does.Contain("Android"));
            Assert.That(result["applicationIdentifier"], Does.Contain("Standalone"));

            // Nested mapping with sequence — the "- m_BuildTarget: X" discriminator
            // lines (written at the parent key's indent by Unity) must be preserved.
            Assert.IsTrue(result.ContainsKey("m_BuildTargetPlatformIcons"));
            Assert.That(result["m_BuildTargetPlatformIcons"], Does.Contain("- m_BuildTarget: iPhone"));
            Assert.That(result["m_BuildTargetGraphicsAPIs"], Does.Contain("- m_BuildTarget: iOSSupport"));
            Assert.That(result["m_BuildTargetGraphicsAPIs"], Does.Contain("- m_BuildTarget: AndroidPlayer"));
            Assert.IsTrue(PlayerSettingsDiffComputer.IsPerPlatformProperty(result["m_BuildTargetGraphicsAPIs"]));

            // Values with special characters
            Assert.AreEqual("APPLICATION:Default", result["webGLTemplate"]);
            Assert.AreEqual("com.unity.template.urp-blank@17.0.14", result["templatePackageId"]);
            Assert.AreEqual("Assets/Scenes/SampleScene.unity", result["templateDefaultScene"]);

            // Empty values
            Assert.AreEqual("", result["cameraUsageDescription"]);
            Assert.AreEqual("", result["aotOptions"]);

            // Verify PlayerSettings root is not included
            Assert.IsFalse(result.ContainsKey("PlayerSettings"));

            // Sanity check — should have hundreds of keys
            Assert.Greater(result.Count, 200);
        }
    }
}
