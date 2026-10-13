using BuildForge.Editor.Core;
using BuildForge.Editor.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// A Build Profile's own platform, by the name and icon Unity's Build Profiles window
    /// gives it, which the build window and the main toolbar show instead of the build
    /// target: a Meta Quest profile builds for Android.
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
                Assert.AreEqual("BuildSettings.Windows", BuildProfilePlatform.IconName(profile));
                Assert.AreEqual("Windows", ForgeBuildWindow.PlatformName(profile));
                Assert.IsNotNull(ForgeBuildWindow.PlatformIcon(profile));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void AMetaQuestProfile_IsMetaQuest_NotItsAndroidBuildTarget()
        {
            var profile = ProfileWithPlatform(MetaQuestPlatformId);
            try
            {
                if (BuildProfilePlatform.DisplayName(profile) == null)
                    Assert.Ignore("This editor has no Meta Quest platform; it comes with Android Build Support.");
                Assert.AreEqual("Meta Quest", ForgeBuildWindow.PlatformName(profile));
                Assert.AreEqual("BuildSettings.Meta", BuildProfilePlatform.IconName(profile));
                Assert.IsNotNull(ForgeBuildWindow.PlatformIcon(profile));
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
                Assert.IsNotNull(ForgeBuildWindow.PlatformIcon(profile));
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
    }
}
