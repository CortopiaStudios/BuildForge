using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Core;
using BuildForge.Editor.OpenXR;
using BuildForge.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace BuildForge.Tests.OpenXR
{
    public class OpenXROverrideTests
    {
        KHRSimpleControllerProfile controller;
        OpenXRProfileConfig config;

        [SetUp] public void SetUp()
        {
            controller = ScriptableObject.CreateInstance<KHRSimpleControllerProfile>();
            config = new OpenXRProfileConfig();
        }
        [TearDown] public void TearDown() => Object.DestroyImmediate(controller);

        [Test]
        public void RequiringGroups_AreTheTickedGroupsThatRequireTheFeature()
        {
            var sets = new[]
            {
                new OpenXRFeatureSetManager.FeatureSet { name = "Meta Quest", isEnabled = true,
                    featureIds = new[] { "quest", "touch" }, requiredFeatureIds = new[] { "quest" } },
                new OpenXRFeatureSetManager.FeatureSet { name = "Meta XR", isEnabled = true,
                    featureIds = new[] { "quest" }, requiredFeatureIds = new[] { "quest" } },
                new OpenXRFeatureSetManager.FeatureSet { name = "Unticked", isEnabled = false,
                    featureIds = new[] { "quest" }, requiredFeatureIds = new[] { "quest" } },
                new OpenXRFeatureSetManager.FeatureSet { name = "Pico", isEnabled = true, featureIds = new[] { "pico" } },
            };
            CollectionAssert.AreEqual(new[] { "Meta Quest", "Meta XR" }, OpenXRPlugin.RequiringGroups("quest", sets));
            CollectionAssert.IsEmpty(OpenXRPlugin.RequiringGroups("touch", sets), "A group member it does not require can be disabled.");
            CollectionAssert.IsEmpty(OpenXRPlugin.RequiringGroups("pico", sets));
        }

        // The build stops on a Disable override that a ticked group blocks, so
        // the window must not promise that the override is merely ignored.
        [Test]
        public void BlockedDisableOverride_SaysTheBuildStops()
        {
            var warning = OpenXRPlugin.FeatureGroupMessage(new[] { "Meta Quest" }, BuildTargetGroup.Android,
                new[] { "'Meta Quest Support' (required by 'Meta Quest')" });
            StringAssert.Contains("Builds of this profile stop on its Disable override for " +
                "'Meta Quest Support' (required by 'Meta Quest'); untick the group or remove the override.", warning);
            StringAssert.DoesNotContain("ignored", warning);

            var error = OpenXRPlugin.RefusedDisableMessage("Meta Quest Support", new[] { "Meta Quest" }, BuildTargetGroup.Android);
            Assert.AreEqual("OpenXR kept 'Meta Quest Support' enabled although this profile disables it: the feature " +
                "group(s) 'Meta Quest', ticked for Android in XR Plug-in Management, require it. Untick the group, " +
                "or remove the Disable override.", error);
        }

        [TestCase(true)] [TestCase(false)]
        public void InteractionOverrideWithoutGlobalOptInExplainsIgnoredSetting(bool enable)
        {
            (enable ? config.EnabledFeatures : config.DisabledFeatures).Add(controller.GetType().FullName);
            var warnings = OpenXRPlugin.OverrideWarnings(config, new[] { controller }, false);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("global OpenXR settings"));
        }

        [Test] public void GlobalOptInAllowsInteractionOverrides()
        {
            config.EnabledFeatures.Add(controller.GetType().FullName);
            Assert.That(OpenXRPlugin.OverrideWarnings(config, new[] { controller }, true), Is.Empty);
        }

        [Test] public void MissingEnabledFeatureIsReported()
        {
            config.EnabledFeatures.Add("Missing.Vendor.Feature");
            Assert.That(OpenXRPlugin.OverrideWarnings(config, new[] { controller }, true)[0], Does.Contain("not installed"));
        }

        [Test] public void ManifestReportsActualFeatureState()
        {
            config.EnabledFeatures.Add(controller.GetType().FullName);
            controller.enabled = false;
            Assert.That(OpenXRPlugin.EffectiveEnabledFeatures(new[] { controller }), Is.Empty);
            controller.enabled = true;
            Assert.That(OpenXRPlugin.EffectiveEnabledFeatures(new[] { controller }), Is.EqualTo(controller.GetType().FullName));
        }

        // Unity's Meta Quest build step copies MetaQuestFeature's symmetric
        // projection into OpenXRSettings, so a settings override of it is lost.
        [Test] public void SettingsOverrideThatAnEnabledFeatureDeclaresIsReported()
        {
            var quest = CreateMetaQuestFeature();
            try
            {
                config.EnabledFeatures.Add(quest.GetType().FullName);
                config.SetSettingOverride("m_symmetricProjection", "1");
                config.SetSettingOverride("m_spacewarpMotionVectorTextureFormat", "0");
                var warnings = OpenXRPlugin.FeatureControlledSettingWarnings(config, new[] { controller, quest }, false);
                Assert.That(warnings, Has.Count.EqualTo(2));
                StringAssert.Contains("'m_symmetricProjection' may not reach the player", warnings[0]);
                StringAssert.Contains("'m_spacewarpMotionVectorTextureFormat' may not reach the player", warnings[1]);
            }
            finally { Object.DestroyImmediate(quest); }
        }

        // A disabled feature's build steps do not run, so on a profile without
        // Meta Quest Support (Steam Frame) the override does reach the player.
        [Test] public void SettingsOverrideThatOnlyADisabledFeatureDeclaresIsNotReported()
        {
            var quest = CreateMetaQuestFeature();
            try
            {
                config.SetSettingOverride("m_symmetricProjection", "0");

                quest.enabled = false;
                Assert.That(OpenXRPlugin.FeatureControlledSettingWarnings(config, new[] { quest }, false), Is.Empty,
                    "Disabled in the settings and not enabled by the profile.");

                quest.enabled = true;
                Assert.That(OpenXRPlugin.FeatureControlledSettingWarnings(config, new[] { quest }, false), Has.Count.EqualTo(1),
                    "Enabled in the settings and left alone by the profile.");

                config.DisabledFeatures.Add(quest.GetType().FullName);
                Assert.That(OpenXRPlugin.FeatureControlledSettingWarnings(config, new[] { quest }, false), Is.Empty,
                    "Disabled by the profile.");
            }
            finally { Object.DestroyImmediate(quest); }
        }

        [Test] public void SettingsOverrideNoFeatureDeclaresIsNotReported()
        {
            config.SetSettingOverride("m_renderMode", "1");
            Assert.That(OpenXRPlugin.FeatureControlledSettingWarnings(config, new[] { controller }, false), Is.Empty);
        }

        // Interaction profiles follow the profile's overrides only when Build
        // Forge manages them (Interaction Profiles in the global OpenXR settings).
        [Test] public void FeaturesEnabledInBuildFollowManagedOverridesOnly()
        {
            controller.enabled = true;
            config.DisabledFeatures.Add(controller.GetType().FullName);
            CollectionAssert.AreEqual(new[] { controller },
                OpenXRPlugin.FeaturesEnabledInBuild(config, new[] { controller }, false).ToArray());
            CollectionAssert.IsEmpty(OpenXRPlugin.FeaturesEnabledInBuild(config, new[] { controller }, true).ToArray());

            controller.enabled = false;
            config.DisabledFeatures.Clear();
            config.EnabledFeatures.Add(controller.GetType().FullName);
            CollectionAssert.AreEqual(new[] { controller },
                OpenXRPlugin.FeaturesEnabledInBuild(config, new[] { controller }, true).ToArray());
        }

        static OpenXRFeature CreateMetaQuestFeature()
            => (OpenXRFeature)ScriptableObject.CreateInstance(
                TypeCache.GetTypesDerivedFrom<OpenXRFeature>().First(t => t.Name == "MetaQuestFeature"));

        // OpenXR's Conformance Automation, which the package marks hidden.
        static OpenXRFeature CreateHiddenFeature()
            => (OpenXRFeature)ScriptableObject.CreateInstance(
                TypeCache.GetTypesDerivedFrom<OpenXRFeature>().First(t => t.Name == "ConformanceAutomationFeature"));

        [Test] public void ManifestListsPinsOfEnabledFeatures()
        {
            config.EnabledFeatures.Add("Vendor.Enabled");
            config.SetFeatureOverrides("Vendor.Enabled", new List<PropertyValueEntry>
            {
                new PropertyValueEntry { path = "m_symmetricProjection", value = "1" },
                new PropertyValueEntry { path = "targetDevices.Array.data[0].enabled", value = "0" },
            });
            config.SetFeatureOverrides("Vendor.NotEnabled", new List<PropertyValueEntry>
            {
                new PropertyValueEntry { path = "m_value", value = "1" },
            });
            var section = new ManifestSection("OpenXR");

            OpenXRPlugin.AddFeaturePins(section, config);

            Assert.That(section.Entries, Has.Count.EqualTo(2));
            Assert.AreEqual("1", section.Get("Vendor.Enabled.m_symmetricProjection"));
            Assert.AreEqual("0", section.Get("Vendor.Enabled.targetDevices.Array.data[0].enabled"));
        }

        // OpenXR 1.18 turns the Meta controller profiles and Meta Quest Support
        // off for Android on the first editor load without a Meta Quest Build
        // Profile, when it had enabled them for one.
        [Test] public void ProviderWarning_OnlyWhenTheProfileOverridesSomething()
        {
            Assert.IsNull(OpenXRPlugin.ProviderWarning(config, false, BuildTargetGroup.Standalone), "Nothing configured, nothing lost.");

            config.DisabledFeatures.Add(typeof(KHRSimpleControllerProfile).FullName);
            StringAssert.Contains("not enabled as a plug-in provider in XR Plug-in Management for Standalone",
                OpenXRPlugin.ProviderWarning(config, false, BuildTargetGroup.Standalone));
            Assert.IsNull(OpenXRPlugin.ProviderWarning(config, true, BuildTargetGroup.Standalone), "OpenXR is a provider.");

            var settingOnly = new OpenXRProfileConfig();
            settingOnly.SetSettingOverride("m_renderMode", "0");
            Assert.IsNotNull(OpenXRPlugin.ProviderWarning(settingOnly, false, BuildTargetGroup.Android));
        }

        [Test] public void MetaQuestUtilityWarning_OnlyWhenOpenXRWillTurnTheFeaturesOff()
        {
            StringAssert.Contains("BuildProfileUtilityOpenXR.asset",
                OpenXRPlugin.MetaQuestUtilityWarning(BuildTargetGroup.Android, false, true));
            Assert.IsNull(OpenXRPlugin.MetaQuestUtilityWarning(BuildTargetGroup.Android, true, true), "A Meta Quest profile keeps them on.");
            Assert.IsNull(OpenXRPlugin.MetaQuestUtilityWarning(BuildTargetGroup.Android, false, false), "Nothing to turn off.");
            Assert.IsNull(OpenXRPlugin.MetaQuestUtilityWarning(BuildTargetGroup.Android, false, null), "This OpenXR version has no such step.");
            Assert.IsNull(OpenXRPlugin.MetaQuestUtilityWarning(BuildTargetGroup.Standalone, false, true), "It only touches Android features.");
        }

        [Test] public void MetaQuestUtilityInitialized_ReadsTheFlagWhereOpenXRHasIt()
        {
            var utility = System.Type.GetType("UnityEditor.XR.OpenXR.BuildProfileUtilityOpenXR, Unity.XR.OpenXR.Editor");
            var flag = OpenXRPlugin.MetaQuestUtilityInitialized();
            if (utility == null)
                Assert.IsNull(flag, "OpenXR before 1.18 has no such step.");
            else
                Assert.IsNotNull(flag, "OpenXR has the utility, but its flag or disable step changed; update the reflection.");
        }

        // OpenXR 1.18 sets up a Meta Quest profile's features only in an editor
        // session, so a batch build has to do it; once OpenXR did its part, the
        // settings are the user's.
        [Test] public void MetaQuestSetupFeatures_OnlyWhatTheEditorWouldStillEnable()
        {
            var touch = ScriptableObject.CreateInstance<OculusTouchControllerProfile>();
            var quest = CreateMetaQuestFeature();
            try
            {
                SetFeatureId(touch, "com.unity.openxr.feature.input.oculustouch");
                SetFeatureId(quest, "com.unity.openxr.feature.metaquest");
                SetFeatureId(controller, "com.unity.openxr.feature.input.khrsimpleprofile");
                touch.enabled = false;
                quest.enabled = true;
                controller.enabled = false;
                var ids = new[] { "com.unity.openxr.feature.input.oculustouch", "com.unity.openxr.feature.metaquest" };
                var features = new OpenXRFeature[] { touch, quest, controller };

                CollectionAssert.AreEqual(new[] { touch },
                    OpenXRPlugin.MetaQuestSetupFeatures(BuildTargetGroup.Android, true, false, ids, features),
                    "The listed features that are off.");
                CollectionAssert.IsEmpty(OpenXRPlugin.MetaQuestSetupFeatures(BuildTargetGroup.Android, true, true, ids, features),
                    "OpenXR already set them up.");
                CollectionAssert.IsEmpty(OpenXRPlugin.MetaQuestSetupFeatures(BuildTargetGroup.Android, false, false, ids, features),
                    "Not a Meta Quest profile.");
                CollectionAssert.IsEmpty(OpenXRPlugin.MetaQuestSetupFeatures(BuildTargetGroup.Android, true, null, null, features),
                    "This OpenXR version has no such step.");
                CollectionAssert.IsEmpty(OpenXRPlugin.MetaQuestSetupFeatures(BuildTargetGroup.Standalone, true, false, ids, features),
                    "It only touches Android features.");
            }
            finally
            {
                Object.DestroyImmediate(touch);
                Object.DestroyImmediate(quest);
            }
        }

        [Test] public void MetaQuestUtilityFeatureIds_ReadsTheListWhereOpenXRHasIt()
        {
            var utility = System.Type.GetType("UnityEditor.XR.OpenXR.BuildProfileUtilityOpenXR, Unity.XR.OpenXR.Editor");
            var ids = OpenXRPlugin.MetaQuestUtilityFeatureIds();
            if (utility == null)
                Assert.IsNull(ids, "OpenXR before 1.18 has no such step.");
            else
                CollectionAssert.Contains(ids, "com.unity.openxr.feature.metaquest",
                    "OpenXR has the utility, but its feature list changed; update the reflection.");
        }

        static void SetFeatureId(OpenXRFeature feature, string id)
        {
            var so = new SerializedObject(feature);
            so.FindProperty("featureIdInternal").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test] public void InteractionApplyAndRestorePreserveSavedState()
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(settings, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var installed = settings.GetFeature<KHRSimpleControllerProfile>();
            Assert.That(installed, Is.Not.Null);
            bool original = installed.enabled;
            var snapshot = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            try
            {
                (original ? config.DisabledFeatures : config.EnabledFeatures).Add(installed.GetType().FullName);
                OpenXRSettingsState.Apply(config, BuildTargetGroup.Standalone, true);
                Assert.That(installed.enabled, Is.EqualTo(!original));
            }
            finally { OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, snapshot); }
            Assert.That(installed.enabled, Is.EqualTo(original));
        }

        // Only Listed Features: listed visible features on, every other visible
        // feature off, hidden features (here OpenXR's own Conformance Automation)
        // left to their packages, even when listed.
        [Test] public void OnlyListedFeatures_ListedOn_OthersOff_HiddenLeftAlone()
        {
            var quest = CreateMetaQuestFeature();
            var hidden = CreateHiddenFeature();
            try
            {
                Assert.IsTrue(OpenXRSettingsState.IsHidden(hidden), "OpenXR marks Conformance Automation hidden.");
                Assert.IsFalse(OpenXRSettingsState.IsHidden(quest));
                config.OnlyListedFeatures = true;
                config.EnabledFeatures.Add(quest.GetType().FullName);
                config.DisabledFeatures.Add(quest.GetType().FullName);

                Assert.AreEqual(true, OpenXRSettingsState.Override(config, quest), "Listed; Disabled Features is not used.");
                Assert.IsNull(OpenXRSettingsState.Override(config, hidden));
                Assert.AreEqual(false, OpenXRSettingsState.ManagedOverride(config, controller, true));
                Assert.IsNull(OpenXRSettingsState.ManagedOverride(config, controller, false), "Interaction profiles follow the global option.");
                config.EnabledFeatures.Add(hidden.GetType().FullName);
                Assert.IsNull(OpenXRSettingsState.Override(config, hidden), "A listed hidden feature is still left alone.");

                quest.enabled = false;
                hidden.enabled = true;
                controller.enabled = true;
                CollectionAssert.AreEquivalent(new OpenXRFeature[] { quest, hidden, controller },
                    OpenXRPlugin.FeaturesEnabledInBuild(config, new OpenXRFeature[] { quest, hidden, controller }, false).ToArray());
                CollectionAssert.AreEquivalent(new OpenXRFeature[] { quest, hidden },
                    OpenXRPlugin.FeaturesEnabledInBuild(config, new OpenXRFeature[] { quest, hidden, controller }, true).ToArray());
            }
            finally
            {
                Object.DestroyImmediate(quest);
                Object.DestroyImmediate(hidden);
            }
        }

        [Test] public void OverridesOnHiddenFeatures_AreReported()
        {
            var hidden = CreateHiddenFeature();
            try
            {
                var features = new OpenXRFeature[] { hidden, controller };
                Assert.That(OpenXRPlugin.HiddenFeatureWarnings(config, features), Is.Empty);
                config.DisabledFeatures.Add(hidden.GetType().FullName);
                var warning = OpenXRPlugin.HiddenFeatureWarnings(config, features).Single();
                StringAssert.Contains("is hidden", warning);
                StringAssert.Contains("Disable override", warning);
                Assert.That(OpenXRPlugin.OverrideWarnings(config, features, true), Has.Member(warning), "The build window shows it.");

                config.OnlyListedFeatures = true;
                Assert.That(OpenXRPlugin.HiddenFeatureWarnings(config, features), Is.Empty, "Unlisted, so left alone as it should be.");
                config.EnabledFeatures.Add(hidden.GetType().FullName);
                StringAssert.Contains("leaves it to its package", OpenXRPlugin.HiddenFeatureWarnings(config, features).Single());
            }
            finally { Object.DestroyImmediate(hidden); }
        }

        [Test] public void NotListedFeatures_NamesTheVisibleFeaturesTurnedOff()
        {
            var touch = ScriptableObject.CreateInstance<OculusTouchControllerProfile>();
            var hidden = CreateHiddenFeature();
            try
            {
                var features = new OpenXRFeature[] { controller, touch, hidden };
                CollectionAssert.IsEmpty(OpenXRPlugin.NotListedFeatures(config, features, true), "Only in the allow-list mode.");
                config.OnlyListedFeatures = true;
                config.EnabledFeatures.Add(touch.GetType().FullName);
                var notListed = OpenXRPlugin.NotListedFeatures(config, features, true);
                Assert.That(notListed, Has.Count.EqualTo(1), "The hidden feature is left alone, so it is not listed as off.");
                CollectionAssert.IsEmpty(OpenXRPlugin.NotListedFeatures(config, features, false), "Unmanaged interaction profiles stay as they are.");
            }
            finally
            {
                Object.DestroyImmediate(touch);
                Object.DestroyImmediate(hidden);
            }
        }

        // Switching modes keeps what builds get: the visible features on now are
        // listed; switching back gives every unlisted visible feature a Disable.
        [Test] public void SwitchingOnlyListedFeatures_KeepsWhatBuildsGet()
        {
            var touch = ScriptableObject.CreateInstance<OculusTouchControllerProfile>();
            var quest = CreateMetaQuestFeature();
            var hidden = CreateHiddenFeature();
            try
            {
                var managed = new OpenXRFeature[] { controller, touch, quest, hidden };
                var questName = quest.GetType().FullName;
                config.EnabledFeatures.Add(questName);
                config.SetFeatureOverrides(questName, new List<PropertyValueEntry> { new PropertyValueEntry { path = "m_value", value = "1" } });
                config.DisabledFeatures.Add(controller.GetType().FullName);
                config.DisabledFeatures.Add(hidden.GetType().FullName);
                config.EnabledFeatures.Add("Not.Installed.Here");
                controller.enabled = true;
                touch.enabled = true;   // No Override, on in the settings
                quest.enabled = false;

                OpenXRPlugin.SetOnlyListedFeatures(config, managed, true);
                Assert.IsTrue(config.OnlyListedFeatures);
                CollectionAssert.AreEquivalent(new[] { "Not.Installed.Here", questName, touch.GetType().FullName }, config.EnabledFeatures);
                CollectionAssert.IsEmpty(config.DisabledFeatures);
                Assert.IsNotNull(config.GetFeatureOverrides(questName), "A listed feature keeps its pins.");

                OpenXRPlugin.SetOnlyListedFeatures(config, managed, false);
                Assert.IsFalse(config.OnlyListedFeatures);
                CollectionAssert.AreEquivalent(new[] { controller.GetType().FullName }, config.DisabledFeatures,
                    "The unlisted visible feature; the hidden one stays with its package.");
            }
            finally
            {
                Object.DestroyImmediate(touch);
                Object.DestroyImmediate(quest);
                Object.DestroyImmediate(hidden);
            }
        }

        [Test] public void OnlyListedFeatures_GroupConflictsAndRefusals_SayToListTheFeature()
        {
            StringAssert.Contains("stop on turning off 'Meta Quest Support' (required by 'Meta Quest'), which it doesn't list; " +
                "untick the group or list the feature.", OpenXRPlugin.FeatureGroupMessage(new[] { "Meta Quest" },
                BuildTargetGroup.Android, new[] { "'Meta Quest Support' (required by 'Meta Quest')" }, true));
            Assert.AreEqual("OpenXR kept 'Meta Quest Support' enabled although this profile doesn't list it: the feature " +
                "group(s) 'Meta Quest', ticked for Android in XR Plug-in Management, require it. Untick the group, or list the feature.",
                OpenXRPlugin.RefusedDisableMessage("Meta Quest Support", new[] { "Meta Quest" }, BuildTargetGroup.Android, true));
        }

        // Real Standalone settings: Apply turns every unlisted visible feature off
        // and leaves the hidden ones; Restore brings everything back.
        [Test] public void OnlyListedFeatures_ApplyAndRestore()
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(settings, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var all = settings.GetFeatures<OpenXRFeature>().Where(f => f != null).ToList();
            var listed = settings.GetFeature<KHRSimpleControllerProfile>();
            Assert.That(listed, Is.Not.Null);
            Assert.That(all.Any(OpenXRSettingsState.IsHidden), "The test host's Standalone settings must contain a hidden feature.");
            var before = all.ToDictionary(f => f, f => f.enabled);
            var snapshot = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            try
            {
                foreach (var feature in all.Where(OpenXRSettingsState.IsHidden))
                    feature.enabled = !before[feature]; // Flipped, so "left alone" is not just "off already".
                var hiddenStates = all.Where(OpenXRSettingsState.IsHidden).ToDictionary(f => f, f => f.enabled);
                config.OnlyListedFeatures = true;
                config.EnabledFeatures.Add(listed.GetType().FullName);
                OpenXRSettingsState.Apply(config, BuildTargetGroup.Standalone, true);
                foreach (var feature in all)
                {
                    if (OpenXRSettingsState.IsHidden(feature))
                        Assert.AreEqual(hiddenStates[feature], feature.enabled, feature.name);
                    else
                        Assert.AreEqual(feature == listed, feature.enabled, feature.name);
                }
            }
            finally { OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, snapshot); }
            foreach (var feature in all)
                Assert.AreEqual(before[feature], feature.enabled, feature.name);
        }

        [Test] public void MirrorFeatureStates_CopiesWhatStandaloneShares_RestoreBringsItBack()
        {
            var android = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(android, Is.Not.Null, "The OpenXR test host must contain Android settings.");
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var source = android.GetFeature<KHRSimpleControllerProfile>();
            var target = standalone.GetFeature<KHRSimpleControllerProfile>();
            Assert.That(source, Is.Not.Null);
            Assert.That(target, Is.Not.Null);
            bool targetOriginal = target.enabled;
            var androidSnapshot = OpenXRSettingsState.Capture(BuildTargetGroup.Android, true);
            var standaloneSnapshot = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            try
            {
                source.enabled = !targetOriginal;
                OpenXRSettingsState.MirrorFeatureStates(BuildTargetGroup.Android, BuildTargetGroup.Standalone);
                Assert.That(target.enabled, Is.EqualTo(!targetOriginal));
            }
            finally
            {
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, standaloneSnapshot);
                OpenXRSettingsState.Restore(BuildTargetGroup.Android, androidSnapshot);
            }
            Assert.That(target.enabled, Is.EqualTo(targetOriginal));
        }
    }
}
