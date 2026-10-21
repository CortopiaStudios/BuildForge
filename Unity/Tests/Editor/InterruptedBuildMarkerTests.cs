using System;
using System.IO;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// A build that did not finish, because the editor quit or a step that puts settings
    /// back failed, stops the next build until it is cleared: the project files may still
    /// hold what it changed, and a build would start from them and keep them.
    /// </summary>
    public class InterruptedBuildMarkerTests
    {
        string previous;

        [SetUp] public void SetUp()
        {
            previous = File.Exists(InterruptedBuildMarker.FilePath) ? File.ReadAllText(InterruptedBuildMarker.FilePath) : null;
            InterruptedBuildMarker.Clear();
        }

        [TearDown] public void TearDown()
        {
            InterruptedBuildMarker.Clear();
            if (previous != null)
                File.WriteAllText(InterruptedBuildMarker.FilePath, previous);
        }

        [Test]
        public void ABuildThatPutEverythingBack_LeavesNothing()
        {
            Assert.IsTrue(InterruptedBuildMarker.Begin("Quest", "Internal"));
            Assert.IsNull(InterruptedBuildMarker.Error(), "The build that began it doesn't stop on it.");

            InterruptedBuildMarker.Finish(true, true);

            Assert.IsFalse(File.Exists(InterruptedBuildMarker.FilePath));
            Assert.IsNull(InterruptedBuildMarker.Error());
        }

        [Test]
        public void AFailedRestore_KeepsIt_AndTheMessageNamesTheBuild()
        {
            InterruptedBuildMarker.Begin("Quest", "Internal");

            InterruptedBuildMarker.Finish(true, false);

            var error = InterruptedBuildMarker.Error();
            StringAssert.StartsWith("A Build Forge build of 'Quest' (variant Internal), started ", error);
            StringAssert.Contains("did not finish", error);
            StringAssert.Contains(InterruptedBuildMarker.FilePath, error);
        }

        // The command line begins it before its version overrides; the build it runs reports
        // its own restores to it, and the file stays when either failed.
        [Test]
        public void InsideTheCommandLinesSpan_TheBuildReportsToIt()
        {
            Assert.IsTrue(InterruptedBuildMarker.Begin("Quest", null));
            Assert.IsFalse(InterruptedBuildMarker.Begin("Quest", null), "The build inside doesn't begin its own.");

            InterruptedBuildMarker.Finish(false, false);
            Assert.IsTrue(File.Exists(InterruptedBuildMarker.FilePath));
            Assert.IsNull(InterruptedBuildMarker.Error(), "Still the command line's span.");
            InterruptedBuildMarker.Finish(true, true);

            StringAssert.Contains("did not finish", InterruptedBuildMarker.Error());
        }

        [Test]
        public void AnUnreadableFile_StillStopsBuilds()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(InterruptedBuildMarker.FilePath));
            File.WriteAllText(InterruptedBuildMarker.FilePath, "not json");

            StringAssert.StartsWith("A Build Forge build did not finish", InterruptedBuildMarker.Error());
        }

        [Test]
        public void ABuild_StopsBeforeItStarts_WhileOneDidNotFinish()
        {
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var native = VariantRulesTests.CreateNativeProfile();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var settings = new SerializedObject(ForgeSettings.instance);
                settings.FindProperty("buildVariants").arraySize = 0;
                settings.FindProperty("variantRules").arraySize = 0;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var fp = new SerializedObject(forge);
                fp.FindProperty("buildProfile").objectReferenceValue = native;
                fp.ApplyModifiedPropertiesWithoutUndo();
                InterruptedBuildMarker.Begin("Quest", null);
                InterruptedBuildMarker.Finish(true, false);
                var before = EditorJsonUtility.ToJson(native);

                var message = Assert.Throws<InvalidOperationException>(() => ForgeBuildRunner.RunBuild(forge)).Message;

                StringAssert.Contains("A Build Forge build of 'Quest'", message);
                Assert.That(EditorJsonUtility.ToJson(native), Is.EqualTo(before));
                Assert.IsTrue(File.Exists(InterruptedBuildMarker.FilePath), "Only Clear removes it.");
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                UnityEngine.Object.DestroyImmediate(forge);
                UnityEngine.Object.DestroyImmediate(native);
            }
        }
    }
}
