using System.IO;
using System.Reflection;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build.Profile;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class PlayerSettingsProfileTests
    {
        const string ProfilePath = "Assets/BuildForgeTestProfile.asset";
        BuildProfile profile;

        /// <summary>
        /// A Build Profile of the editor's own platform, with Player Settings of its own, saved
        /// as an asset: the test hosts have none. It isn't made active: activating any profile
        /// makes Unity 6000.3 compile scripts and reload, which the tests after this one would
        /// run into.
        /// </summary>
        void CreateProfileWithItsOwnPlayerSettings()
        {
            AssetDatabase.DeleteAsset(ProfilePath);
            profile = VariantRulesTests.CreateNativeProfile(EditorUserBuildSettings.activeBuildTarget);
            AssetDatabase.CreateAsset(profile, ProfilePath);
            var createPlayerSettings = typeof(BuildProfile).Assembly.GetType("UnityEditor.Build.Profile.BuildProfileModuleUtil")
                ?.GetMethod("CreatePlayerSettingsFromGlobal", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(createPlayerSettings, "Unity's factory for a Build Profile's own Player Settings changed.");
            createPlayerSettings.Invoke(null, new object[] { profile });
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            if (profile == null)
                return;
            AssetDatabase.DeleteAsset(ProfilePath);
            profile = null;
        }

        // Every build saves the restored Player Settings, including a profile's own; a Unity
        // version that keeps them somewhere Build Forge doesn't know fails here first.
        [Test]
        public void ThisEditor_KeepsAProfilesOwnPlayerSettingsWhereBuildForgeCanSaveThem()
        {
            Assert.IsTrue(PlayerSettingsPersistence.CanSaveProfilePlayerSettings,
                "Neither BuildProfile.SerializePlayerSettings() (up to 6000.6) nor BuildProfile.buildProfilePlayerSettings " +
                "(6000.7 and later) exists in this editor.");
        }

        // After a build puts a profile's own Player Settings back, Build Forge saves them into the
        // profile's file. Unity 6000.7 moved them, and every build then failed its restore; this
        // fails in such an editor instead of a build.
        [Test]
        public void ARestoredValue_ReachesTheProfilesOwnPlayerSettingsFile()
        {
            CreateProfileWithItsOwnPlayerSettings();
            // The profile keeps its own in an embedded YAML copy up to 6000.6, in a sub-asset from
            // 6000.7; either way in its own file.
            var embedded = new SerializedObject(profile).FindProperty("m_PlayerSettingsYaml.m_Settings");
            var subasset = typeof(BuildProfile).GetField("buildProfilePlayerSettings",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(profile) as UnityEngine.Object;
            Assert.IsTrue(embedded != null && embedded.arraySize > 0 || subasset != null,
                "The test profile has no Player Settings of its own where Build Forge expects them.");

            var own = new SerializedObject(profile.GetComponent<PlayerSettings>());
            var versionCode = own.FindProperty("AndroidBundleVersionCode");
            Assert.IsNotNull(versionCode, "AndroidBundleVersionCode property should exist");
            var restored = versionCode.intValue == 99999 ? 99998 : 99999;
            versionCode.intValue = restored;
            own.ApplyModifiedPropertiesWithoutUndo();

            PlayerSettingsPersistence.SaveRestoredSettings(profile);

            StringAssert.Contains($"AndroidBundleVersionCode: {restored}", File.ReadAllText(ProfilePath),
                "The restored value must reach the profile's own Player Settings on disk.");
        }

        // Build Forge reads and writes a profile's own Player Settings through the component.
        [Test]
        public void GetComponent_ReturnsTheProfilesOwnPlayerSettings()
        {
            CreateProfileWithItsOwnPlayerSettings();

            var ps = profile.GetComponent<PlayerSettings>();
            Assert.IsNotNull(ps, "GetComponent<PlayerSettings>() returned null for a profile with Player Settings of its own.");
            Assert.AreNotSame(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"), ps,
                "GetComponent<PlayerSettings>() returned the project's Player Settings, not the profile's own.");
        }
    }
}
