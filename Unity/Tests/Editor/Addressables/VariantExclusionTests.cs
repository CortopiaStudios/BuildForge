using System;
using System.Linq;
using BuildForge.Editor.Addressables;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BuildForge.Tests.Addressables
{
    public class VariantExclusionTests
    {
        [Test]
        public void VariantAddsToBaseExclusions_WithoutMutatingSavedConfiguration()
        {
            var config = new AddressablesStripperConfig();
            config.ExcludedGroups.Add("Demo Only");
            config.ExcludedLabels.Add("Spoiler");
            var release = new VariantAddressablesExclusions { variant = "Default" };
            release.groups.AddRange(new[] { "Debug", "Demo Only" });
            release.labels.Add("Development");
            config.VariantExclusions.Add(release);
            var actual = config.ForVariant(null);
            CollectionAssert.AreEqual(new[] { "Demo Only", "Debug" }, actual.ExcludedGroups);
            CollectionAssert.AreEqual(new[] { "Spoiler", "Development" }, actual.ExcludedLabels);
            CollectionAssert.AreEqual(new[] { "Demo Only" }, config.ForVariant("Development").ExcludedGroups);
            actual.ExcludedGroups.Clear();
            CollectionAssert.AreEqual(new[] { "Demo Only" }, config.ExcludedGroups);
            CollectionAssert.AreEqual(new[] { "Debug", "Demo Only" }, release.groups);
        }

        [Test]
        public void ExclusionsForUnconfiguredVariants_AreReported_DefaultIsAlwaysConfigured()
        {
            var config = new AddressablesStripperConfig();
            foreach (var name in new[] { "Default", "Internal", "Demo" })
                config.VariantExclusions.Add(new VariantAddressablesExclusions { variant = name });
            CollectionAssert.AreEqual(new[] { "Demo" },
                config.UnknownVariantExclusions(new[] { "Internal", "PublicDemo" }).Select(v => v.variant));
            CollectionAssert.AreEqual(new[] { "Internal", "Demo" },
                config.UnknownVariantExclusions(Array.Empty<string>()).Select(v => v.variant));
            Assert.IsNull(AddressablesStripperPlugin.UnknownVariantsError(config, new[] { "Internal", "Demo" }));
        }
    }

    /// <summary>
    /// Renaming "Demo" to "PublicDemo" in Project Settings leaves the profile's
    /// exclusions under "Demo", where no build finds them.
    /// </summary>
    public class RenamedVariantExclusionTests
    {
        string previousSettings;
        ForgeProfile profile;
        readonly AddressablesStripperPlugin plugin = new AddressablesStripperPlugin();

        [SetUp] public void SetUp()
        {
            previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var settings = new SerializedObject(ForgeSettings.instance);
            var variants = settings.FindProperty("buildVariants");
            variants.arraySize = 1;
            variants.GetArrayElementAtIndex(0).stringValue = "PublicDemo";
            settings.FindProperty("variantRules").arraySize = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
            profile = ScriptableObject.CreateInstance<ForgeProfile>();
        }

        [TearDown] public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(profile);
            EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
        }

        ForgeBuildContext Context(string variant) =>
            new ForgeBuildContext(null, profile, BuildTarget.StandaloneWindows64, "unused", true, variant);

        [Test]
        public void ExclusionsLeftUnderTheFormerName_StopEveryBuild()
        {
            var config = new AddressablesStripperConfig();
            var former = new VariantAddressablesExclusions { variant = "Demo" };
            former.groups.Add("Spoilers");
            config.VariantExclusions.Add(former);
            profile.SetPluginConfig(AddressablesStripperPlugin.ConfigKey, config);

            foreach (var variant in new[] { "PublicDemo", null })
            {
                StringAssert.Contains("'Demo'", plugin.Validate(profile, variant).Single());
                var failure = Assert.Throws<InvalidOperationException>(() => plugin.OnPreBuild(Context(variant)));
                StringAssert.Contains("'Demo', which is not a configured variant", failure.Message);
            }
        }

        [Test]
        public void ExclusionsForDefaultAndConfiguredVariants_DoNotStopTheBuild()
        {
            var config = new AddressablesStripperConfig();
            config.VariantExclusions.Add(new VariantAddressablesExclusions { variant = "Default" });
            config.VariantExclusions.Add(new VariantAddressablesExclusions { variant = "PublicDemo" });
            profile.SetPluginConfig(AddressablesStripperPlugin.ConfigKey, config);

            CollectionAssert.IsEmpty(plugin.Validate(profile, "PublicDemo"));
            Assert.DoesNotThrow(() => plugin.OnPreBuild(Context("PublicDemo")));
        }
    }
}
