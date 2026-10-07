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
    /// A recorded setting group whose plugin is gone (its package was removed
    /// after Apply) can never be restored, so it blocks every Apply, Activate,
    /// revert and build until Build Forge stops tracking it.
    /// </summary>
    public class StopTrackingTests
    {
        const string RemovedKey = "BuildForge.Tests.RemovedPlugin|Android";
        string previousState;

        [SetUp] public void SetUp()
        {
            previousState = EditorJsonUtility.ToJson(ForgeEditorStateStore.instance);
            var store = ForgeEditorStateStore.instance;
            store.EditorState.Clear();
            store.EditorState.Add(new EditorStateEntry { key = RemovedKey, baselineJson = "baseline", expectedJson = "applied" });
            store.AppliedBuildProfileGuid = "0123456789abcdef0123456789abcdef"; // no such asset
            store.AppliedVariant = "";
            ForgeEditorState.InvalidateDriftCache();
        }

        [TearDown] public void TearDown()
        {
            EditorJsonUtility.FromJsonOverwrite(previousState, ForgeEditorStateStore.instance);
            ForgeEditorStateStore.instance.SaveState();
            ForgeEditorState.InvalidateDriftCache();
        }

        [Test]
        public void RevertThatCannotRestore_KeepsTheEntry_StopTrackingUnblocksTheEditor()
        {
            LogAssert.Expect(LogType.Warning, new Regex("RemovedPlugin\\|Android: plugin not found"));
            var failure = Assert.Throws<AggregateException>(ForgeEditorState.RevertToBaseline);
            Assert.AreEqual(RemovedKey, failure.InnerExceptions.OfType<EditorStateRestoreException>().Single().Key);
            Assert.IsTrue(ForgeEditorState.HasAppliedGuid, "A failed revert keeps the applied state for a retry.");
            Assert.AreEqual(1, ForgeEditorState.Entries.Count);

            LogAssert.Expect(LogType.Warning, new Regex("Stopped tracking 1 setting group"));
            CollectionAssert.AreEqual(new[] { RemovedKey }, ForgeEditorState.StopTracking());

            Assert.IsEmpty(ForgeEditorState.Entries);
            Assert.IsFalse(ForgeEditorState.HasAppliedGuid);
            var saved = System.IO.File.ReadAllText("UserSettings/ForgeEditorState.asset");
            StringAssert.DoesNotContain(RemovedKey, saved, "The dropped entry must not come back after a restart.");
            Assert.DoesNotThrow(ForgeEditorState.RevertToBaseline, "Nothing left blocks a revert.");
        }
    }
}
