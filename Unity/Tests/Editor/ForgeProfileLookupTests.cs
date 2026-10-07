using System.Collections.Generic;
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
    /// The 1:1 rule: a Unity Build Profile belongs to exactly one Build Forge
    /// Profile, so the active Build Profile identifies the Build Forge Profile
    /// (CI resolution) and sharing is an error in the UI.
    /// </summary>
    public class ForgeProfileLookupTests
    {
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
            // Internal factory (present since Unity 6000.0); tests need in-memory
            // Build Profiles without touching the asset database.
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

        ForgeProfile NewForgeProfile(string name, BuildProfile buildProfile)
        {
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            profile.name = name;
            if (buildProfile != null)
            {
                var so = new SerializedObject(profile);
                so.FindProperty("buildProfile").objectReferenceValue = buildProfile;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            created.Add(profile);
            return profile;
        }

        [Test]
        public void Resolve_ExactlyOne_ReturnsIt()
        {
            var bp = NewBuildProfile("Quest");
            var other = NewBuildProfile("Pico");
            var quest = NewForgeProfile("QuestForge", bp);
            var all = new List<ForgeProfile> { NewForgeProfile("PicoForge", other), quest };

            var resolved = ForgeProfileLookup.ResolveForBuildProfile(bp, all, out var error);

            Assert.AreSame(quest, resolved);
            Assert.IsNull(error);
        }

        [Test]
        public void Resolve_None_IsAnError()
        {
            var bp = NewBuildProfile("Quest");
            var all = new List<ForgeProfile> { NewForgeProfile("PicoForge", NewBuildProfile("Pico")) };

            var resolved = ForgeProfileLookup.ResolveForBuildProfile(bp, all, out var error);

            Assert.IsNull(resolved);
            StringAssert.Contains("has no Build Forge settings", error);
            StringAssert.Contains("Quest", error);
        }

        [Test]
        public void Resolve_Shared_IsAnError_NeverASilentPick()
        {
            var bp = NewBuildProfile("Quest");
            var all = new List<ForgeProfile>
            {
                NewForgeProfile("QuestDev", bp),
                NewForgeProfile("QuestRelease", bp),
            };

            var resolved = ForgeProfileLookup.ResolveForBuildProfile(bp, all, out var error);

            Assert.IsNull(resolved);
            StringAssert.Contains("2 sets of Build Forge settings", error);
            StringAssert.Contains("QuestDev", error);
            StringAssert.Contains("QuestRelease", error);
        }

        [Test]
        public void Resolve_NoActiveBuildProfile_IsAnError()
        {
            var resolved = ForgeProfileLookup.ResolveForBuildProfile(null, new List<ForgeProfile>(), out var error);

            Assert.IsNull(resolved);
            StringAssert.Contains("No Unity Build Profile is active", error);
        }

        [Test]
        public void SharedError_NullForUniqueOrUnassigned()
        {
            var bp = NewBuildProfile("Quest");
            var quest = NewForgeProfile("QuestForge", bp);
            var unassignedA = NewForgeProfile("A", null);
            var unassignedB = NewForgeProfile("B", null);
            var all = new List<ForgeProfile> { quest, unassignedA, unassignedB };

            Assert.IsNull(ForgeProfileLookup.SharedBuildProfileError(quest, all));
            Assert.IsNull(ForgeProfileLookup.SharedBuildProfileError(unassignedA, all),
                "Two profiles without a Build Profile do not share one.");
            Assert.IsEmpty(ForgeProfileLookup.FindSharing(quest, all));
        }

        [Test]
        public void SharedError_NamesTheOtherProfiles_NotItself()
        {
            var bp = NewBuildProfile("Quest");
            var dev = NewForgeProfile("QuestDev", bp);
            var release = NewForgeProfile("QuestRelease", bp);
            var all = new List<ForgeProfile> { dev, release, NewForgeProfile("PicoForge", NewBuildProfile("Pico")) };

            var error = ForgeProfileLookup.SharedBuildProfileError(dev, all);

            Assert.IsNotNull(error);
            StringAssert.Contains("'QuestRelease'", error);
            StringAssert.DoesNotContain("'QuestDev'", error);
            StringAssert.DoesNotContain("PicoForge", error);
            CollectionAssert.AreEqual(new[] { release }, ForgeProfileLookup.FindSharing(dev, all));
        }
    }
}
