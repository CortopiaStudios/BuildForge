using System;
using System.Linq;
using System.Reflection;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    public class VariantRulesTests
    {
        string previousSettings;

        [SetUp] public void SetUp()
        {
            previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var settings = new SerializedObject(ForgeSettings.instance);
            var variants = settings.FindProperty("buildVariants");
            variants.arraySize = 2;
            variants.GetArrayElementAtIndex(0).stringValue = "Development";
            variants.GetArrayElementAtIndex(1).stringValue = "China";
            settings.FindProperty("variantRules").arraySize = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown] public void TearDown()
            => EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);

        [TestCase(null)] [TestCase("")] [TestCase("Developmnt")]
        [TestCase("Development ")] [TestCase("development")] [TestCase("default")]
        public void InvalidRuleNamesAreRejectedEvenForAnotherSelectedVariant(string name)
        {
            var rules = new[] { Rule(name, VariantOverride.Inherit) };
            Assert.That(BuildVariants.RulesError(rules, new[] { "Development" }), Is.Not.Null);
        }

        [Test] public void DuplicateRulesAreRejected()
        {
            var rules = new[] { Rule("Development", VariantOverride.Enabled), Rule("Development", VariantOverride.Disabled) };
            Assert.That(BuildVariants.RulesError(rules, new[] { "Development" }), Does.Contain("more than once"));
        }

        [TestCase(null)] [TestCase("")] [TestCase(" HAS_SPACE")] [TestCase("A B")]
        [TestCase("A;B")] [TestCase("A,B")] [TestCase("A=B")] [TestCase("1DEBUG")]
        [TestCase("A-B")] [TestCase("true")] [TestCase("false")] [TestCase("DEBUG\n")]
        [TestCase("BUILD_PROFILE_OTHER")] [TestCase("BUILD_VARIANT_OTHER")]
        public void InvalidOrGeneratedDefinesAreRejected(string define)
        {
            var rules = new[] { Rule("Default", VariantOverride.Inherit, define) };
            Assert.That(BuildVariants.RulesError(rules, Array.Empty<string>()), Is.Not.Null);
        }

        [Test] public void DuplicateDefinesInOneRuleAreRejectedButSharedAliasesAreAllowed()
        {
            var first = Rule("Development", VariantOverride.Enabled, "DEBUG_TOOLS", "_FEATURE2", "ÅNGSTRÖM");
            var second = Rule("Internal", VariantOverride.Inherit, "DEBUG_TOOLS");
            var variants = new[] { "Development", "Internal" };
            Assert.That(BuildVariants.RulesError(new[] { first, second }, variants), Is.Null);
            first.ScriptingDefines.Add("DEBUG_TOOLS");
            Assert.That(BuildVariants.RulesError(new[] { first, second }, variants), Does.Contain("more than once"));
        }

        [TestCase("{Build}")] [TestCase("{version}")] [TestCase("{Version}.{Commit}")]
        public void UnknownVersionPlaceholdersAreRejected(string template)
        {
            var rule = Rule("Default", VariantOverride.Inherit);
            rule.Version = "{Version}.{BuildNumber}d-{Variant}";
            Assert.That(BuildVariants.RulesError(new[] { rule }, Array.Empty<string>()), Is.Null);
            rule.Version = template;
            Assert.That(BuildVariants.RulesError(new[] { rule }, Array.Empty<string>()), Does.Contain("unknown placeholder"));
        }

        [Test] public void FormatVersion_FillsThePlaceholders_AndNeedsANumberForBuildNumber()
        {
            Assert.AreEqual("1.4.0.412d", BuildVariants.FormatVersion("{Version}.{BuildNumber}d", "1.4.0", "412", "Development", out var error));
            Assert.IsNull(error);
            Assert.AreEqual("1.4.0-cn", BuildVariants.FormatVersion("{Version}-cn", "1.4.0", "", "China", out error));
            Assert.AreEqual("1.4.0 Development", BuildVariants.FormatVersion("{Version} {Variant}", "1.4.0", null, "Development", out error));

            Assert.IsNull(BuildVariants.FormatVersion("{Version}.{BuildNumber}", "1.4.0", "", "Default", out error));
            StringAssert.Contains("no build number", error);
            Assert.IsNull(BuildVariants.FormatVersion("{Variant}", "1.4.0", "1", null, out error), "No variants, no name.");
            StringAssert.Contains("empty version", error);
        }

        [Test] public void BuildNumberFor_UsesThePluginsNumberOnEveryPlatform()
        {
            var context = new ForgeBuildContext(null, null, BuildTarget.StandaloneWindows64, "unused", false);
            Assert.AreEqual("", BuildManifestWriter.BuildNumberFor(context), "Windows has no number of its own.");
            context.BuildNumber = 551;
            Assert.AreEqual("551", BuildManifestWriter.BuildNumberFor(context));
        }

        [TestCase(null)] [TestCase("")] [TestCase("Removed")] [TestCase("Default")] [TestCase("Development ")]
        public void InvalidAllowedVariantsAreRejected(string name)
            => Assert.That(BuildVariants.AllowedVariantsError(new[] { name }, new[] { "Development" }), Is.Not.Null);

        [Test] public void AllowedVariantsRejectDuplicatesButAllowDefaultOnlyProfiles()
        {
            Assert.That(BuildVariants.AllowedVariantsError(new[] { "Development", "Development" }, new[] { "Development" }),
                Does.Contain("more than once"));
            Assert.That(BuildVariants.AllowedVariantsError(Array.Empty<string>(), Array.Empty<string>()), Is.Null);
        }

        [Test] public void InvalidRulesStopBuildAndApplyBeforeSettingsOrLedgerChanges()
        {
            var native = CreateNativeProfile();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var fp = new SerializedObject(forge);
                fp.FindProperty("buildProfile").objectReferenceValue = native;
                fp.ApplyModifiedPropertiesWithoutUndo();
                var settings = new SerializedObject(ForgeSettings.instance);
                var rules = settings.FindProperty("variantRules"); rules.arraySize = 1;
                rules.GetArrayElementAtIndex(0).FindPropertyRelative("variant").stringValue = "RemovedVariant";
                settings.ApplyModifiedPropertiesWithoutUndo();
                native.scriptingDefines = new[] { "USER_SYMBOL" };
                var before = EditorJsonUtility.ToJson(native);
                var ledger = EditorJsonUtility.ToJson(ForgeEditorStateStore.instance);
                var aliases = EditorJsonUtility.ToJson(VariantDefineLedger.instance);
                var product = PlayerSettings.productName;
                Assert.That(BuildVariants.ProfileError(forge, null), Does.Contain("RemovedVariant"));
                Assert.Throws<InvalidOperationException>(() => ForgeBuildRunner.RunBuild(forge));
                Assert.Throws<InvalidOperationException>(() => ForgeEditorState.Apply(forge));
                Assert.Throws<InvalidOperationException>(() => BuildVariants.ApplyOnProfile(native, null));
                Assert.That(EditorJsonUtility.ToJson(native), Is.EqualTo(before));
                Assert.That(EditorJsonUtility.ToJson(ForgeEditorStateStore.instance), Is.EqualTo(ledger));
                Assert.That(EditorJsonUtility.ToJson(VariantDefineLedger.instance), Is.EqualTo(aliases));
                Assert.That(PlayerSettings.productName, Is.EqualTo(product));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(forge);
                UnityEngine.Object.DestroyImmediate(native);
            }
        }

        [Test] public void MissingNativeSettingsAreReportedBeforeEditorApply()
        {
            var native = ScriptableObject.CreateInstance<BuildProfile>();
            try
            {
                Assert.That(VariantBuildSettings.ValidationError(native, Rule("Default", VariantOverride.Disabled)),
                    Does.Contain("native development build setting"));
                Assert.That(VariantBuildSettings.ValidationError(native, Rule("Internal", VariantOverride.Inherit)), Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(native); }
        }

        // Stripping variant state this machine has no record of: a variant define
        // takes the rules' aliases with it, as every build removes them; without
        // one the list is the user's own.
        [Test] public void WithoutStrayVariantState_RemovesRuleAliasesOnlyWithAVariantDefine()
        {
            var rules = new[] { Rule("Internal", VariantOverride.Enabled, "INTERNAL_TOOLS"), Rule("QA", VariantOverride.Inherit, "QA_MENU") };
            CollectionAssert.AreEqual(new[] { "USER" },
                BuildVariants.WithoutStrayVariantState(new[] { "USER", "BUILD_VARIANT_INTERNAL", "INTERNAL_TOOLS", "QA_MENU" }, rules));
            CollectionAssert.AreEqual(new[] { "USER", "INTERNAL_TOOLS" },
                BuildVariants.WithoutStrayVariantState(new[] { "USER", "INTERNAL_TOOLS" }, rules));
            CollectionAssert.IsEmpty(BuildVariants.WithoutStrayVariantState(null, rules));
        }

        static BuildVariantRule Rule(string name, VariantOverride development, params string[] defines)
        {
            var rule = new BuildVariantRule { Variant = name, DevelopmentBuild = development };
            rule.ScriptingDefines.AddRange(defines);
            return rule;
        }

        [Test]
        public void NamedCombinations_RemoveOtherVariantsAliases_AndKeepUnownedDefines()
        {
            var rules = new[] {
                Rule("Default", VariantOverride.Disabled),
                Rule("Development", VariantOverride.Enabled, "DEVELOPMENT"),
                Rule("China", VariantOverride.Disabled, "REGION_CHINA"),
                Rule("ChinaDevelopment", VariantOverride.Enabled, "DEVELOPMENT", "REGION_CHINA")
            };
            var chinaDev = BuildVariants.WithRuleDefines(new[] { "MY_SYMBOL" }, "ChinaDevelopment", rules, true);
            CollectionAssert.AreEquivalent(new[] { "MY_SYMBOL", "BUILD_VARIANT_CHINADEVELOPMENT", "DEVELOPMENT", "REGION_CHINA" }, chinaDev);
            var release = BuildVariants.WithRuleDefines(chinaDev, null, rules, true);
            CollectionAssert.AreEquivalent(new[] { "MY_SYMBOL", "BUILD_VARIANT_DEFAULT" }, release);
            var dev = BuildVariants.WithRuleDefines(chinaDev, "Development", rules, true);
            Assert.That(dev, Does.Not.Contain("REGION_CHINA"));
        }

        [Test]
        public void UnconfiguredRules_PreserveExistingDefines()
        {
            var actual = BuildVariants.WithRuleDefines(new[] { "DEVELOPMENT", "EXTERNAL" }, "Internal", Array.Empty<BuildVariantRule>(), true);
            CollectionAssert.AreEquivalent(new[] { "DEVELOPMENT", "EXTERNAL", "BUILD_VARIANT_INTERNAL" }, actual);
        }

        [Test]
        public void ProfileRestrictions_AlwaysAllowDefault_AndRejectOtherRegions()
        {
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var so = new SerializedObject(profile);
                so.FindProperty("restrictVariants").boolValue = true;
                var allowed = so.FindProperty("allowedVariants");
                allowed.arraySize = 1; allowed.GetArrayElementAtIndex(0).stringValue = "Development";
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsTrue(profile.SupportsVariant(null));
                Assert.IsTrue(profile.SupportsVariant("Development"));
                Assert.IsFalse(profile.SupportsVariant("China"));
                StringAssert.Contains("China", BuildVariants.ProfileError(profile, "China"));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        internal static BuildProfile CreateNativeProfile()
        {
            var method = typeof(BuildProfile).GetMethod("CreateInstance", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(BuildTarget), typeof(StandaloneBuildSubtarget) }, null);
            Assert.IsNotNull(method, "Unity's native profile factory changed.");
            return (BuildProfile)method.Invoke(null, new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
        }

        [Test]
        public void ReleaseClearsNativeDebugFlags_AndRestoreReturnsExactSettings()
        {
            var profile = CreateNativeProfile();
            try
            {
                var so = new SerializedObject(profile);
                foreach (var field in VariantBuildSettings.Fields)
                    so.FindProperty("m_PlatformBuildProfile." + field).boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                var before = VariantBuildSettings.Capture(profile);
                VariantBuildSettings.Apply(profile, Rule("Default", VariantOverride.Disabled));
                so.Update();
                foreach (var field in VariantBuildSettings.Fields)
                    Assert.IsFalse(so.FindProperty("m_PlatformBuildProfile." + field).boolValue, field);
                VariantBuildSettings.Apply(profile, Rule("Development", VariantOverride.Enabled));
                Assert.IsTrue(VariantBuildSettings.IsDevelopment(profile));
                VariantBuildSettings.Restore(profile, before);
                Assert.AreEqual(before, VariantBuildSettings.Capture(profile));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void InheritDoesNotAlterNativeBuildSettings()
        {
            var profile = CreateNativeProfile();
            try
            {
                var before = VariantBuildSettings.Capture(profile);
                VariantBuildSettings.Apply(profile, Rule("Internal", VariantOverride.Inherit));
                Assert.AreEqual(before, VariantBuildSettings.Capture(profile));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }
    }
}
