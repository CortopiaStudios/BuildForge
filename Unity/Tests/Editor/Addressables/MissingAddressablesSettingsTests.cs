using System;
using BuildForge.Editor.Addressables;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace BuildForge.Tests.Addressables
{
    /// <summary>
    /// With the Addressables package installed but no Addressables settings, exclusions and
    /// an enabled Addressables Rebuild would do nothing, while Unity's player build still
    /// copies the content built before into the player: such builds stop.
    /// </summary>
    public class MissingAddressablesSettingsTests
    {
        string previousSettings;
        ForgeProfile profile;

        [SetUp] public void SetUp()
        {
            Assume.That(AddressableAssetSettingsDefaultObject.Settings, Is.Null,
                "This host has Addressables settings; the hosts without them run these tests.");
            previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var settings = new SerializedObject(ForgeSettings.instance);
            settings.FindProperty("buildVariants").arraySize = 0;
            settings.FindProperty("variantRules").arraySize = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
            profile = ScriptableObject.CreateInstance<ForgeProfile>();
        }

        [TearDown] public void TearDown()
        {
            if (profile != null)
                UnityEngine.Object.DestroyImmediate(profile);
            if (previousSettings != null)
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
        }

        ForgeBuildContext Context() => new ForgeBuildContext(null, profile, BuildTarget.StandaloneWindows64, "unused", true);

        [Test]
        public void StripperExclusions_StopTheBuild()
        {
            var config = new AddressablesStripperConfig();
            config.ExcludedGroups.Add("Spoilers");
            profile.SetPluginConfig(AddressablesStripperPlugin.ConfigKey, config);
            var stripper = new AddressablesStripperPlugin();

            Assert.IsTrue(stripper.IsApplicable(null), "Not skipped, as it was without settings.");
            CollectionAssert.AreEqual(new[] { AddressablesStripperPlugin.MissingSettingsMessage }, stripper.Validate(profile, null));
            StringAssert.Contains(AddressablesStripperPlugin.MissingSettingsMessage,
                Assert.Throws<InvalidOperationException>(() => stripper.OnPreBuild(Context())).Message);
            Assert.DoesNotThrow(() => stripper.OnPostBuild(Context()));
        }

        [Test]
        public void TheStripperWithoutExclusions_LetsTheBuildRun()
        {
            var stripper = new AddressablesStripperPlugin();

            CollectionAssert.IsEmpty(stripper.Validate(profile, null));
            Assert.DoesNotThrow(() => stripper.OnPreBuild(Context()));
        }

        [Test]
        public void AnEnabledRebuild_StopsTheBuild()
        {
            var rebuild = new AddressablesRebuildPlugin();
            rebuild.SetEnabled(profile, true);

            Assert.IsTrue(rebuild.IsApplicable(null), "Not skipped, as it was without settings.");
            CollectionAssert.AreEqual(new[] { AddressablesRebuildPlugin.MissingSettingsMessage }, rebuild.Validate(profile, null));
            StringAssert.Contains(AddressablesRebuildPlugin.MissingSettingsMessage,
                Assert.Throws<InvalidOperationException>(() => rebuild.OnPreBuild(Context())).Message);
        }
    }
}
