using System;
using System.Linq;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// A Build Profile's own platform, by the name and icon Unity's Build Profiles window
    /// gives it, which the build window and the main toolbar show instead of the build
    /// target: a Meta Quest profile builds for Android. And whether the editor has it disabled.
    /// </summary>
    public class BuildProfilePlatformTests
    {
        const string MetaQuestPlatformId = "80657fe557de4d17822398b3a01b8c9e";

        static BuildProfile ProfileWithPlatform(string platformId)
        {
            var profile = VariantRulesTests.CreateNativeProfile();
            if (platformId != null)
            {
                var so = new SerializedObject(profile);
                so.FindProperty("m_PlatformId").stringValue = platformId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return profile;
        }

        [Test]
        public void AWindowsProfile_IsWindows_WithUnitysWindowsIcon()
        {
            var profile = ProfileWithPlatform(null);
            try
            {
                Assert.AreEqual("Windows", BuildProfilePlatform.DisplayName(profile));
                Assert.AreEqual("Windows", ForgeBuildWindow.PlatformName(profile));
                StringAssert.Contains("BuildSettings.Windows.Small", ForgeBuildWindow.PlatformIcon(profile).name);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        // The header draws the icon at 28 points: the small one, made for 16, came out blurred.
        [Test]
        public void TheHeadersIcon_IsUnitysLargePlatformIcon()
        {
            var profile = ProfileWithPlatform(null);
            try
            {
                var large = ForgeBuildWindow.LargePlatformIcon(profile);
                StringAssert.Contains("BuildSettings.Windows", large.name);
                StringAssert.DoesNotContain(".Small", large.name);
                Assert.Greater(large.width, ForgeBuildWindow.PlatformIcon(profile).width);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        // A Windows profile carrying Meta Quest's platform id: name and icon come from
        // the id, not from the build target, which a real Meta Quest profile shares
        // with Android.
        [Test]
        public void AMetaQuestPlatformId_IsMetaQuest_NotTheBuildTarget()
        {
            var profile = ProfileWithPlatform(MetaQuestPlatformId);
            try
            {
                if (BuildProfilePlatform.DisplayName(profile) == null)
                    Assert.Ignore("This editor has no Meta Quest platform; it comes with Android Build Support.");
                Assert.AreEqual("Meta Quest", ForgeBuildWindow.PlatformName(profile));
                var icon = ForgeBuildWindow.PlatformIcon(profile);
                StringAssert.Contains("BuildSettings.Meta.Small", icon.name, "Meta Quest's own icon, not the build target's.");
                StringAssert.Contains("BuildSettings.Meta", ForgeBuildWindow.LargePlatformIcon(profile).name);
                Assert.AreNotSame(ForgeBuildWindow.PlatformIcon(BuildProfileUtility.GetBuildTarget(profile)), icon);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void AnUnknownPlatform_FallsBackToTheBuildTarget()
        {
            var profile = ProfileWithPlatform("0123456789abcdef0123456789abcdef");
            try
            {
                Assert.AreEqual(BuildProfileUtility.GetBuildTarget(profile).ToString(), ForgeBuildWindow.PlatformName(profile));
                var targetIcon = ForgeBuildWindow.PlatformIcon(BuildProfileUtility.GetBuildTarget(profile));
                Assert.IsNotNull(targetIcon);
                Assert.AreSame(targetIcon, ForgeBuildWindow.PlatformIcon(profile));
                Assert.AreSame(targetIcon, ForgeBuildWindow.LargePlatformIcon(profile), "Not Unity's generic editor icon.");
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void PlatformLabel_AddsTheBuildTarget_OnlyWhenItHasAnotherName()
        {
            Assert.AreEqual("Meta Quest (Android)", ForgeBuildWindow.PlatformLabel("Meta Quest", BuildTarget.Android));
            Assert.AreEqual("Windows (StandaloneWindows64)", ForgeBuildWindow.PlatformLabel("Windows", BuildTarget.StandaloneWindows64));
            Assert.AreEqual("Android™", ForgeBuildWindow.PlatformLabel("Android™", BuildTarget.Android));
        }

        // Disabled is the Build Profiles window's "currently disabled": a derived platform the
        // editor has but has not enabled. Unity enables Meta Quest for the user, with an
        // EditorPrefs value named after the platform that Enable Platform sets, or with
        // -enableAllDerivedPlatforms, so the check agrees with those on any machine.
        [Test]
        public void MetaQuest_IsDisabled_ExactlyWhenUnityHasNotEnabledIt()
        {
            var profile = ProfileWithPlatform(MetaQuestPlatformId);
            try
            {
                if (BuildProfilePlatform.DisplayName(profile) == null)
                    Assert.Ignore("This editor has no Meta Quest platform; it comes with Android Build Support.");
                var enabled = EditorPrefs.GetInt(MetaQuestPlatformId, 0) == 1
                              || Environment.GetCommandLineArgs().Contains("-enableAllDerivedPlatforms");
                Assert.AreEqual(!enabled, BuildProfilePlatform.IsDisabled(profile));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        // A definite answer, not null: on an editor where Build Forge can't tell, every build
        // would stop, so these fail there first.
        [Test]
        public void ABasePlatform_AndAnUnknownOne_AreNotDisabled()
        {
            var windows = ProfileWithPlatform(null);
            var unknown = ProfileWithPlatform("0123456789abcdef0123456789abcdef");
            try
            {
                Assert.AreEqual(false, BuildProfilePlatform.IsDisabled(windows));
                Assert.AreEqual(false, BuildProfilePlatform.IsDisabled(unknown), "A platform Unity doesn't know is not disabled.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(windows);
                UnityEngine.Object.DestroyImmediate(unknown);
            }
        }

        // The build stops before it changes anything: for a disabled platform, naming it and both
        // ways to enable it; where Build Forge can't tell, saying so rather than building unchecked.
        [TestCase(true, "is for Windows, which is disabled in this editor", "Enable Platform", "-enableAllDerivedPlatforms")]
        [TestCase(null, "cannot tell whether the platform of", "Update Build Forge", "does not build it unchecked")]
        public void ABuildOfADisabledPlatform_OrOneBuildForgeCannotCheck_StopsBeforeItStarts(
            bool? disabled, string reason, string remedy, string detail)
        {
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var native = VariantRulesTests.CreateNativeProfile();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            BuildProfilePlatform.IsDisabledOverride = profile => profile == native ? disabled : false;
            try
            {
                var settings = new SerializedObject(ForgeSettings.instance);
                settings.FindProperty("buildVariants").arraySize = 0;
                settings.FindProperty("variantRules").arraySize = 0;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var fp = new SerializedObject(forge);
                fp.FindProperty("buildProfile").objectReferenceValue = native;
                fp.ApplyModifiedPropertiesWithoutUndo();
                var before = EditorJsonUtility.ToJson(native);

                var message = Assert.Throws<InvalidOperationException>(() => ForgeBuildRunner.RunBuild(forge)).Message;
                StringAssert.Contains(reason, message);
                StringAssert.Contains(remedy, message);
                StringAssert.Contains(detail, message);
                Assert.That(EditorJsonUtility.ToJson(native), Is.EqualTo(before));
            }
            finally
            {
                BuildProfilePlatform.IsDisabledOverride = null;
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                UnityEngine.Object.DestroyImmediate(forge);
                UnityEngine.Object.DestroyImmediate(native);
            }
        }
    }
}
