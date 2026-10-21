using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// The build window computes its warnings at a refresh and says when the settings
    /// they read changed since, by comparing what it recorded then with what they hold now.
    /// </summary>
    public class BuildWindowStaleWarningsTests
    {
        [Serializable]
        public class FingerprintConfig
        {
            public int value;
        }

        readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        BuildProfile NewBuildProfile(string name)
        {
            var factory = typeof(BuildProfile)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "CreateInstance" && m.GetParameters().Length == 2);
            Assert.IsNotNull(factory, "BuildProfile.CreateInstance(BuildTarget, StandaloneBuildSubtarget) not found.");
            var bp = (BuildProfile)factory.Invoke(null,
                new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
            bp.name = name;
            created.Add(bp);
            return bp;
        }

        ForgeProfile NewForgeProfile(BuildProfile buildProfile)
        {
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            var so = new SerializedObject(profile);
            so.FindProperty("buildProfile").objectReferenceValue = buildProfile;
            so.ApplyModifiedPropertiesWithoutUndo();
            created.Add(profile);
            return profile;
        }

        [Test]
        public void TheFingerprint_StaysTheSame_WhileNothingChanges()
        {
            var profiles = new[] { NewForgeProfile(NewBuildProfile("Quest")), NewForgeProfile(NewBuildProfile("Windows")) };

            Assert.AreEqual(ForgeBuildWindow.SettingsFingerprint(profiles, ForgeSettings.instance),
                ForgeBuildWindow.SettingsFingerprint(profiles, ForgeSettings.instance));
        }

        [Test]
        public void TheFingerprint_Changes_WithAProfilesBuildForgeSettings()
        {
            var profile = NewForgeProfile(NewBuildProfile("Quest"));
            var profiles = new[] { profile };
            var refreshed = ForgeBuildWindow.SettingsFingerprint(profiles, null);

            var so = new SerializedObject(profile);
            so.FindProperty("outputPath").stringValue = "Builds/{ProfileName}";
            so.ApplyModifiedPropertiesWithoutUndo();
            var afterOutputPath = ForgeBuildWindow.SettingsFingerprint(profiles, null);
            Assert.AreNotEqual(refreshed, afterOutputPath);

            // A plugin's settings, as its section in the Inspector writes them.
            profile.SetPluginConfig("Fingerprint", new FingerprintConfig { value = 1 });
            Assert.AreNotEqual(afterOutputPath, ForgeBuildWindow.SettingsFingerprint(profiles, null));
        }

        [Test]
        public void TheFingerprint_Changes_WithTheBuildProfilesDefines_ButNotWithItsBuildProfileDefine()
        {
            var buildProfile = NewBuildProfile("Quest");
            var profiles = new[] { NewForgeProfile(buildProfile) };
            var refreshed = ForgeBuildWindow.SettingsFingerprint(profiles, null);

            buildProfile.scriptingDefines = new[] { "BUILD_VARIANT_INTERNAL" };
            var afterVariantDefine = ForgeBuildWindow.SettingsFingerprint(profiles, null);
            Assert.AreNotEqual(refreshed, afterVariantDefine);

            // Build Forge writes this one itself, a refresh among others.
            buildProfile.scriptingDefines = new[] { BuildProfileDefines.DefineFor("Quest"), "BUILD_VARIANT_INTERNAL" };
            Assert.AreEqual(afterVariantDefine, ForgeBuildWindow.SettingsFingerprint(profiles, null));
        }

        [Test]
        public void TheFingerprint_Changes_WithProjectSettings()
        {
            var profiles = new[] { NewForgeProfile(NewBuildProfile("Quest")) };
            // In memory only: nothing is saved to the host project's ForgeSettings.asset.
            var settings = new SerializedObject(ForgeSettings.instance);
            var mark = settings.FindProperty("markVariantBuilds");
            var previous = mark.boolValue;
            try
            {
                var refreshed = ForgeBuildWindow.SettingsFingerprint(profiles, ForgeSettings.instance);

                mark.boolValue = !previous;
                settings.ApplyModifiedPropertiesWithoutUndo();

                Assert.AreNotEqual(refreshed, ForgeBuildWindow.SettingsFingerprint(profiles, ForgeSettings.instance));
            }
            finally
            {
                // Only the one setting, not a JSON round trip, which would drop settings whose type is missing.
                mark.boolValue = previous;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
