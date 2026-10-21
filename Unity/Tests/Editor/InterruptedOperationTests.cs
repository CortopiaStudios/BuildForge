using System;
using System.Linq;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// An Apply or a build that did not finish leaves recorded editor settings in an
    /// unknown state. Builds, and Apply and Activate of another profile, stop on it
    /// before they change anything; only Revert to Baseline or Re-apply of the applied
    /// profile undo it, because they say what they restore.
    /// </summary>
    public class InterruptedOperationTests
    {
        const string Key = "BuildForge.Tests.RemovedPlugin|Android";
        string previousState;
        string previousSettings;

        [SetUp] public void SetUp()
        {
            previousState = EditorJsonUtility.ToJson(ForgeEditorStateStore.instance);
            previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var store = ForgeEditorStateStore.instance;
            store.EditorState.Clear();
            store.EditorState.Add(new EditorStateEntry
            {
                key = Key, baselineJson = "baseline", expectedJson = "baseline", phase = EditorStatePhase.Applying
            });
            store.AppliedBuildProfileGuid = "0123456789abcdef0123456789abcdef"; // no such asset
            store.AppliedVariant = "";
            ForgeEditorState.InvalidateDriftCache();
            var settings = new SerializedObject(ForgeSettings.instance);
            settings.FindProperty("buildVariants").arraySize = 0;
            settings.FindProperty("variantRules").arraySize = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown] public void TearDown()
        {
            EditorJsonUtility.FromJsonOverwrite(previousState, ForgeEditorStateStore.instance);
            ForgeEditorStateStore.instance.SaveState();
            EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
            ForgeEditorState.InvalidateDriftCache();
        }

        [Test]
        public void InterruptedError_NamesTheSettingsWhoseApplyDidNotFinish()
        {
            var error = ForgeEditorState.InterruptedError();
            StringAssert.Contains("did not finish for BuildForge.Tests.RemovedPlugin (Android)", error);

            ForgeEditorStateStore.instance.EditorState[0].phase = EditorStatePhase.Applied;
            Assert.IsNull(ForgeEditorState.InterruptedError(), "A finished Apply is not interrupted.");
        }

        [Test]
        public void ABuild_AndApplyAndActivateOfAnotherProfile_StopBeforeTheyChangeAnything()
        {
            var native = VariantRulesTests.CreateNativeProfile();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var fp = new SerializedObject(forge);
                fp.FindProperty("buildProfile").objectReferenceValue = native;
                fp.ApplyModifiedPropertiesWithoutUndo();
                var before = EditorJsonUtility.ToJson(native);
                var ledger = EditorJsonUtility.ToJson(ForgeEditorStateStore.instance);

                StringAssert.Contains("did not finish", Assert.Throws<InvalidOperationException>(() => ForgeBuildRunner.RunBuild(forge)).Message);
                StringAssert.Contains("did not finish", Assert.Throws<InvalidOperationException>(() => ForgeEditorState.Apply(forge)).Message);
                StringAssert.Contains("did not finish", Assert.Throws<InvalidOperationException>(() => ForgeEditorState.Activate(forge)).Message);

                Assert.That(EditorJsonUtility.ToJson(native), Is.EqualTo(before));
                Assert.That(EditorJsonUtility.ToJson(ForgeEditorStateStore.instance), Is.EqualTo(ledger));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(forge);
                UnityEngine.Object.DestroyImmediate(native);
            }
        }

        [Test]
        public void DismissDrift_KeepsReportingTheUnfinishedEntry()
        {
            ForgeEditorState.DismissDrift();

            CollectionAssert.Contains(ForgeEditorState.GetDrift(force: true), Key + EditorStateApplier.ApplyUnfinishedSuffix);
        }

        // The ways out say which settings they restore whole. This entry's plugin is gone,
        // so the restore itself fails after the warning, which is all these tests need.
        [Test]
        public void RevertToBaseline_WarnsWhichSettingsItRestoresWhole()
        {
            LogAssert.Expect(LogType.Warning, new Regex(
                @"Reverting restores the settings whose Apply or build did not finish .*: BuildForge\.Tests\.RemovedPlugin \(Android\)\."));
            LogAssert.Expect(LogType.Warning, new Regex(@"RemovedPlugin\|Android: plugin not found"));

            var failure = Assert.Throws<AggregateException>(ForgeEditorState.RevertToBaseline);

            Assert.IsInstanceOf<EditorStateRestoreException>(failure.InnerExceptions.Single());
        }

        [Test]
        public void ReapplyOfTheAppliedProfile_GetsPastTheStop_AndWarnsWhatItRestoresWhole()
        {
            var name = "BuildForgeInterruptedTest-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            var folder = "Assets/" + name;
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                AssetDatabase.CreateAsset(VariantRulesTests.CreateNativeProfile(), folder + "/Native.asset");
                var fp = new SerializedObject(forge);
                fp.FindProperty("buildProfile").objectReferenceValue = AssetDatabase.LoadMainAssetAtPath(folder + "/Native.asset");
                fp.ApplyModifiedPropertiesWithoutUndo();
                ForgeEditorStateStore.instance.AppliedBuildProfileGuid = AssetDatabase.AssetPathToGUID(folder + "/Native.asset");
                Assert.IsTrue(ForgeEditorState.IsApplied(forge));
                LogAssert.Expect(LogType.Warning, new Regex(
                    @"Re-applying '.*' restores the settings whose Apply or build did not finish .*: BuildForge\.Tests\.RemovedPlugin \(Android\)\."));
                LogAssert.Expect(LogType.Warning, new Regex(@"RemovedPlugin\|Android: plugin not found"));

                Assert.Throws<AggregateException>(() => ForgeEditorState.Apply(forge));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(forge);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
