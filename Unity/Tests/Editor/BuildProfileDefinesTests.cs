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
    /// <summary>
    /// One scripting define per Unity Build Profile, kept in the profile's own
    /// Scripting Defines list: naming, list maintenance, and the write path.
    /// </summary>
    public class BuildProfileDefinesTests
    {
        [TestCase("Quest", "BUILD_PROFILE_QUEST")]
        [TestCase("Quest 3", "BUILD_PROFILE_QUEST_3")]
        [TestCase("quest-3 (dev)", "BUILD_PROFILE_QUEST_3_DEV")]
        [TestCase("  Steam  Frame  ", "BUILD_PROFILE_STEAM_FRAME")]
        [TestCase("Pico__4", "BUILD_PROFILE_PICO_4")]
        [TestCase("Ünïcödé", "BUILD_PROFILE_ÜNÏCÖDÉ")]
        [TestCase("U\u0308bung", "BUILD_PROFILE_ÜBUNG")] // decomposed umlaut, NFC-normalized first
        [TestCase("日本", "BUILD_PROFILE_日本")]
        [TestCase("Quest \U0001F600", "BUILD_PROFILE_QUEST")] // emoji is not a letter
        public void DefineFor_Sanitizes(string profileName, string expected)
        {
            Assert.AreEqual(expected, BuildProfileDefines.DefineFor(profileName));
        }

        [Test]
        public void Compute_AddsMissing_KeepsUserDefines_InOrder()
        {
            var result = BuildProfileDefines.Compute(new[] { "USER_A", "USER_B" }, "BUILD_PROFILE_QUEST", out var changed);

            Assert.IsTrue(changed);
            CollectionAssert.AreEqual(new[] { "USER_A", "USER_B", "BUILD_PROFILE_QUEST" }, result);
        }

        [Test]
        public void Compute_ReplacesStaleBuildForgeDefine_AfterRename()
        {
            var result = BuildProfileDefines.Compute(
                new[] { "BUILD_PROFILE_OLD_NAME", "USER_A" }, "BUILD_PROFILE_QUEST", out var changed);

            Assert.IsTrue(changed);
            CollectionAssert.AreEqual(new[] { "USER_A", "BUILD_PROFILE_QUEST" }, result);
        }

        [Test]
        public void Compute_LeavesUserDefinesWithSimilarNamesAlone()
        {
            var result = BuildProfileDefines.Compute(
                new[] { "MY_BUILD_PROFILE_X", "BUILD_PROFILES_LEGACY", "BUILD_PROFILE_QUEST" },
                "BUILD_PROFILE_QUEST", out var changed);

            Assert.IsFalse(changed, "Only the exact prefix marks a Build Forge define.");
            CollectionAssert.AreEqual(new[] { "MY_BUILD_PROFILE_X", "BUILD_PROFILES_LEGACY", "BUILD_PROFILE_QUEST" }, result);
        }

        [Test]
        public void Compute_UpToDate_IsUnchanged()
        {
            var existing = new[] { "USER_A", "BUILD_PROFILE_QUEST" };

            var result = BuildProfileDefines.Compute(existing, "BUILD_PROFILE_QUEST", out var changed);

            Assert.IsFalse(changed);
            CollectionAssert.AreEqual(existing, result);
        }

        [Test]
        public void Compute_EmptyOrNull_AddsOnlyTheDefine()
        {
            CollectionAssert.AreEqual(new[] { "BUILD_PROFILE_QUEST" },
                BuildProfileDefines.Compute(new string[0], "BUILD_PROFILE_QUEST", out _));
            CollectionAssert.AreEqual(new[] { "BUILD_PROFILE_QUEST" },
                BuildProfileDefines.Compute(null, "BUILD_PROFILE_QUEST", out _));
        }

        static BuildProfile NewBuildProfile(string name)
        {
            var factory = typeof(BuildProfile)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "CreateInstance" && m.GetParameters().Length == 2);
            Assert.IsNotNull(factory, "BuildProfile.CreateInstance(BuildTarget, StandaloneBuildSubtarget) not found.");
            var bp = (BuildProfile)factory.Invoke(null,
                new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
            bp.name = name;
            return bp;
        }

        static ForgeProfile NewForgeProfile(string name, BuildProfile buildProfile)
        {
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            profile.name = name;
            var so = new SerializedObject(profile);
            so.FindProperty("buildProfile").objectReferenceValue = buildProfile;
            so.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        [Test]
        public void CollisionWarning_NamesTheOtherBuildProfile_WhenDefinesCoincide()
        {
            var quest3 = NewBuildProfile("Quest 3");
            var questDash3 = NewBuildProfile("Quest-3");
            var pico = NewBuildProfile("Pico");
            var a = NewForgeProfile("A", quest3);
            var b = NewForgeProfile("B", questDash3);
            var c = NewForgeProfile("C", pico);
            var all = new System.Collections.Generic.List<ForgeProfile> { a, b, c };
            try
            {
                var warning = BuildProfileDefines.CollisionWarning(a, all);
                Assert.IsNotNull(warning);
                StringAssert.Contains("BUILD_PROFILE_QUEST_3", warning);
                StringAssert.Contains("'Quest-3'", warning);
                StringAssert.DoesNotContain("'Quest 3'", warning);
                StringAssert.DoesNotContain("Pico", warning);

                Assert.IsNotNull(BuildProfileDefines.CollisionWarning(b, all), "Both sides warn.");
                Assert.IsNull(BuildProfileDefines.CollisionWarning(c, all));
            }
            finally
            {
                foreach (var o in new Object[] { a, b, c, quest3, questDash3, pico })
                    Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void CollisionWarning_IgnoresProfilesSharingTheSameBuildProfile()
        {
            // Sharing one Build Profile is the 1:1 error (ForgeProfileLookup), not a define collision.
            var quest = NewBuildProfile("Quest");
            var a = NewForgeProfile("A", quest);
            var b = NewForgeProfile("B", quest);
            var all = new System.Collections.Generic.List<ForgeProfile> { a, b };
            try
            {
                Assert.IsNull(BuildProfileDefines.CollisionWarning(a, all));
            }
            finally
            {
                foreach (var o in new Object[] { a, b, quest })
                    Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void Ensure_WritesDefine_ThenIsIdempotent_AndFollowsRename()
        {
            var bp = NewBuildProfile("Quest 3");
            var settings = ForgeSettings.instance;
            var previous = settings.MaintainBuildProfileDefines;
            try
            {
                settings.MaintainBuildProfileDefines = true;
                bp.scriptingDefines = new[] { "USER_A" };

                Assert.IsTrue(BuildProfileDefines.NeedsUpdate(bp));
                Assert.IsTrue(BuildProfileDefines.Ensure(bp));
                CollectionAssert.AreEqual(new[] { "USER_A", "BUILD_PROFILE_QUEST_3" }, bp.scriptingDefines);

                Assert.IsFalse(BuildProfileDefines.NeedsUpdate(bp));
                Assert.IsFalse(BuildProfileDefines.Ensure(bp), "Second call must not touch the profile.");

                bp.name = "Quest Pro";
                Assert.IsTrue(BuildProfileDefines.Ensure(bp));
                CollectionAssert.AreEqual(new[] { "USER_A", "BUILD_PROFILE_QUEST_PRO" }, bp.scriptingDefines);
            }
            finally
            {
                settings.MaintainBuildProfileDefines = previous;
                Object.DestroyImmediate(bp);
            }
        }

        [Test]
        public void Ensure_DoesNothing_WhenMaintenanceIsOff()
        {
            var bp = NewBuildProfile("Quest");
            var settings = ForgeSettings.instance;
            var previous = settings.MaintainBuildProfileDefines;
            try
            {
                settings.MaintainBuildProfileDefines = false;
                bp.scriptingDefines = new[] { "USER_A" };

                Assert.IsFalse(BuildProfileDefines.Ensure(bp));
                CollectionAssert.AreEqual(new[] { "USER_A" }, bp.scriptingDefines);
                Assert.IsTrue(BuildProfileDefines.NeedsUpdate(bp), "NeedsUpdate reports the state regardless of the toggle.");
            }
            finally
            {
                settings.MaintainBuildProfileDefines = previous;
                Object.DestroyImmediate(bp);
            }
        }

        // Early 6000.3 patches' scriptingDefines setter leaves Unity's record of the
        // compiled defines behind, and Unity's Build Profiles window then asks to
        // apply or revert them; Build Forge makes Unity's own (internal) request there.
        [Test]
        public void UnitysOwnCompileRequest_IsFoundWhereTheSetterDoesNotMakeIt()
        {
            if (BuildProfileDefines.SetterRecordsDefines)
                Assert.Pass("This editor's scriptingDefines setter records the defines itself.");
            Assert.IsTrue(BuildProfileDefines.CanRecordDefines,
                "UnityEditor.Build.Profile.BuildProfileModuleUtil.RequestScriptCompilation(BuildProfile) was not found.");
        }
    }
}
