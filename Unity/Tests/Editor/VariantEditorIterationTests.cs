using System;
using System.IO;
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
    public class VariantEditorIterationTests
    {
        [Test]
        public void DevelopmentToDefaultToRevert_RestoresUserDefinesAndLeavesNativeFlags()
        {
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var path = "Assets/ForgeVariantTest_" + Guid.NewGuid().ToString("N") + ".asset";
            var profile = (BuildProfile)typeof(BuildProfile).GetMethod("CreateInstance", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(BuildTarget), typeof(StandaloneBuildSubtarget) }, null)
                .Invoke(null, new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
            AssetDatabase.CreateAsset(profile, path);
            try
            {
                var settings = new SerializedObject(ForgeSettings.instance);
                var names = settings.FindProperty("buildVariants"); names.arraySize = 1;
                names.GetArrayElementAtIndex(0).stringValue = "Development";
                var rules = settings.FindProperty("variantRules"); rules.arraySize = 2;
                for (var i = 0; i < 2; i++)
                {
                    var rule = rules.GetArrayElementAtIndex(i);
                    rule.FindPropertyRelative("variant").stringValue = i == 0 ? "Default" : "Development";
                    rule.FindPropertyRelative("developmentBuild").enumValueIndex = i == 0 ? 1 : 2;
                    rule.FindPropertyRelative("markProductName").enumValueIndex = i == 0 ? 1 : 2;
                    var aliases = rule.FindPropertyRelative("scriptingDefines"); aliases.arraySize = i;
                    if (i == 1) aliases.GetArrayElementAtIndex(0).stringValue = "DEVELOPMENT";
                }
                settings.ApplyModifiedPropertiesWithoutUndo();
                profile.scriptingDefines = new[] { "USER_SYMBOL", "DEVELOPMENT" };
                var native = new SerializedObject(profile);
                native.FindProperty("m_PlatformBuildProfile.m_AllowDebugging").boolValue = true;
                native.ApplyModifiedPropertiesWithoutUndo();
                var before = VariantBuildSettings.Capture(profile);

                BuildVariants.ApplyOnProfile(profile, "Development");
                Assert.That(VariantBuildSettings.Capture(profile), Is.EqualTo(before), "Apply writes defines only; builds apply the rule's flags");
                Assert.That(profile.scriptingDefines, Does.Contain("DEVELOPMENT"));
                Assert.That(BuildVariants.ShouldMark("Development"), Is.True);
                BuildVariants.ApplyOnProfile(profile, null);
                Assert.That(VariantBuildSettings.Capture(profile), Is.EqualTo(before));
                Assert.That(profile.scriptingDefines, Does.Not.Contain("DEVELOPMENT"));
                Assert.That(File.ReadAllText(path), Does.Contain("BUILD_VARIANT_DEFAULT"));
                BuildVariants.StripFromProfile(profile);
                CollectionAssert.AreEqual(new[] { "USER_SYMBOL", "DEVELOPMENT" }, profile.scriptingDefines);
                Assert.That(VariantBuildSettings.Capture(profile), Is.EqualTo(before));
                Assert.That(File.ReadAllText(path), Does.Not.Contain("BUILD_VARIANT_"));
            }
            finally
            {
                VariantDefineLedger.instance.TryRestore(profile, out _);
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                ForgeSettings.instance.SaveSettings();
                AssetDatabase.DeleteAsset(path);
            }
        }

        // Regression: the ledger used to freeze the profile's defines and flags
        // at the first Apply, so builds ignored later edits and Re-apply/Revert
        // wrote the frozen values back over them.
        [Test]
        public void EditsMadeAfterApply_ReachBuildsAndSurviveReapplyAndRevert()
        {
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var path = "Assets/ForgeVariantTest_" + Guid.NewGuid().ToString("N") + ".asset";
            var profile = CreateProfile();
            AssetDatabase.CreateAsset(profile, path);
            try
            {
                var settings = new SerializedObject(ForgeSettings.instance);
                var names = settings.FindProperty("buildVariants"); names.arraySize = 1;
                names.GetArrayElementAtIndex(0).stringValue = "Development";
                var rules = settings.FindProperty("variantRules"); rules.arraySize = 1;
                var rule = rules.GetArrayElementAtIndex(0);
                rule.FindPropertyRelative("variant").stringValue = "Development";
                rule.FindPropertyRelative("developmentBuild").enumValueIndex = (int)VariantOverride.Enabled;
                rule.FindPropertyRelative("markProductName").enumValueIndex = (int)VariantOverride.Inherit;
                var aliases = rule.FindPropertyRelative("scriptingDefines"); aliases.arraySize = 1;
                aliases.GetArrayElementAtIndex(0).stringValue = "DEVELOPMENT";
                settings.ApplyModifiedPropertiesWithoutUndo();
                profile.scriptingDefines = new[] { "USER_SYMBOL" };
                SetFlag(profile, "m_Development", false);
                SetFlag(profile, "m_AllowDebugging", false);

                BuildVariants.ApplyOnProfile(profile, "Development");
                Assert.That(GetFlag(profile, "m_Development"), Is.False, "Apply writes the variant's defines, never its development flags");
                Assert.That(File.ReadAllText(path), Does.Not.Contain("m_Development: 1"),
                    "a Build Profile committed while the variant is applied must not carry its flag to other machines");

                // Edits in Unity's Build Profiles window while the variant is applied.
                profile.scriptingDefines = profile.scriptingDefines.Concat(new[] { "ENABLE_LOGS" }).ToArray();
                SetFlag(profile, "m_AllowDebugging", true);

                // A Default build starts from the user's values, edits included.
                var defines = profile.scriptingDefines;
                var flags = VariantBuildSettings.Capture(profile);
                BuildVariants.BeginBuild(profile, null);
                VariantDefineLedger.instance.UndoBuildSettings(profile);
                CollectionAssert.AreEquivalent(new[] { "USER_SYMBOL", "ENABLE_LOGS", "BUILD_VARIANT_DEFAULT" }, profile.scriptingDefines);
                Assert.That(GetFlag(profile, "m_Development"), Is.False, "the applied variant's flag must not leak into a Default build");
                Assert.That(GetFlag(profile, "m_AllowDebugging"), Is.True);
                BuildVariants.EndBuild(profile, defines);
                VariantBuildSettings.Restore(profile, flags);

                // Re-apply keeps the edits.
                BuildVariants.ApplyOnProfile(profile, "Development");
                CollectionAssert.AreEquivalent(new[] { "USER_SYMBOL", "ENABLE_LOGS", "BUILD_VARIANT_DEVELOPMENT", "DEVELOPMENT" }, profile.scriptingDefines);
                Assert.That(GetFlag(profile, "m_AllowDebugging"), Is.True);

                // More edits after Re-apply: a new define, and a flag Build Forge did not set.
                profile.scriptingDefines = profile.scriptingDefines.Concat(new[] { "EDIT_AFTER_REAPPLY" }).ToArray();
                SetFlag(profile, "m_AllowDebugging", false);

                // Revert keeps every edit and removes only what Build Forge added.
                BuildVariants.StripFromProfile(profile);
                CollectionAssert.AreEquivalent(new[] { "USER_SYMBOL", "ENABLE_LOGS", "EDIT_AFTER_REAPPLY" }, profile.scriptingDefines);
                Assert.That(GetFlag(profile, "m_Development"), Is.False);
                Assert.That(GetFlag(profile, "m_AllowDebugging"), Is.False);
            }
            finally
            {
                VariantDefineLedger.instance.TryRestore(profile, out _);
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                ForgeSettings.instance.SaveSettings();
                AssetDatabase.DeleteAsset(path);
            }
        }

        // An Apply that changes nothing, as in a project without variants or
        // rules, must leave nothing in the ledger that could later be restored.
        [Test]
        public void ApplyWithoutVariantsOrRules_RecordsNothing()
        {
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var path = "Assets/ForgeVariantTest_" + Guid.NewGuid().ToString("N") + ".asset";
            var profile = CreateProfile();
            AssetDatabase.CreateAsset(profile, path);
            try
            {
                var settings = new SerializedObject(ForgeSettings.instance);
                settings.FindProperty("buildVariants").arraySize = 0;
                settings.FindProperty("variantRules").arraySize = 0;
                settings.ApplyModifiedPropertiesWithoutUndo();
                profile.scriptingDefines = new[] { "USER_SYMBOL" };

                BuildVariants.ApplyOnProfile(profile, null);
                var guid = AssetDatabase.AssetPathToGUID(path);
                Assert.That(EditorJsonUtility.ToJson(VariantDefineLedger.instance), Does.Not.Contain(guid));

                profile.scriptingDefines = new[] { "USER_SYMBOL", "ADDED_LATER" };
                Assert.That(VariantDefineLedger.instance.TryRestore(profile, out _), Is.False);
                CollectionAssert.AreEqual(new[] { "USER_SYMBOL", "ADDED_LATER" }, profile.scriptingDefines);
            }
            finally
            {
                VariantDefineLedger.instance.TryRestore(profile, out _);
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                ForgeSettings.instance.SaveSettings();
                AssetDatabase.DeleteAsset(path);
            }
        }

        // A Build Profile committed while a variant was applied on another
        // machine: nothing in this machine's ledger explains its variant define.
        // It is reported until Activate's strip removes it, together with the
        // rule's aliases; a profile without a variant define keeps its defines.
        [Test]
        public void VariantStateSavedElsewhere_IsReportedAndStrippedWithItsAliases()
        {
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var path = "Assets/ForgeVariantTest_" + Guid.NewGuid().ToString("N") + ".asset";
            var profile = CreateProfile();
            AssetDatabase.CreateAsset(profile, path);
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var settings = new SerializedObject(ForgeSettings.instance);
                var names = settings.FindProperty("buildVariants"); names.arraySize = 1;
                names.GetArrayElementAtIndex(0).stringValue = "Development";
                var rules = settings.FindProperty("variantRules"); rules.arraySize = 1;
                var rule = rules.GetArrayElementAtIndex(0);
                rule.FindPropertyRelative("variant").stringValue = "Development";
                rule.FindPropertyRelative("developmentBuild").enumValueIndex = (int)VariantOverride.Enabled;
                rule.FindPropertyRelative("markProductName").enumValueIndex = (int)VariantOverride.Inherit;
                var aliases = rule.FindPropertyRelative("scriptingDefines"); aliases.arraySize = 1;
                aliases.GetArrayElementAtIndex(0).stringValue = "DEVELOPMENT";
                settings.ApplyModifiedPropertiesWithoutUndo();
                var fp = new SerializedObject(forge);
                fp.FindProperty("buildProfile").objectReferenceValue = profile;
                fp.ApplyModifiedPropertiesWithoutUndo();

                // As committed where Development was applied by a Build Forge that
                // still wrote the rule's development flag.
                profile.scriptingDefines = new[] { "USER_SYMBOL", "BUILD_VARIANT_DEVELOPMENT", "DEVELOPMENT" };
                SetFlag(profile, "m_Development", true);

                var warning = BuildVariants.StrayWarning(forge, appliedHere: false);
                StringAssert.Contains("carries BUILD_VARIANT_DEVELOPMENT", warning);
                StringAssert.Contains("variant 'Development' was applied", warning);
                StringAssert.Contains("Its defines BUILD_VARIANT_DEVELOPMENT, DEVELOPMENT compile into the editor", warning);
                StringAssert.Contains("a Default build is a development build", warning);
                Assert.IsNull(BuildVariants.StrayWarning(forge, appliedHere: true), "expected state where it is applied");

                BuildVariants.StripFromProfile(profile);
                CollectionAssert.AreEqual(new[] { "USER_SYMBOL" }, profile.scriptingDefines);
                Assert.IsNull(BuildVariants.StrayWarning(forge, appliedHere: false));

                profile.scriptingDefines = new[] { "USER_SYMBOL", "DEVELOPMENT" };
                BuildVariants.StripFromProfile(profile);
                CollectionAssert.AreEqual(new[] { "USER_SYMBOL", "DEVELOPMENT" }, profile.scriptingDefines);
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                ForgeSettings.instance.SaveSettings();
                UnityEngine.Object.DestroyImmediate(forge);
                AssetDatabase.DeleteAsset(path);
            }
        }

        static BuildProfile CreateProfile()
            => (BuildProfile)typeof(BuildProfile).GetMethod("CreateInstance", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(BuildTarget), typeof(StandaloneBuildSubtarget) }, null)
                .Invoke(null, new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });

        static void SetFlag(BuildProfile profile, string field, bool value)
        {
            var so = new SerializedObject(profile);
            so.FindProperty("m_PlatformBuildProfile." + field).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static bool GetFlag(BuildProfile profile, string field)
            => new SerializedObject(profile).FindProperty("m_PlatformBuildProfile." + field).boolValue;
    }
}
