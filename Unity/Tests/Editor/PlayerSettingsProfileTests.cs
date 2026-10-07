using System.IO;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build.Profile;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class PlayerSettingsProfileTests
    {
        [Test]
        public void StaticPlayerSettingsAPI_TargetsActiveProfileSettings()
        {
            var profile = BuildProfile.GetActiveBuildProfile();
            if (profile == null)
            {
                Assert.Ignore("No active build profile");
                return;
            }

            var profilePS = profile.GetComponent<PlayerSettings>();
            if (profilePS == null)
            {
                Assert.Ignore("Active profile has no PlayerSettings component");
                return;
            }

            var before = PlayerSettings.Android.bundleVersionCode;

            try
            {
                PlayerSettings.Android.bundleVersionCode = 99999;
                var so = new SerializedObject(profilePS);
                so.Update();
                var prop = so.FindProperty("AndroidBundleVersionCode");
                Assert.IsNotNull(prop, "AndroidBundleVersionCode property should exist");
                Assert.AreEqual(99999, prop.intValue,
                    "Static API write should be visible via profile's SerializedObject");

                // GetComponent falls back to global settings when the profile has no overrides.
                var embedded = new SerializedObject(profile).FindProperty("m_PlayerSettingsYaml.m_Settings");
                var path = embedded != null && embedded.arraySize > 0
                    ? AssetDatabase.GetAssetPath(profile) : "ProjectSettings/ProjectSettings.asset";
                PlayerSettingsPersistence.SaveRestoredSettings();
                StringAssert.Contains("AndroidBundleVersionCode: 99999", File.ReadAllText(path));

                PlayerSettings.Android.bundleVersionCode = before;
                PlayerSettingsPersistence.SaveRestoredSettings();
                StringAssert.Contains($"AndroidBundleVersionCode: {before}", File.ReadAllText(path),
                    "Restoring the live object must also refresh the profile's embedded settings on disk.");
            }
            finally
            {
                PlayerSettings.Android.bundleVersionCode = before;
                PlayerSettingsPersistence.SaveRestoredSettings();
            }
        }

        [Test]
        public void GetComponent_ReturnsNonNull_ForActiveProfile()
        {
            var profile = BuildProfile.GetActiveBuildProfile();
            if (profile == null)
            {
                Assert.Ignore("No active build profile");
                return;
            }

            var ps = profile.GetComponent<PlayerSettings>();
            Assert.IsNotNull(ps, "GetComponent<PlayerSettings>() should never return null " +
                "(falls back to global PlayerSettings)");
        }
    }
}
