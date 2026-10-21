using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.OpenXR;
using BuildForge.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.TestTools;
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
            StringAssert.Contains("Builds and Apply of this profile stop on its Disable override for " +
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

        // What the Feature Overrides dropdown does for Enable and then No Override.
        [Test] public void FeatureEnabledAndBackToNoOverride_LeavesNoProfileSettings()
        {
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var typeName = controller.GetType().FullName;
                var stored = profile.GetPluginConfig<OpenXRProfileConfig>("OpenXR");
                stored.EnabledFeatures.Add(typeName);
                stored.SetFeatureOverrides(typeName, new List<PropertyValueEntry> { new PropertyValueEntry { path = "m_Enabled", value = "1" } });
                profile.SetPluginConfig("OpenXR", stored);
                Assert.AreSame(stored, profile.GetPluginConfig<OpenXRProfileConfig>("OpenXR"));

                stored.EnabledFeatures.Remove(typeName);
                stored.SetFeatureOverrides(typeName, null);
                profile.SetPluginConfig("OpenXR", stored);
                Assert.AreNotSame(stored, profile.GetPluginConfig<OpenXRProfileConfig>("OpenXR"),
                    "Back at No Override, the profile stores no OpenXR settings.");
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        // Overrides and pins that OpenXR can't take, as after an update renamed a setting or a feature:
        // builds and Apply stop on them, and each can be removed.
        [Test] public void StaleEntries_NameWhatOpenXRCannotTake_AndRemoveThem()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var installed = standalone.GetFeature<KHRSimpleControllerProfile>();
            Assert.That(installed, Is.Not.Null);
            var boolSetting = FirstVisibleSetting(standalone, SerializedPropertyType.Boolean);
            var installedType = installed.GetType().FullName;

            config.SetSettingOverride("m_noSuchSetting", "1");
            config.SetSettingOverride(boolSetting, "yes");
            config.EnabledFeatures.Add("Missing.Vendor.Feature");
            config.DisabledFeatures.Add("Missing.Vendor.Other");
            config.SetFeatureOverrides("Missing.Vendor.Feature", new List<PropertyValueEntry> { new PropertyValueEntry { path = "m_x", value = "1" } });
            config.EnabledFeatures.Add(installedType);
            config.SetFeatureOverrides(installedType, new List<PropertyValueEntry> { new PropertyValueEntry { path = "m_noSuchField", value = "1" } });

            var stale = OpenXRSettingsState.StaleEntries(config, standalone, true);
            var text = string.Join("; ", stale.Select(e => e.Description));
            StringAssert.Contains("Setting 'm_noSuchSetting': OpenXR has no such setting", text);
            StringAssert.Contains($"Setting '{boolSetting}': 'yes' is not a valid Boolean value", text);
            StringAssert.Contains("Feature 'Missing.Vendor.Feature' (Enable): not installed", text);
            StringAssert.Contains("Feature 'Missing.Vendor.Other' (Disable): not installed", text);
            StringAssert.Contains("Settings of feature 'Missing.Vendor.Feature': not installed", text);
            StringAssert.Contains("Setting 'm_noSuchField' of feature", text);
            Assert.AreEqual(6, stale.Count, text);
            StringAssert.Contains("Builds and Apply of this profile stop", OpenXRPlugin.StaleEntriesError(stale, BuildTargetGroup.Standalone));

            foreach (var entry in stale)
                entry.Remove(config);
            CollectionAssert.IsEmpty(OpenXRSettingsState.StaleEntries(config, standalone, true));
            Assert.IsNull(config.GetSettingOverride(boolSetting));
            CollectionAssert.AreEqual(new[] { installedType }, config.EnabledFeatures, "The installed feature's override stays.");
            Assert.IsNull(config.GetFeatureOverrides(installedType), "Its only pin was stale.");
        }

        [Test] public void StaleEntries_LeaveValidOverridesAlone()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var installed = standalone.GetFeature<KHRSimpleControllerProfile>();
            var boolSetting = FirstVisibleSetting(standalone, SerializedPropertyType.Boolean);
            config.SetSettingOverride(boolSetting, "1");
            config.EnabledFeatures.Add(installed.GetType().FullName);
            config.SetFeatureOverrides(installed.GetType().FullName,
                SerializedPropertySnapshot.Capture(new SerializedObject(installed), OpenXRSettingsState.SkipFeatureProperties));
            config.OnlyListedFeatures = true;
            config.DisabledFeatures.Add("Missing.Vendor.Unused");

            CollectionAssert.IsEmpty(OpenXRSettingsState.StaleEntries(config, standalone, true).Select(e => e.Description),
                "Only Listed Features doesn't use Disabled Features.");
        }

        // A build and an Apply stop before they change anything.
        [Test] public void StaleEntries_StopTheBuildAndApply()
        {
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                config.SetSettingOverride("m_noSuchSetting", "1");
                forge.SetPluginConfig("OpenXR", config);
                var plugin = new OpenXRPlugin();
                var before = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true).ToJson();

                var build = Assert.Throws<System.InvalidOperationException>(() => plugin.OnPreBuild(
                    new ForgeBuildContext(null, forge, BuildTarget.StandaloneWindows64, "unused", true)));
                StringAssert.Contains("m_noSuchSetting", build.Message);
                var apply = Assert.Throws<System.InvalidOperationException>(() => plugin.ApplyToEditor(forge, BuildTargetGroup.Standalone));
                StringAssert.Contains("m_noSuchSetting", apply.Message);
                Assert.AreEqual(before, OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true).ToJson());
            }
            finally { Object.DestroyImmediate(forge); }
        }

        static string FirstVisibleSetting(OpenXRSettings settings, SerializedPropertyType type)
        {
            var iterator = new SerializedObject(settings).GetIterator();
            iterator.Next(true);
            do
            {
                if (iterator.depth == 0 && iterator.propertyType == type
                    && !OpenXRSettingsState.SkipSettingsProperties.Contains(iterator.name))
                    return iterator.name;
            } while (iterator.NextVisible(false));
            Assert.Fail($"OpenXR's settings have no visible {type} setting.");
            return null;
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

        // OpenXR Features: Turn on the platform's (the default). A feature on for
        // the applied platform comes on for Play Mode; one that Standalone has on
        // itself, such as hand tracking for a tool that runs in Play Mode, stays
        // on although the platform has it off. Match the platform's turns it off.
        [TestCase(false)] [TestCase(true)]
        public void CopyFeatureStates_TurnsOnWhatThePlatformHasOn_AndOffOnlyWhenMatching_RestoreBringsItBack(bool match)
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
                source.enabled = true;
                target.enabled = false;
                OpenXRSettingsState.CopyFeatureStates(BuildTargetGroup.Android, BuildTargetGroup.Standalone, match);
                Assert.IsTrue(target.enabled);

                source.enabled = false;
                OpenXRSettingsState.CopyFeatureStates(BuildTargetGroup.Android, BuildTargetGroup.Standalone, match);
                Assert.That(target.enabled, Is.EqualTo(!match), match ? "Matching turns it off." : "Standalone keeps a feature it has on.");
            }
            finally
            {
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, standaloneSnapshot);
                OpenXRSettingsState.Restore(BuildTargetGroup.Android, androidSnapshot);
            }
            Assert.That(target.enabled, Is.EqualTo(targetOriginal));
        }

        // Meta Quest Setup is off by default. A Meta Quest profile whose builds would lack
        // a feature that OpenXR turns on in the editor gets a warning while the step's flag
        // says it hasn't run; the setup, an Enable override or the flag ends it.
        [Test] public void MetaQuestSetupWarning_NamesWhatCIBuildsWouldLack_UntilTheSetupIsOn()
        {
            var quest = CreateMetaQuestFeature();
            try
            {
                SetFeatureId(quest, "com.unity.openxr.feature.metaquest");
                var ids = new[] { "com.unity.openxr.feature.metaquest" };
                var features = new OpenXRFeature[] { quest };
                quest.enabled = false;
                Assert.IsFalse(config.MetaQuestSetup, "Off by default.");
                StringAssert.Contains("so this profile's CI builds lack them. Turn on Meta Quest Setup",
                    OpenXRPlugin.MetaQuestSetupWarning(true, config, false, ids, features, false));
                Assert.IsNull(OpenXRPlugin.MetaQuestSetupWarning(false, config, false, ids, features, false), "Not a Meta Quest profile.");
                Assert.IsNull(OpenXRPlugin.MetaQuestSetupWarning(true, config, true, ids, features, false), "OpenXR's step has run.");
                Assert.IsNull(OpenXRPlugin.MetaQuestSetupWarning(true, config, null, ids, features, false), "OpenXR without the step.");

                config.EnabledFeatures.Add(quest.GetType().FullName);
                Assert.IsNull(OpenXRPlugin.MetaQuestSetupWarning(true, config, false, ids, features, false), "On in the build anyway.");
                config.EnabledFeatures.Clear();

                Assert.IsFalse(config.HasOverrides);
                config.MetaQuestSetup = true;
                Assert.IsNull(OpenXRPlugin.MetaQuestSetupWarning(true, config, false, ids, features, false));
                Assert.IsTrue(config.HasOverrides, "Like an override, the setup needs OpenXR as a provider.");
            }
            finally { Object.DestroyImmediate(quest); }
        }

        [Test] public void IsMetaQuestProfile_ReadsThePlatformIdTheProfileStores()
        {
            var create = typeof(BuildProfile).GetMethod("CreateInstance", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(BuildTarget), typeof(StandaloneBuildSubtarget) }, null);
            Assert.IsNotNull(create, "Unity's native profile factory changed.");
            var profile = (BuildProfile)create.Invoke(null, new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
            try
            {
                Assert.AreEqual(false, OpenXRPlugin.IsMetaQuestProfile(profile));
                var so = new SerializedObject(profile);
                so.FindProperty("m_PlatformId").stringValue = "80657fe557de4d17822398b3a01b8c9e";
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(true, OpenXRPlugin.IsMetaQuestProfile(profile));
            }
            finally { Object.DestroyImmediate(profile); }
        }

        // OpenXR's public type: a release that renames it fails to compile instead of letting
        // a build treat controller profiles like other features and turn unlisted ones off.
        [Test] public void InteractionProfiles_AreOpenXRsInteractionFeatures()
        {
            var touch = ScriptableObject.CreateInstance<OculusTouchControllerProfile>();
            var quest = CreateMetaQuestFeature();
            try
            {
                Assert.IsTrue(OpenXRSettingsState.IsInteractionProfile(touch));
                Assert.IsTrue(OpenXRSettingsState.IsInteractionProfile(controller));
                Assert.IsFalse(OpenXRSettingsState.IsInteractionProfile(quest));
            }
            finally
            {
                Object.DestroyImmediate(touch);
                Object.DestroyImmediate(quest);
            }
        }

        // OpenXR before 1.18 has no Meta Quest setup to read; from 1.18, or with no known version,
        // a part Build Forge cannot read is reported instead of taken for a version without it.
        [Test] public void MetaQuestUtilityUnreadable_TellsAnOpenXRWithoutTheSetupFromOneBuildForgeCannotRead()
        {
            var features = new OpenXRFeature[] { controller };
            var ids = new[] { "com.unity.openxr.feature.metaquest" };
            Assert.IsNull(OpenXRPlugin.MetaQuestUtilityUnreadable("1.16.1", null, null, features), "OpenXR 1.16 has no such step.");
            Assert.IsNull(OpenXRPlugin.MetaQuestUtilityUnreadable("1.18.0", false, ids, features));
            StringAssert.Contains("isMetaQuestInitialized", OpenXRPlugin.MetaQuestUtilityUnreadable("1.18.0", null, ids, features));
            StringAssert.Contains("metaFeatureIds", OpenXRPlugin.MetaQuestUtilityUnreadable("1.19.0-pre.1", true, null, features));
            StringAssert.Contains("isMetaQuestInitialized", OpenXRPlugin.MetaQuestUtilityUnreadable(null, null, ids, features),
                "An unknown version is not taken for an old one.");
        }

        // Fails on an OpenXR version whose Meta Quest setup Build Forge can no longer read, before
        // a build of a profile with Meta Quest Setup on would stop for it.
        [Test] public void MetaQuestUtility_IsReadableInTheInstalledOpenXR()
        {
            var features = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android)?.GetFeatures<OpenXRFeature>()
                           ?? new OpenXRFeature[0];
            Assert.IsNull(OpenXRPlugin.MetaQuestUtilityUnreadable(OpenXRPlugin.OpenXRVersion(),
                OpenXRPlugin.MetaQuestUtilityInitialized(), OpenXRPlugin.MetaQuestUtilityFeatureIds(), features));
        }

        static OpenXREditorStateSnapshot Snapshot(string renderMode, string latency, params OpenXRFeatureState[] features)
        {
            var snapshot = new OpenXREditorStateSnapshot();
            snapshot.settings.Add(new SettingsOverride { propertyName = "m_renderMode", value = renderMode });
            snapshot.settings.Add(new SettingsOverride { propertyName = "m_latencyOptimization", value = latency });
            snapshot.features.AddRange(features);
            return snapshot;
        }

        static OpenXRFeatureState Feature(string name, bool enabled, string json = "{}")
            => new OpenXRFeatureState { featureTypeName = name, enabled = enabled, json = json };

        // Undoing an Apply after a git pull: what the Apply wrote goes back to the
        // baseline, what changed after it stays.
        [Test] public void Merge_UndoesTheApply_AndKeepsChangesMadeAfterIt()
        {
            var baseline = Snapshot("0", "0", Feature("Hands", false, "{\"a\":1}"), Feature("Touch", false)).ToJson();
            var expected = Snapshot("1", "0", Feature("Hands", true, "{\"a\":1}"), Feature("Touch", false)).ToJson();
            var current = Snapshot("1", "1", Feature("Hands", true, "{\"a\":2}"), Feature("Touch", true)).ToJson();

            var merged = OpenXREditorStateSnapshot.FromJson(OpenXRSettingsState.Merge(baseline, expected, current));

            Assert.AreEqual("0", merged.settings.Single(s => s.propertyName == "m_renderMode").value, "The Apply's setting is undone.");
            Assert.AreEqual("1", merged.settings.Single(s => s.propertyName == "m_latencyOptimization").value, "The pulled setting stays.");
            var hands = merged.features.Single(f => f.featureTypeName == "Hands");
            Assert.IsFalse(hands.enabled, "The feature the Apply turned on goes off again.");
            Assert.AreEqual("{\"a\":2}", hands.json, "Its pulled properties stay.");
            Assert.IsTrue(merged.features.Single(f => f.featureTypeName == "Touch").enabled, "The feature the pull turned on stays on.");
        }

        // A feature's JSON holds its on/off state too. Turning it off after the Apply is a
        // change to that state alone: the properties the Apply wrote are undone with it.
        [Test] public void Merge_FeatureTurnedOffAfterTheApply_GetsTheBaselinesPropertiesBack()
        {
            var baseline = Snapshot("0", "0", Feature("Hands", false, "{\"m_enabled\":false,\"p\":\"Y\"}")).ToJson();
            var expected = Snapshot("0", "0", Feature("Hands", true, "{\"m_enabled\":true,\"p\":\"X\"}")).ToJson();
            var current = Snapshot("0", "0", Feature("Hands", false, "{\"m_enabled\":false,\"p\":\"X\"}")).ToJson();

            var hands = OpenXREditorStateSnapshot.FromJson(OpenXRSettingsState.Merge(baseline, expected, current)).features.Single();

            Assert.IsFalse(hands.enabled);
            Assert.AreEqual("{\"m_enabled\":false,\"p\":\"Y\"}", hands.json);
        }

        // Unity writes the on/off field as true or false, which Merge leaves out of the comparison.
        [Test] public void WithoutEnabled_LeavesOutTheOnOffStateOfARealFeature()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var feature = standalone.GetFeature<KHRSimpleControllerProfile>();
            var original = new SerializedObject(feature).FindProperty(OpenXRSettingsState.EnabledField).boolValue;
            string JsonWith(bool enabled)
            {
                var so = new SerializedObject(feature);
                so.FindProperty(OpenXRSettingsState.EnabledField).boolValue = enabled;
                so.ApplyModifiedPropertiesWithoutUndo();
                return EditorJsonUtility.ToJson(feature);
            }
            try
            {
                var on = JsonWith(true);
                var off = JsonWith(false);

                Assert.AreNotEqual(on, off);
                Assert.AreEqual(OpenXRSettingsState.WithoutEnabled(on), OpenXRSettingsState.WithoutEnabled(off));
            }
            finally
            {
                JsonWith(original);
            }
        }

        // A snapshot leaves out interaction profiles while Build Forge doesn't manage
        // them, and a feature can come or go with a package.
        [Test] public void Merge_TakesTheBaselineForWhatTheCurrentSnapshotLeavesOut()
        {
            var baseline = Snapshot("0", "0", Feature("Hands", false), Feature("Touch", false)).ToJson();
            var expected = Snapshot("0", "0", Feature("Hands", true), Feature("Touch", true)).ToJson();
            var current = Snapshot("0", "0", Feature("Hands", true), Feature("New", true)).ToJson();

            var merged = OpenXREditorStateSnapshot.FromJson(OpenXRSettingsState.Merge(baseline, expected, current));

            CollectionAssert.AreEquivalent(new[] { "Hands", "New", "Touch" }, merged.features.Select(f => f.featureTypeName));
            Assert.IsFalse(merged.features.Single(f => f.featureTypeName == "Touch").enabled, "Not in the current snapshot: the baseline's.");
            Assert.IsFalse(merged.features.Single(f => f.featureTypeName == "Hands").enabled);
            Assert.IsTrue(merged.features.Single(f => f.featureTypeName == "New").enabled, "Only in the current snapshot: kept.");
            Assert.IsNull(OpenXRSettingsState.Merge(baseline, expected, "not json"));
        }

        // Hidden settings are in the snapshot too, so a build puts back what it wrote there, such as
        // the multiview render regions mode Unity's Meta Quest build step copies from MetaQuestFeature;
        // values OpenXR derives itself are left out.
        [Test] public void Snapshot_HoldsHiddenSettings_ButNotTheOnesOpenXRDerives()
        {
            const string hidden = "m_multiviewRenderRegionsOptimizationMode";
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(settings, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var snapshot = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, false);
            var names = snapshot.settings.Select(s => s.propertyName).ToList();
            CollectionAssert.DoesNotContain(names, "customLoaderName");
            CollectionAssert.DoesNotContain(names, "m_Enabled");

            var so = new SerializedObject(settings);
            var property = so.FindProperty(hidden);
            if (property == null)
                Assert.Ignore("This OpenXR has no multiview render regions mode.");
            CollectionAssert.Contains(names, hidden);
            var before = property.intValue;
            try
            {
                property.intValue = before == 0 ? 1 : 0;
                so.ApplyModifiedPropertiesWithoutUndo();
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, snapshot);
                Assert.AreEqual(before, new SerializedObject(settings).FindProperty(hidden).intValue);
            }
            finally
            {
                var reset = new SerializedObject(settings);
                reset.FindProperty(hidden).intValue = before;
                reset.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // The same on the host's Standalone settings: the Apply turned on one
        // controller profile, a pull another, and restoring the merge keeps the pull's.
        [Test] public void Merge_OnRealSettings_RestoresWhatTheApplyChangedOnly()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var applied = standalone.GetFeature<KHRSimpleControllerProfile>();
            var pulled = standalone.GetFeature<HTCViveControllerProfile>();
            Assert.That(applied, Is.Not.Null);
            Assert.That(pulled, Is.Not.Null);
            var original = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            try
            {
                applied.enabled = false;
                pulled.enabled = false;
                var baseline = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true).ToJson();
                applied.enabled = true;
                var expected = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true).ToJson();
                pulled.enabled = true;
                var current = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true).ToJson();

                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone,
                    OpenXREditorStateSnapshot.FromJson(OpenXRSettingsState.Merge(baseline, expected, current)));

                Assert.IsFalse(applied.enabled, "The Apply is undone.");
                Assert.IsTrue(pulled.enabled, "The pull is kept.");
            }
            finally
            {
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, original);
            }
        }

        // A restore that can't write its snapshot throws before writing anything, so the
        // entry stays for a retry and a stopped build leaves the editor as it was.
        [Test] public void Restore_UnreadableSnapshot_Throws()
            => StringAssert.Contains("can't be read", Assert.Throws<System.InvalidOperationException>(
                () => OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, null)).Message);

        [Test] public void Restore_ValueOpenXRCannotTake_ThrowsBeforeWritingAnything()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var feature = standalone.GetFeature<KHRSimpleControllerProfile>();
            Assert.That(feature, Is.Not.Null);
            var original = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            try
            {
                var snapshot = OpenXREditorStateSnapshot.FromJson(original.ToJson());
                var so = new SerializedObject(standalone);
                var numeric = snapshot.settings.First(s =>
                {
                    var type = so.FindProperty(s.propertyName)?.propertyType;
                    return type == SerializedPropertyType.Integer || type == SerializedPropertyType.Enum
                           || type == SerializedPropertyType.Boolean || type == SerializedPropertyType.Float;
                });
                numeric.value = "not a number";
                feature.enabled = !feature.enabled;
                var changed = feature.enabled;

                var message = Assert.Throws<System.InvalidOperationException>(
                    () => OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, snapshot)).Message;

                StringAssert.Contains(numeric.propertyName, message);
                Assert.AreEqual(changed, feature.enabled, "Nothing is written when a value can't be.");
            }
            finally
            {
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, original);
            }
        }

        // There is nothing to restore a removed setting or feature to, and nothing to build with it.
        [Test] public void Restore_SkipsWhatThisOpenXRNoLongerHas_WithAWarning()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var feature = standalone.GetFeature<KHRSimpleControllerProfile>();
            var original = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            try
            {
                var snapshot = OpenXREditorStateSnapshot.FromJson(original.ToJson());
                snapshot.settings.Add(new SettingsOverride { propertyName = "m_noSuchSetting", value = "1" });
                snapshot.features.Add(new OpenXRFeatureState { featureTypeName = "No.Such.Feature", enabled = true, json = "{}" });
                var before = feature.enabled;
                feature.enabled = !before;
                LogAssert.Expect(LogType.Warning, new Regex(
                    "except what this OpenXR no longer has: setting m_noSuchSetting, feature No.Such.Feature"));

                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, snapshot);

                Assert.AreEqual(before, feature.enabled, "The rest is restored.");
            }
            finally
            {
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, original);
            }
        }

        // A feature JSON that can't be read stops the restore before the settings, which
        // come first, are written.
        [Test] public void Restore_UnreadableFeatureJson_ThrowsBeforeWritingAnything()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var original = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            try
            {
                var snapshot = OpenXREditorStateSnapshot.FromJson(original.ToJson());
                snapshot.features.Single(f => f.featureTypeName == typeof(KHRSimpleControllerProfile).FullName).json = "{ not json";
                var so = new SerializedObject(standalone);
                var flag = snapshot.settings.FirstOrDefault(s => so.FindProperty(s.propertyName)?.propertyType == SerializedPropertyType.Boolean);
                Assert.IsNotNull(flag, "OpenXR's settings have an on/off setting to change.");
                so.FindProperty(flag.propertyName).boolValue = !so.FindProperty(flag.propertyName).boolValue;
                so.ApplyModifiedPropertiesWithoutUndo();
                var changed = so.FindProperty(flag.propertyName).boolValue;

                var message = Assert.Throws<System.InvalidOperationException>(
                    () => OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, snapshot)).Message;

                StringAssert.Contains("can't be read", message);
                Assert.AreEqual(changed, new SerializedObject(standalone).FindProperty(flag.propertyName).boolValue,
                    "Nothing is written when a feature's recorded JSON can't be read.");
            }
            finally
            {
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, original);
            }
        }

        // Unknown is not an installed platform: OpenXR's lookup creates settings for any
        // supported group that has none yet.
        [Test] public void Restore_GroupWithoutOpenXRSettings_Throws()
        {
            var snapshot = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            Assert.That(snapshot, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            StringAssert.Contains("no OpenXR settings", Assert.Throws<System.InvalidOperationException>(
                () => OpenXRSettingsState.Restore(BuildTargetGroup.Unknown, snapshot)).Message);
        }

        // A recorded snapshot the post-build step can't read stops it; nothing recorded,
        // because the pre-build step stopped before it wrote, is not an error.
        [Test] public void OnPostBuild_UnreadableRecordedSnapshot_Throws()
        {
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var plugin = new OpenXRPlugin();
                var context = new ForgeBuildContext(null, forge, BuildTarget.StandaloneWindows64, "unused", true);
                Assert.DoesNotThrow(() => plugin.OnPostBuild(context), "Nothing recorded, nothing to restore.");

                context.SetProperty("BuildForge.OpenXR.Snapshot", "not json");
                LogAssert.Expect(LogType.Warning, new Regex("Could not parse settings snapshot"));

                StringAssert.Contains("can't be read", Assert.Throws<System.InvalidOperationException>(
                    () => plugin.OnPostBuild(context)).Message);
            }
            finally { Object.DestroyImmediate(forge); }
        }

        // The on/off state is written as data, after the feature's own JSON, so the
        // snapshot's state wins where the two differ (a merged snapshot), and neither a
        // feature group nor a callback can refuse or widen it. Both are OpenXR internals.
        [Test] public void Restore_WritesTheOnOffStateAsData_OverTheFeaturesJson()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var feature = standalone.GetFeature<KHRSimpleControllerProfile>();
            Assert.IsNotNull(new SerializedObject(feature).FindProperty(OpenXRSettingsState.EnabledField),
                "Build Forge restores OpenXRFeature's on/off field by name.");
            var refusal = typeof(OpenXRFeature).GetField("canSetFeatureDisabled", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(refusal, "OpenXRFeature's feature-group refusal is an OpenXR internal.");
            var original = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            var originalRefusal = refusal.GetValue(null);
            try
            {
                feature.enabled = true; // turning a feature on is never refused
                var snapshot = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true); // its JSON says on
                var state = snapshot.features.Single(f => f.featureTypeName == typeof(KHRSimpleControllerProfile).FullName);
                state.enabled = false;
                // As for a feature an enabled feature group requires: the enabled setter refuses to turn it off.
                refusal.SetValue(null, (System.Func<string, bool>)(_ => false));

                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, snapshot);

                Assert.IsFalse(feature.enabled, "The snapshot's on/off state wins over the JSON and the refusal.");
            }
            finally
            {
                refusal.SetValue(null, originalRefusal);
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, original);
            }
        }

        static OpenXRFeatureSetManager.FeatureSet RequiringControllers(bool ticked) => new OpenXRFeatureSetManager.FeatureSet
        {
            name = "Controllers", isEnabled = ticked,
            featureIds = new[] { KHRSimpleControllerProfile.featureId },
            requiredFeatureIds = new[] { KHRSimpleControllerProfile.featureId },
        };

        // Before anything is written, for builds and Apply: OpenXR's own refusal depends on the
        // platform tab shown last, and refuses nothing in batch mode.
        [Test] public void TurningOffAFeatureATickedGroupRequires_IsAnErrorBeforeWriting()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var features = standalone.GetFeatures<OpenXRFeature>();
            var disables = new OpenXRProfileConfig();
            disables.DisabledFeatures.Add(typeof(KHRSimpleControllerProfile).FullName);
            var ticked = new[] { RequiringControllers(true) };

            var error = OpenXRPlugin.RequiredFeatureConflictError(disables, features, ticked, BuildTargetGroup.Standalone, true);
            StringAssert.Contains("with a Disable override", error);
            StringAssert.Contains("'Controllers', ticked for Standalone", error);
            StringAssert.EndsWith("remove the Disable override.", error);

            Assert.IsNull(OpenXRPlugin.RequiredFeatureConflictError(disables, features, new[] { RequiringControllers(false) },
                BuildTargetGroup.Standalone, true), "An unticked group requires nothing.");
            Assert.IsNull(OpenXRPlugin.RequiredFeatureConflictError(new OpenXRProfileConfig(), features, ticked,
                BuildTargetGroup.Standalone, true), "Without an override nothing is turned off.");
            Assert.IsNull(OpenXRPlugin.RequiredFeatureConflictError(disables, features, ticked, BuildTargetGroup.Standalone, false),
                "Interaction profiles are left alone while their overrides aren't managed.");

            var onlyListed = new OpenXRProfileConfig { OnlyListedFeatures = true };
            StringAssert.EndsWith("list the feature.",
                OpenXRPlugin.RequiredFeatureConflictError(onlyListed, features, ticked, BuildTargetGroup.Standalone, true));
        }

        // OpenXR refuses for the platform in activeBuildTarget: the one being written, while Build Forge writes.
        [Test] public void Apply_PointsOpenXRsRefusalAtThePlatformItWrites_AndPutsItBack()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            var feature = standalone.GetFeature<KHRSimpleControllerProfile>();
            var refusal = typeof(OpenXRFeature).GetField("canSetFeatureDisabled", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(refusal, "OpenXRFeature's feature-group refusal is an OpenXR internal.");
            var original = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            var originalRefusal = refusal.GetValue(null);
            var previousTarget = OpenXRFeatureSetManager.activeBuildTarget;
            try
            {
                feature.enabled = true;
                OpenXRFeatureSetManager.activeBuildTarget = BuildTargetGroup.Android;
                var asked = BuildTargetGroup.Unknown;
                refusal.SetValue(null, (System.Func<string, bool>)(_ => { asked = OpenXRFeatureSetManager.activeBuildTarget; return true; }));
                var config = new OpenXRProfileConfig();
                config.DisabledFeatures.Add(typeof(KHRSimpleControllerProfile).FullName);

                OpenXRSettingsState.Apply(config, BuildTargetGroup.Standalone, true);

                Assert.AreEqual(BuildTargetGroup.Standalone, asked);
                Assert.AreEqual(BuildTargetGroup.Android, OpenXRFeatureSetManager.activeBuildTarget);
                Assert.IsFalse(feature.enabled);
            }
            finally
            {
                refusal.SetValue(null, originalRefusal);
                OpenXRFeatureSetManager.activeBuildTarget = previousTarget;
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, original);
            }
        }

        // The Play Mode copy with Match turns features off in Standalone, which a group ticked there can forbid.
        [Test] public void TheMatchingPlayModeCopy_IsAnErrorBeforeWriting_WhenItWouldTurnOffARequiredFeature()
        {
            var standalone = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            var android = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            Assert.That(standalone, Is.Not.Null, "The OpenXR test host must contain Standalone settings.");
            Assert.That(android, Is.Not.Null, "The OpenXR test host must contain Android settings.");
            var standaloneOriginal = OpenXRSettingsState.Capture(BuildTargetGroup.Standalone, true);
            var androidOriginal = OpenXRSettingsState.Capture(BuildTargetGroup.Android, true);
            void SetOn(OpenXRFeature target, bool enabled)
            {
                var so = new SerializedObject(target);
                so.FindProperty(OpenXRSettingsState.EnabledField).boolValue = enabled;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            try
            {
                SetOn(standalone.GetFeature<KHRSimpleControllerProfile>(), true);
                SetOn(android.GetFeature<KHRSimpleControllerProfile>(), false);

                StringAssert.Contains("would turn off OpenXR feature",
                    OpenXRPlugin.MatchConflictError(BuildTargetGroup.Android, BuildTargetGroup.Standalone, new[] { RequiringControllers(true) }));
                Assert.IsNull(OpenXRPlugin.MatchConflictError(BuildTargetGroup.Android, BuildTargetGroup.Standalone,
                    new[] { RequiringControllers(false) }));
                SetOn(android.GetFeature<KHRSimpleControllerProfile>(), true);
                Assert.IsNull(OpenXRPlugin.MatchConflictError(BuildTargetGroup.Android, BuildTargetGroup.Standalone,
                    new[] { RequiringControllers(true) }), "A feature the platform has on is not turned off.");
            }
            finally
            {
                OpenXRSettingsState.Restore(BuildTargetGroup.Standalone, standaloneOriginal);
                OpenXRSettingsState.Restore(BuildTargetGroup.Android, androidOriginal);
            }
        }
    }
}
