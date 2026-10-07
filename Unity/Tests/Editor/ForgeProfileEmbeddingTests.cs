using System.IO;
using System.Linq;
using System.Reflection;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.Plugins;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    // Build Forge Profiles stored inside Unity Build Profile assets, as Build
    // Profile components.
    public class ForgeProfileEmbeddingTests
    {
        const string TempFolder = "Assets/BuildForgeEmbedTmp";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "BuildForgeEmbedTmp");
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(TempFolder);

        static BuildProfile CreateBuildProfileAsset(string name)
        {
            var factory = typeof(BuildProfile).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .First(m => m.Name == "CreateInstance" && m.GetParameters().Length == 2);
            var profile = (BuildProfile)factory.Invoke(null, new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
            AssetDatabase.CreateAsset(profile, $"{TempFolder}/{name}.asset");
            return profile;
        }

        static string FileText(Object asset) => File.ReadAllText(AssetDatabase.GetAssetPath(asset));

        [Test]
        public void Add_StoresTheProfileInsideTheBuildProfileAsset()
        {
            var buildProfile = CreateBuildProfileAsset("Quest");

            var profile = ForgeProfileEmbedding.Add(buildProfile);

            Assert.IsTrue(profile.IsEmbedded);
            Assert.AreSame(buildProfile, profile.BuildProfile);
            Assert.IsFalse(AssetDatabase.IsMainAsset(profile));
            Assert.AreEqual(AssetDatabase.GetAssetPath(buildProfile), AssetDatabase.GetAssetPath(profile));
            Assert.AreEqual("Quest", profile.DisplayName);
            Assert.AreEqual("Quest", profile.ProfileName, "An embedded profile has no asset name of its own.");
            StringAssert.Contains("m_Name: " + ForgeProfileEmbedding.ComponentName, FileText(buildProfile));
            Assert.AreSame(profile, ForgeProfileEmbedding.Add(buildProfile), "Adding twice keeps the stored profile.");
        }

        [Test]
        public void SavingAnEditedEmbeddedProfile_WritesTheBuildProfileFile()
        {
            var buildProfile = CreateBuildProfileAsset("Quest");
            var profile = ForgeProfileEmbedding.Add(buildProfile);

            profile.SetPluginConfig("BuildNumber", new BuildNumberConfig { Enabled = true, Offset = 7 });
            AssetDatabase.SaveAssetIfDirty(profile);

            StringAssert.Contains("offset: 7", FileText(buildProfile));
        }

        [Test]
        public void Lookup_FindsEmbeddedProfiles_AndResolvesTheirBuildProfile()
        {
            var buildProfile = CreateBuildProfileAsset("Quest");
            var profile = ForgeProfileEmbedding.Add(buildProfile);

            var all = ForgeProfileLookup.FindAll();

            Assert.That(all, Has.Member(profile));
            Assert.AreSame(profile, ForgeProfileLookup.ResolveForBuildProfile(buildProfile, all, out var error));
            Assert.IsNull(error);
        }

        // Unity's Duplicate copies the component; the copy belongs to the
        // duplicate, so the 1:1 rule holds without any fix-up.
        [Test]
        public void DuplicatedBuildProfile_OwnsItsCopyOfTheSettings()
        {
            var buildProfile = CreateBuildProfileAsset("Quest");
            var profile = ForgeProfileEmbedding.Add(buildProfile);
            profile.SetPluginConfig("BuildNumber", new BuildNumberConfig { Enabled = true, Offset = 3 });
            AssetDatabase.SaveAssetIfDirty(profile);

            Assert.IsTrue(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(buildProfile), $"{TempFolder}/Quest Copy.asset"));
            var copy = AssetDatabase.LoadAssetAtPath<BuildProfile>($"{TempFolder}/Quest Copy.asset");
            var copied = ForgeProfileEmbedding.Get(copy);

            Assert.IsNotNull(copied);
            Assert.AreNotSame(profile, copied);
            Assert.AreSame(copy, copied.BuildProfile);
            Assert.AreEqual(3, copied.GetPluginConfig<BuildNumberConfig>("BuildNumber").Offset);
            var all = ForgeProfileLookup.FindAll();
            Assert.IsNull(ForgeProfileLookup.SharedBuildProfileError(profile, all));
            Assert.IsNull(ForgeProfileLookup.SharedBuildProfileError(copied, all));
        }

        [Test]
        public void Remove_DeletesTheStoredSettings()
        {
            var buildProfile = CreateBuildProfileAsset("Quest");
            ForgeProfileEmbedding.Add(buildProfile);

            ForgeProfileEmbedding.Remove(buildProfile);

            Assert.IsNull(ForgeProfileEmbedding.Get(buildProfile));
            StringAssert.DoesNotContain("m_Name: " + ForgeProfileEmbedding.ComponentName, FileText(buildProfile));
        }

        [Test]
        public void BuildWindow_OpensTheBuildProfileInTheInspector()
        {
            var buildProfile = CreateBuildProfileAsset("Quest");
            var profile = ForgeProfileEmbedding.Add(buildProfile);
            Assert.AreSame(buildProfile, BuildForge.Editor.UI.ForgeBuildWindow.InspectorTarget(profile));

            var madeInCode = ScriptableObject.CreateInstance<ForgeProfile>();
            Assert.AreSame(madeInCode, BuildForge.Editor.UI.ForgeBuildWindow.InspectorTarget(madeInCode));
            Object.DestroyImmediate(madeInCode);
        }
    }
}
