using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.XRManagement;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;

namespace BuildForge.Tests.XRManagement
{
    public class PlayModeXRLoadersTests
    {
        // XR Management 4.7 added settingsKey (same value) and made k_SettingsKey obsolete.
        static readonly string SettingsKey =
#if BUILDFORGE_XR_MANAGEMENT_4_7
            XRGeneralSettings.settingsKey;
#else
            XRGeneralSettings.k_SettingsKey;
#endif
        string folder;
        XRGeneralSettingsPerBuildTarget original, settings;
        XRGeneralSettings android, standalone;
        TestXRLoader questLoader, pcLoader;
        BuildProfile native;
        ForgeProfile profile;
        bool followsAppliedProfile;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/ForgePlayModeTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            EditorBuildSettings.TryGetConfigObject(SettingsKey, out original);
            settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(settings, folder + "/Settings.asset");
            android = AddGroup(BuildTargetGroup.Android);
            standalone = AddGroup(BuildTargetGroup.Standalone);
            EditorBuildSettings.AddConfigObject(SettingsKey, settings, true);

            questLoader = ScriptableObject.CreateInstance<TestXRLoader>();
            pcLoader = ScriptableObject.CreateInstance<TestXRLoader>();
            AssetDatabase.CreateAsset(questLoader, folder + "/Quest.asset");
            AssetDatabase.CreateAsset(pcLoader, folder + "/PC.asset");
            Assert.That(android.Manager.TrySetLoaders(new List<XRLoader> { questLoader }), Is.True);
            android.InitManagerOnStart = true;
            Assert.That(standalone.Manager.TrySetLoaders(new List<XRLoader> { pcLoader }), Is.True);
            standalone.InitManagerOnStart = false;

            native = (BuildProfile)typeof(BuildProfile).GetMethod("CreateInstance", BindingFlags.Static | BindingFlags.NonPublic,
                    null, new[] { typeof(BuildTarget), typeof(StandaloneBuildSubtarget) }, null)
                .Invoke(null, new object[] { BuildTarget.Android, StandaloneBuildSubtarget.Player });
            profile = ScriptableObject.CreateInstance<ForgeProfile>();
            var so = new SerializedObject(profile);
            so.FindProperty("buildProfile").objectReferenceValue = native;
            so.ApplyModifiedPropertiesWithoutUndo();
            followsAppliedProfile = ForgeSettings.instance.PlayModeFollowsAppliedProfile;
        }

        XRGeneralSettings AddGroup(BuildTargetGroup group)
        {
            var general = ScriptableObject.CreateInstance<XRGeneralSettings>();
            var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
            AssetDatabase.AddObjectToAsset(general, settings);
            AssetDatabase.AddObjectToAsset(manager, settings);
            general.Manager = manager;
            settings.SetSettingsForBuildTarget(group, general);
            return general;
        }

        [TearDown]
        public void TearDown()
        {
            ForgeSettings.instance.PlayModeFollowsAppliedProfile = followsAppliedProfile;
            if (original != null)
            {
                EditorBuildSettings.AddConfigObject(SettingsKey, original, true);
                original.SetSettingsForBuildTarget(BuildTargetGroup.Standalone, original.SettingsForBuildTarget(BuildTargetGroup.Standalone));
            }
            else
                EditorBuildSettings.RemoveConfigObject(SettingsKey);
            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(native);
            AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void Mirror_CopiesThePlatformsLoadersAndStartup_RestoreBringsStandaloneBack()
        {
            var baseline = new PlayModeXRLoaders().CaptureEditorState(BuildTargetGroup.Standalone);

            PlayModeXRLoaders.Mirror(BuildTargetGroup.Android, BuildTargetGroup.Standalone);

            CollectionAssert.AreEqual(new XRLoader[] { questLoader }, standalone.Manager.activeLoaders);
            Assert.IsTrue(standalone.InitManagerOnStart);
            CollectionAssert.AreEqual(new XRLoader[] { questLoader }, android.Manager.activeLoaders, "The source is left alone.");

            // A fresh instance restores from the persisted JSON alone.
            new PlayModeXRLoaders().RestoreEditorState(BuildTargetGroup.Standalone, baseline);
            CollectionAssert.AreEqual(new XRLoader[] { pcLoader }, standalone.Manager.activeLoaders);
            Assert.IsFalse(standalone.InitManagerOnStart);
        }

        [Test]
        public void AppliedMirror_IsSuspendedForABuild_ResumedAfterIt_AndReverted()
        {
            var mirror = new PlayModeXRLoaders();
            var pair = new EditorStatePair { PluginTypeName = typeof(PlayModeXRLoaders).FullName, Group = BuildTargetGroup.Standalone, Plugin = mirror };
            IForgeEditorApplicable Resolve(string name) => name == pair.PluginTypeName ? mirror : null;
            var entries = new List<EditorStateEntry>();

            EditorStateApplier.Apply(entries, new[] { pair }, profile, Resolve, null);
            CollectionAssert.AreEqual(new XRLoader[] { questLoader }, standalone.Manager.activeLoaders);

            EditorStateApplier.SuspendForBuild(entries, Resolve);
            CollectionAssert.AreEqual(new XRLoader[] { pcLoader }, standalone.Manager.activeLoaders, "Builds run on the baseline.");

            var result = EditorStateApplier.ResumeAfterBuild(entries, profile, Resolve);
            Assert.IsEmpty(result.Failures);
            CollectionAssert.AreEqual(new XRLoader[] { questLoader }, standalone.Manager.activeLoaders);

            EditorStateApplier.RevertAll(entries, Resolve);
            Assert.IsEmpty(entries);
            CollectionAssert.AreEqual(new XRLoader[] { pcLoader }, standalone.Manager.activeLoaders);
            Assert.IsFalse(standalone.InitManagerOnStart);
        }

        [Test]
        public void ParticipatingPairs_AddTheMirrorsAfterThePlugins_OnlyWithTheSettingOn()
        {
            ForgeSettings.instance.PlayModeFollowsAppliedProfile = true;
            var pairs = ForgeEditorState.GetParticipatingPairs(profile);
            var mirror = pairs.SingleOrDefault(p => p.Plugin is PlayModeXRLoaders);
            Assert.IsNotNull(mirror, "An Android profile gets Play Mode's loaders.");
            Assert.AreEqual(BuildTargetGroup.Standalone, mirror.Group);
            Assert.Greater(pairs.FindIndex(p => p.Plugin is IForgePlayModeMirror),
                pairs.FindLastIndex(p => !(p.Plugin is IForgePlayModeMirror)), "Mirrors copy what the plugins left.");

            ForgeSettings.instance.PlayModeFollowsAppliedProfile = false;
            Assert.IsFalse(ForgeEditorState.GetParticipatingPairs(profile).Any(p => p.Plugin is IForgePlayModeMirror));
        }
    }
}
