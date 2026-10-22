using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.OpenXR;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace BuildForge.Tests.OpenXR
{
    /// <summary>
    /// A feature's on/off callback can write another platform's OpenXR settings. Builds, Apply and the
    /// Play Mode copy keep their writes in the platform they write: they put back each other platform
    /// that changed, as data, before they save, and warn.
    /// </summary>
    public class OpenXRCrossPlatformCallbackTests
    {
        OpenXREditorStateSnapshot standaloneOriginal, androidOriginal;

        [SetUp] public void SetUp()
        {
            standaloneOriginal = null;
            androidOriginal = null;
        }

        [TearDown] public void TearDown()
        {
            TestActivePlatformCallbackFeature.ActivePlatform = null;
            TestActivePlatformCallbackFeature.CallbackCount = 0;
            if (standaloneOriginal != null)
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, standaloneOriginal);
            if (androidOriginal != null)
                OpenXRSettingsState.Restore(BuildTargetGroup.Android, androidOriginal);
        }

        /// <summary>
        /// Standalone's and Android's settings, with both test features in them and off, in a host with
        /// Standalone active. TearDown puts back what they held before.
        /// </summary>
        (OpenXRSettings standalone, OpenXRSettings android) Platforms()
        {
            Assume.That(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget), Is.EqualTo(BuildTargetGroup.Standalone),
                "These tests need a host with Standalone active.");
            Assume.That(BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android),
                "This editor has no Android Build Support.");
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            var android = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            Assert.That(android, Is.Not.Null, "The OpenXR test host must contain Android settings.");
            foreach (var settings in new[] { standalone, android })
            {
                Assert.IsNotNull(settings.GetFeature<TestActivePlatformCallbackFeature>(), "OpenXR adds the test features to both platforms.");
                Assert.IsNotNull(settings.GetFeature<TestPrerequisiteFeature>(), "OpenXR adds the test features to both platforms.");
            }
            standaloneOriginal = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            androidOriginal = OpenXRSettingsState.Capture(BuildTargetGroup.Android, true);
            foreach (var settings in new[] { standalone, android })
            {
                OpenXROverrideTests.SetEnabledAsData(settings.GetFeature<TestActivePlatformCallbackFeature>(), false);
                OpenXROverrideTests.SetEnabledAsData(settings.GetFeature<TestPrerequisiteFeature>(), false);
            }
            return (standalone, android);
        }

        static string Snapshot(BuildTargetGroup group) => OpenXRSettingsState.Capture(group, true).ToJson();

        /// <summary>
        /// The warning that writing <paramref name="written"/>'s features turned on the prerequisite for <paramref name="platform"/>,
        /// which Build Forge put back, ending with the advice to enable it in the profile only with <paramref name="advice"/>.
        /// </summary>
        static Regex PutBack(BuildTargetGroup written, BuildTargetGroup platform, bool advice)
            => new Regex(Regex.Escape(
                $"Turning OpenXR features on and off for {written} also turned on '{TestPrerequisiteFeature.Name}' for {platform}, " +
                $"through a feature's on/off callback; Build Forge put {platform} back." +
                (advice ? $" If {written} needs '{TestPrerequisiteFeature.Name}', enable it in the profile." : "")) + "$");

        /// <summary>OpenXRFeature's own on/off field as the feature's asset file holds it: what the last save wrote.</summary>
        static bool SavedEnabled(OpenXRFeature feature)
        {
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long fileId), "The feature is saved in an asset.");
            var text = File.ReadAllText(AssetDatabase.GetAssetPath(feature));
            Assert.That(text, Does.StartWith("%YAML"), "The test host saves its assets as text.");
            var document = Regex.Match(text, "--- !u!114 &" + fileId + @"\r?\n(.*?)(?=\r?\n--- |\z)", RegexOptions.Singleline);
            Assert.IsTrue(document.Success, $"The asset holds no document for {feature.name}.");
            // At the document's top level; the test feature's nested field of the same name is indented further.
            var enabled = Regex.Match(document.Groups[1].Value, @"^  " + OpenXRSettingsState.EnabledField + @": (\d)\r?$", RegexOptions.Multiline);
            Assert.IsTrue(enabled.Success, $"The saved {feature.name} has no {OpenXRSettingsState.EnabledField}.");
            return enabled.Groups[1].Value == "1";
        }

        // An Android profile applied while Standalone is active: the callback of a feature it turns on
        // also turns on Standalone's prerequisite. Build Forge puts Standalone back before anything is
        // saved, and says so, without saying to enable the prerequisite, which the profile turns on for
        // Android itself. With the Play Mode copy on, the copy then turns on Standalone's features
        // as Android has them, from the state Standalone had before the Apply. Revert to Baseline leaves
        // Standalone as it was.
        [TestCase(false)]
        [TestCase(true)]
        public void AnApplyForAnotherPlatform_PutsBackWhatACallbackTurnedOnForTheActivePlatform(bool mirrorEnabled)
        {
            var (standalone, android) = Platforms();
            var native = OpenXROverrideTests.CreateNativeProfile(BuildTarget.Android);
            var forge = OpenXROverrideTests.ForgeProfileFor(native);
            try
            {
                using (new OpenXROverrideTests.GlobalOptions(false, false))
                {
                    var standaloneBefore = Snapshot(BuildTargetGroup.Standalone);
                    var androidBefore = Snapshot(BuildTargetGroup.Android);
                    var config = new OpenXRProfileConfig();
                    // The prerequisite and the feature that needs it, both turned on for the profile's platform.
                    config.EnabledFeatures.Add(typeof(TestPrerequisiteFeature).FullName);
                    config.EnabledFeatures.Add(typeof(TestActivePlatformCallbackFeature).FullName);
                    forge.SetPluginConfig("OpenXR", config);
                    var plugin = new OpenXRPlugin();
                    var mirror = new PlayModeOpenXRFeatures();
                    var pairs = new List<EditorStatePair>
                    {
                        new EditorStatePair { PluginTypeName = "test.primary", Group = BuildTargetGroup.Android, Plugin = plugin }
                    };
                    // In ForgeEditorState.GetParticipatingPairs' order: the plugins, then the mirror.
                    if (mirrorEnabled)
                        pairs.Add(new EditorStatePair { PluginTypeName = "test.mirror", Group = BuildTargetGroup.Standalone, Plugin = mirror });
                    IForgeEditorApplicable Resolve(string name) => name == "test.primary" ? plugin : mirror;
                    var entries = new List<EditorStateEntry>();
                    LogAssert.Expect(LogType.Warning, PutBack(BuildTargetGroup.Android, BuildTargetGroup.Standalone, advice: false));

                    EditorStateApplier.Apply(entries, pairs, forge, Resolve, null);

                    Assert.IsTrue(android.GetFeature<TestPrerequisiteFeature>().enabled, "Android has the profile's features on.");
                    Assert.IsTrue(android.GetFeature<TestActivePlatformCallbackFeature>().enabled, "Android has the profile's features on.");
                    if (mirrorEnabled)
                    {
                        Assert.AreEqual(standaloneBefore, entries.Single(e => e.key.StartsWith("test.mirror|")).baselineJson,
                            "The Play Mode copy's baseline is Standalone as it was before the Apply, without the callback's write.");
                        Assert.IsTrue(standalone.GetFeature<TestPrerequisiteFeature>().enabled, "The copy turns on what Android has on.");
                        Assert.IsTrue(standalone.GetFeature<TestActivePlatformCallbackFeature>().enabled, "The copy turns on what Android has on.");
                    }
                    else
                    {
                        Assert.AreEqual(standaloneBefore, Snapshot(BuildTargetGroup.Standalone), "Standalone is as it was before the Apply.");
                        Assert.IsFalse(SavedEnabled(standalone.GetFeature<TestPrerequisiteFeature>()),
                            "The saved OpenXR settings don't hold the callback's write.");
                    }

                    EditorStateApplier.RevertAll(entries, Resolve);

                    CollectionAssert.IsEmpty(entries);
                    Assert.AreEqual(standaloneBefore, Snapshot(BuildTargetGroup.Standalone),
                        "Standalone's prerequisite was off before the Apply, and Revert leaves it so.");
                    Assert.AreEqual(androidBefore, Snapshot(BuildTargetGroup.Android));
                }
            }
            finally
            {
                Object.DestroyImmediate(forge);
                Object.DestroyImmediate(native);
            }
        }

        // The same Apply with a profile that turns on only the feature with the callback: Android is left
        // without the prerequisite the callback turned on for Standalone, so the warning says to enable
        // it in the profile. Standalone is as it was, and Revert leaves both platforms so.
        [Test]
        public void AnApplyForAnotherPlatform_SaysToEnableWhatACallbackTurnedOnElsewhere_WhenThePlatformHasItOff()
        {
            var (_, android) = Platforms();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                using (new OpenXROverrideTests.GlobalOptions(false, false))
                {
                    var standaloneBefore = Snapshot(BuildTargetGroup.Standalone);
                    var androidBefore = Snapshot(BuildTargetGroup.Android);
                    var config = new OpenXRProfileConfig();
                    config.EnabledFeatures.Add(typeof(TestActivePlatformCallbackFeature).FullName);
                    forge.SetPluginConfig("OpenXR", config);
                    var plugin = new OpenXRPlugin();
                    var pair = new EditorStatePair { PluginTypeName = nameof(OpenXRPlugin), Group = BuildTargetGroup.Android, Plugin = plugin };
                    var entries = new List<EditorStateEntry>();
                    LogAssert.Expect(LogType.Warning, PutBack(BuildTargetGroup.Android, BuildTargetGroup.Standalone, advice: true));

                    EditorStateApplier.Apply(entries, new[] { pair }, forge, _ => plugin, null);

                    Assert.IsTrue(android.GetFeature<TestActivePlatformCallbackFeature>().enabled, "Android has the profile's feature on.");
                    Assert.IsFalse(android.GetFeature<TestPrerequisiteFeature>().enabled, "Nothing turned on the prerequisite for Android.");
                    Assert.AreEqual(standaloneBefore, Snapshot(BuildTargetGroup.Standalone), "Standalone is as it was before the Apply.");

                    EditorStateApplier.RevertAll(entries, _ => plugin);

                    Assert.AreEqual(standaloneBefore, Snapshot(BuildTargetGroup.Standalone));
                    Assert.AreEqual(androidBefore, Snapshot(BuildTargetGroup.Android));
                }
            }
            finally
            {
                Object.DestroyImmediate(forge);
            }
        }

        // A callback that writes the platform being written, here Standalone's own while it is active,
        // is the vendor's intended side effect: it takes effect without a warning, and Revert undoes it
        // with the rest of the Apply.
        [Test]
        public void ACallbackWithinThePlatformBeingWritten_TakesEffect_AndRevertUndoesIt()
        {
            var (standalone, _) = Platforms();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            var warnings = new List<string>();
            void Collect(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Warning)
                    warnings.Add(message);
            }
            Application.logMessageReceived += Collect;
            try
            {
                using (new OpenXROverrideTests.GlobalOptions(false, false))
                {
                    var standaloneBefore = Snapshot(BuildTargetGroup.Standalone);
                    var androidBefore = Snapshot(BuildTargetGroup.Android);
                    var config = new OpenXRProfileConfig();
                    config.EnabledFeatures.Add(typeof(TestActivePlatformCallbackFeature).FullName);
                    forge.SetPluginConfig("OpenXR", config);
                    var plugin = new OpenXRPlugin();
                    var pair = new EditorStatePair { PluginTypeName = nameof(OpenXRPlugin), Group = BuildTargetGroup.Standalone, Plugin = plugin };
                    var entries = new List<EditorStateEntry>();

                    EditorStateApplier.Apply(entries, new[] { pair }, forge, _ => plugin, null);

                    Assert.IsTrue(standalone.GetFeature<TestActivePlatformCallbackFeature>().enabled);
                    Assert.IsTrue(standalone.GetFeature<TestPrerequisiteFeature>().enabled, "The callback's write within the platform stays.");
                    Assert.AreEqual(androidBefore, Snapshot(BuildTargetGroup.Android));
                    CollectionAssert.IsEmpty(warnings.Where(w => w.Contains("Build Forge put")), "Nothing was put back.");
                    CollectionAssert.IsEmpty(EditorStateApplier.Drift(entries, _ => plugin));

                    EditorStateApplier.RevertAll(entries, _ => plugin);

                    Assert.AreEqual(standaloneBefore, Snapshot(BuildTargetGroup.Standalone), "Revert undoes the callback's write with the Apply.");
                }
            }
            finally
            {
                Application.logMessageReceived -= Collect;
                Object.DestroyImmediate(forge);
            }
        }

        // With Android active, Play Mode Follows Applied Profile's copy turns on a feature for Standalone
        // whose callback turns on the prerequisite for Android, after the OpenXR plugin recorded what it
        // left there. Build Forge puts Android back, so nothing drifts, and Revert to Baseline leaves
        // Android as it was before the Apply instead of keeping the write as a change made after it.
        // Standalone has the prerequisite off, so the warning says to enable it.
        [Test]
        public void ThePlayModeCopy_PutsBackWhatACallbackTurnedOnForTheActivePlatform()
        {
            var (standalone, android) = Platforms();
            TestActivePlatformCallbackFeature.ActivePlatform = BuildTargetGroup.Android;
            var native = OpenXROverrideTests.CreateNativeProfile(BuildTarget.Android);
            var forge = OpenXROverrideTests.ForgeProfileFor(native);
            try
            {
                using (new OpenXROverrideTests.GlobalOptions(false, false))
                {
                    // Android has the feature on, and its prerequisite off.
                    OpenXROverrideTests.SetEnabledAsData(android.GetFeature<TestActivePlatformCallbackFeature>(), true);
                    var standaloneBefore = Snapshot(BuildTargetGroup.Standalone);
                    var androidBefore = Snapshot(BuildTargetGroup.Android);
                    var config = new OpenXRProfileConfig();
                    config.EnabledFeatures.Add(typeof(TestActivePlatformCallbackFeature).FullName);
                    forge.SetPluginConfig("OpenXR", config);
                    var plugin = new OpenXRPlugin();
                    var mirror = new PlayModeOpenXRFeatures();
                    var pairs = new List<EditorStatePair>
                    {
                        new EditorStatePair { PluginTypeName = "test.primary", Group = BuildTargetGroup.Android, Plugin = plugin },
                        new EditorStatePair { PluginTypeName = "test.mirror", Group = BuildTargetGroup.Standalone, Plugin = mirror },
                    };
                    IForgeEditorApplicable Resolve(string name) => name == "test.primary" ? plugin : mirror;
                    var entries = new List<EditorStateEntry>();
                    LogAssert.Expect(LogType.Warning, PutBack(BuildTargetGroup.Standalone, BuildTargetGroup.Android, advice: true));

                    EditorStateApplier.Apply(entries, pairs, forge, Resolve, null);

                    Assert.IsTrue(standalone.GetFeature<TestActivePlatformCallbackFeature>().enabled, "The copy turns on what Android has on.");
                    Assert.AreEqual(androidBefore, Snapshot(BuildTargetGroup.Android), "Android is as it was before the Apply.");
                    CollectionAssert.IsEmpty(EditorStateApplier.Drift(entries, Resolve));

                    EditorStateApplier.RevertAll(entries, Resolve);

                    Assert.AreEqual(androidBefore, Snapshot(BuildTargetGroup.Android));
                    Assert.AreEqual(standaloneBefore, Snapshot(BuildTargetGroup.Standalone));
                }
            }
            finally
            {
                Object.DestroyImmediate(forge);
                Object.DestroyImmediate(native);
            }
        }

        // After a build of a Standalone profile, the applied Android profile is applied again with
        // Standalone still active, the platform the build has just put back. The callback turns on
        // Standalone's prerequisite again; Build Forge puts Standalone back, so it stays as the build
        // left it, and the Android profile is applied as before. The profile turns the prerequisite on
        // for Android, so neither warning says to enable it.
        [Test]
        public void TheReapplyAfterABuild_PutsBackWhatACallbackTurnedOnForTheBuiltPlatform()
        {
            var (standalone, android) = Platforms();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                using (new OpenXROverrideTests.GlobalOptions(false, false))
                {
                    var androidBefore = Snapshot(BuildTargetGroup.Android);
                    var config = new OpenXRProfileConfig();
                    config.EnabledFeatures.Add(typeof(TestPrerequisiteFeature).FullName);
                    config.EnabledFeatures.Add(typeof(TestActivePlatformCallbackFeature).FullName);
                    forge.SetPluginConfig("OpenXR", config);
                    var plugin = new OpenXRPlugin();
                    var pair = new EditorStatePair { PluginTypeName = nameof(OpenXRPlugin), Group = BuildTargetGroup.Android, Plugin = plugin };
                    var entries = new List<EditorStateEntry>();
                    LogAssert.Expect(LogType.Warning, PutBack(BuildTargetGroup.Android, BuildTargetGroup.Standalone, advice: false));
                    EditorStateApplier.Apply(entries, new[] { pair }, forge, _ => plugin, null);
                    EditorStateApplier.SuspendForBuild(entries, _ => plugin, forge, null);
                    // The Standalone build runs here, and puts Standalone back as it found it.
                    var standaloneAfterBuild = Snapshot(BuildTargetGroup.Standalone);
                    LogAssert.Expect(LogType.Warning, PutBack(BuildTargetGroup.Android, BuildTargetGroup.Standalone, advice: false));

                    var result = EditorStateApplier.ResumeAfterBuild(entries, forge, _ => plugin);

                    CollectionAssert.IsEmpty(result.Failures);
                    Assert.AreEqual(standaloneAfterBuild, Snapshot(BuildTargetGroup.Standalone), "Standalone stays as the build left it.");
                    Assert.IsFalse(standalone.GetFeature<TestPrerequisiteFeature>().enabled);
                    Assert.IsTrue(android.GetFeature<TestPrerequisiteFeature>().enabled, "Android has the profile's features on again.");
                    Assert.IsTrue(android.GetFeature<TestActivePlatformCallbackFeature>().enabled, "Android has the profile's features on again.");
                    Assert.AreEqual(EditorStatePhase.Applied, entries.Single().phase);
                    CollectionAssert.IsEmpty(EditorStateApplier.Drift(entries, _ => plugin));

                    EditorStateApplier.RevertAll(entries, _ => plugin);

                    Assert.AreEqual(androidBefore, Snapshot(BuildTargetGroup.Android));
                }
            }
            finally
            {
                Object.DestroyImmediate(forge);
            }
        }

        // A restore writes the on/off state as data, so no feature's callback runs for it, and it
        // writes only its own platform.
        [Test]
        public void ARestore_RunsNoFeatureCallback()
        {
            var (standalone, _) = Platforms();
            var feature = standalone.GetFeature<TestActivePlatformCallbackFeature>();
            var before = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            TestActivePlatformCallbackFeature.CallbackCount = 0;
            feature.enabled = true;
            Assert.AreEqual(1, TestActivePlatformCallbackFeature.CallbackCount);

            OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, before);

            Assert.IsFalse(feature.enabled);
            Assert.AreSame(feature, standalone.GetFeature<TestActivePlatformCallbackFeature>(), "The restore keeps the feature's object.");
            Assert.AreEqual(1, TestActivePlatformCallbackFeature.CallbackCount, "A restore runs no callback.");
        }

        // Merge compares a feature's properties without OpenXRFeature's own on/off field, and only that:
        // a field of the same name in the feature's own settings still counts.
        [Test]
        public void WithoutEnabled_LeavesOutOnlyTheFeaturesOwnOnOffField()
        {
            var feature = ScriptableObject.CreateInstance<TestPrerequisiteFeature>();
            try
            {
                OpenXROverrideTests.SetEnabledAsData(feature, false);
                var original = EditorJsonUtility.ToJson(feature);
                OpenXROverrideTests.SetEnabledAsData(feature, true);
                var turnedOn = EditorJsonUtility.ToJson(feature);
                Assert.AreEqual(OpenXRSettingsState.WithoutEnabled(original), OpenXRSettingsState.WithoutEnabled(turnedOn),
                    "Only OpenXRFeature's own on/off field changed, and it is left out.");
                feature.nested.m_enabled = false;
                var nestedChanged = EditorJsonUtility.ToJson(feature);
                Assert.AreNotEqual(OpenXRSettingsState.WithoutEnabled(turnedOn), OpenXRSettingsState.WithoutEnabled(nestedChanged),
                    "The nested field of the same name still counts.");
            }
            finally { Object.DestroyImmediate(feature); }
        }
    }
}
