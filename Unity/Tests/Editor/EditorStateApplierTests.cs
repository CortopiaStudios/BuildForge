using System;
using System.Collections.Generic;
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
    /// Locks the editor-state invariant (applied = baseline + one profile's
    /// deltas) on a fake plugin with an in-memory "world" per target group.
    /// </summary>
    public class EditorStateApplierTests
    {
        /// <summary>
        /// World state per group is a string. Applying a profile sets the world to
        /// "<current>+<profile name>" so stacking is observable; a snapshot is the
        /// world string itself.
        /// </summary>
        class FakePlugin : IForgeEditorApplicable
        {
            public readonly Dictionary<BuildTargetGroup, string> World = new();
            public readonly List<string> Calls = new();
            public Func<BuildTargetGroup, bool> HasState = _ => true;
            public bool ThrowOnApply;
            public BuildTargetGroup? ThrowOnApplyGroup;
            public BuildTargetGroup? ThrowOnRestoreGroup;
            public BuildTargetGroup? ThrowOnCaptureGroup;

            public string CaptureEditorState(BuildTargetGroup group)
            {
                Calls.Add($"capture:{group}");
                if (ThrowOnCaptureGroup == group)
                    throw new InvalidOperationException("capture boom");
                if (!HasState(group)) return null;
                return World.TryGetValue(group, out var w) ? w : "base";
            }

            public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group)
            {
                Calls.Add($"apply:{forgeProfile.name}:{group}");
                if (ThrowOnApply || ThrowOnApplyGroup == group)
                    throw new InvalidOperationException("boom");
                var current = World.TryGetValue(group, out var w) ? w : "base";
                World[group] = current + "+" + forgeProfile.name;
            }

            public void RestoreEditorState(BuildTargetGroup group, string json)
            {
                Calls.Add($"restore:{group}:{json}");
                if (ThrowOnRestoreGroup == group)
                    throw new InvalidOperationException("restore boom");
                World[group] = json;
            }
        }

        const string PluginName = "Fake";
        FakePlugin plugin;
        List<EditorStateEntry> entries;
        ForgeProfile profileA, profileB;
        int checkpoints;

        Func<string, IForgeEditorApplicable> Resolve => name => name == PluginName ? plugin : null;

        [SetUp]
        public void SetUp()
        {
            plugin = new FakePlugin();
            entries = new List<EditorStateEntry>();
            profileA = ScriptableObject.CreateInstance<ForgeProfile>();
            profileA.name = "A";
            profileB = ScriptableObject.CreateInstance<ForgeProfile>();
            profileB.name = "B";
            checkpoints = 0;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(profileA);
            UnityEngine.Object.DestroyImmediate(profileB);
        }

        EditorStatePair Pair(BuildTargetGroup group) =>
            new EditorStatePair { PluginTypeName = PluginName, Group = group, Plugin = plugin };

        void Apply(ForgeProfile profile, params BuildTargetGroup[] groups)
        {
            EditorStateApplier.Apply(entries, groups.Select(Pair).ToList(), profile, Resolve, () => checkpoints++);
        }

        [Test]
        public void FirstApply_CapturesBaselineAppliesAndCapturesExpected()
        {
            Apply(profileA, BuildTargetGroup.Android);

            CollectionAssert.AreEqual(
                new[] { "capture:Android", "apply:A:Android", "capture:Android" }, plugin.Calls);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("Fake|Android", entries[0].key);
            Assert.AreEqual("base", entries[0].baselineJson);
            Assert.AreEqual("base+A", entries[0].expectedJson);
            Assert.AreEqual("base+A", plugin.World[BuildTargetGroup.Android]);
            Assert.AreEqual(3, checkpoints, "after revert, after the baseline is recorded, after the last pair");
        }

        [Test]
        public void Apply_CheckpointsBaselineBeforePluginWrites()
        {
            var seen = new List<(int count, string expected, string world)>();
            EditorStateApplier.Apply(entries, new[] { Pair(BuildTargetGroup.Android) }, profileA, Resolve, () =>
                seen.Add((entries.Count,
                    entries.Count > 0 ? entries[0].expectedJson : null,
                    plugin.World.TryGetValue(BuildTargetGroup.Android, out var w) ? w : "base")));

            Assert.AreEqual(3, seen.Count);
            Assert.AreEqual((0, (string)null, "base"), seen[0], "after revert: nothing recorded, nothing written");
            Assert.AreEqual((1, "base", "base"), seen[1], "baseline recorded and persisted before the plugin writes");
            Assert.AreEqual((1, "base+A", "base+A"), seen[2], "final: expected state recorded");
        }

        [Test]
        public void ApplyB_AfterA_RevertsABeforeCapturingBsBaseline()
        {
            Apply(profileA, BuildTargetGroup.Android);
            plugin.Calls.Clear();

            Apply(profileB, BuildTargetGroup.Android);

            CollectionAssert.AreEqual(
                new[] { "restore:Android:base", "capture:Android", "apply:B:Android", "capture:Android" }, plugin.Calls);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("base", entries[0].baselineJson, "B's baseline must be the pre-A state, not A's state.");
            Assert.AreEqual("base+B", plugin.World[BuildTargetGroup.Android], "No stacking.");
        }

        [Test]
        public void ApplyA_Twice_IsIdempotent()
        {
            Apply(profileA, BuildTargetGroup.Android);
            var first = entries[0].expectedJson;

            Apply(profileA, BuildTargetGroup.Android);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(first, entries[0].expectedJson);
            Assert.AreEqual("base+A", plugin.World[BuildTargetGroup.Android]);
        }

        [Test]
        public void Apply_ZeroPairs_StillRevertsPrevious()
        {
            Apply(profileA, BuildTargetGroup.Android);

            EditorStateApplier.Apply(entries, new List<EditorStatePair>(), profileB, Resolve, () => checkpoints++);

            Assert.IsEmpty(entries);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
        }

        [Test]
        public void Apply_NullCapture_SkipsPair()
        {
            plugin.HasState = g => g != BuildTargetGroup.Standalone;

            Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("Fake|Android", entries[0].key);
            Assert.IsFalse(plugin.Calls.Contains("apply:A:Standalone"));
        }

        [Test]
        public void Apply_PluginThrows_OtherPairsStillAppliedAndEntryRecorded()
        {
            plugin.ThrowOnApply = true;
            LogAssert.Expect(LogType.Error, new Regex("apply failed"));
            LogAssert.Expect(LogType.Error, new Regex("apply failed"));

            Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone);

            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual("base", entries[0].expectedJson, "Expected reflects the actual (unchanged) state.");
        }

        [Test]
        public void RevertAll_RestoresInReverseOrderAndClears()
        {
            Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone);
            plugin.Calls.Clear();

            EditorStateApplier.RevertAll(entries, Resolve);

            CollectionAssert.AreEqual(new[] { "restore:Standalone:base", "restore:Android:base" }, plugin.Calls);
            Assert.IsEmpty(entries);
        }

        [Test]
        public void RevertAll_UnresolvablePlugin_WarnsAndRetainsBaseline()
        {
            Apply(profileA, BuildTargetGroup.Android);
            entries.Add(new EditorStateEntry { key = "Gone|Android", baselineJson = "x", expectedJson = "x" });
            LogAssert.Expect(LogType.Warning, new Regex("Gone\\|Android: plugin not found"));

            var failure = Assert.Throws<AggregateException>(() => EditorStateApplier.RevertAll(entries, Resolve));

            Assert.AreEqual(1, failure.InnerExceptions.Count);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("Gone|Android", entries[0].key);
            Assert.AreEqual("x", entries[0].baselineJson);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
        }

        [Test]
        public void RevertAll_RestoreFails_OtherEntriesRestoreAndFailedBaselineCanBeRetried()
        {
            Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone);
            plugin.ThrowOnRestoreGroup = BuildTargetGroup.Standalone;
            LogAssert.Expect(LogType.Error, new Regex("restore failed"));

            Assert.Throws<AggregateException>(() => EditorStateApplier.RevertAll(entries, Resolve));

            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
            Assert.AreEqual("base+A", plugin.World[BuildTargetGroup.Standalone]);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("Fake|Standalone", entries[0].key);
            Assert.AreEqual("base", entries[0].baselineJson);
            Assert.AreEqual("base+A", entries[0].expectedJson);

            plugin.ThrowOnRestoreGroup = null;
            EditorStateApplier.RevertAll(entries, Resolve);

            Assert.IsEmpty(entries);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Standalone]);
        }

        [Test]
        public void Apply_PreviousRestoreFails_DoesNotApplyOrRecordNewProfile()
        {
            Apply(profileA, BuildTargetGroup.Android);
            checkpoints = 0;
            plugin.Calls.Clear();
            plugin.ThrowOnRestoreGroup = BuildTargetGroup.Android;
            LogAssert.Expect(LogType.Error, new Regex("restore failed"));

            Assert.Throws<AggregateException>(() => Apply(profileB, BuildTargetGroup.Android));

            Assert.AreEqual(0, checkpoints);
            CollectionAssert.AreEqual(new[] { "restore:Android:base" }, plugin.Calls);
            Assert.AreEqual("base", entries[0].baselineJson);
            Assert.AreEqual("base+A", entries[0].expectedJson);
            Assert.AreEqual("base+A", plugin.World[BuildTargetGroup.Android]);
        }

        [Test]
        public void Drift_EmptyAfterApply_ListedAfterMutation_ClearedByAccept()
        {
            Apply(profileA, BuildTargetGroup.Android);
            Assert.IsEmpty(EditorStateApplier.Drift(entries, Resolve));

            plugin.World[BuildTargetGroup.Android] = "base+A+hand-edit";
            CollectionAssert.AreEqual(new[] { "Fake|Android" }, EditorStateApplier.Drift(entries, Resolve));

            Assert.IsTrue(EditorStateApplier.DismissDrift(entries, Resolve));
            Assert.IsEmpty(EditorStateApplier.Drift(entries, Resolve));
            Assert.AreEqual("base", entries[0].baselineJson, "Accept must not touch the baseline.");
            Assert.IsFalse(EditorStateApplier.DismissDrift(entries, Resolve), "Nothing to accept the second time.");
        }

        [Test]
        public void Drift_ReapplyReturnsToBaselinePlusDelta_NotAcceptedState()
        {
            Apply(profileA, BuildTargetGroup.Android);
            plugin.World[BuildTargetGroup.Android] = "base+A+hand-edit";

            Apply(profileA, BuildTargetGroup.Android);

            Assert.AreEqual("base+A", plugin.World[BuildTargetGroup.Android]);
            Assert.IsEmpty(EditorStateApplier.Drift(entries, Resolve));
        }

        [Test]
        public void Drift_MissingPlugin_ReportedWithSuffix()
        {
            entries.Add(new EditorStateEntry { key = "Gone|Android", baselineJson = "x", expectedJson = "x" });
            CollectionAssert.AreEqual(new[] { "Gone|Android (plugin missing)" }, EditorStateApplier.Drift(entries, Resolve));
        }

        [Test]
        public void Suspend_RestoresBaselineKeepsEntries_Resume_ReappliesAndRecaptures()
        {
            Apply(profileA, BuildTargetGroup.Android);

            EditorStateApplier.SuspendForBuild(entries, Resolve);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
            Assert.AreEqual(1, entries.Count);

            var result = EditorStateApplier.ResumeAfterBuild(entries, profileA, Resolve);
            Assert.AreEqual("base+A", plugin.World[BuildTargetGroup.Android]);
            Assert.IsFalse(result.Changed, "Same state as before the build — nothing to save.");
            Assert.IsFalse(result.Cleared);
            Assert.IsEmpty(EditorStateApplier.Drift(entries, Resolve));
        }

        [Test]
        public void Resume_WithNullProfile_ClearsEntriesAndSignals()
        {
            Apply(profileA, BuildTargetGroup.Android);
            EditorStateApplier.SuspendForBuild(entries, Resolve);

            var result = EditorStateApplier.ResumeAfterBuild(entries, null, Resolve);

            Assert.IsTrue(result.Cleared);
            Assert.IsEmpty(entries);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
        }

        [Test]
        public void Suspend_RestoreFailure_ThrowsAfterOtherEntriesAndRetainsRecoveryState()
        {
            Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone);
            plugin.ThrowOnRestoreGroup = BuildTargetGroup.Standalone;
            LogAssert.Expect(LogType.Error, new Regex("Standalone: restore failed"));

            var error = Assert.Throws<AggregateException>(
                () => EditorStateApplier.SuspendForBuild(entries, Resolve));

            Assert.AreEqual(1, error.InnerExceptions.Count);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android], "Continue restoring the other entries.");
            Assert.AreEqual("base+A", plugin.World[BuildTargetGroup.Standalone]);
            Assert.AreEqual(2, entries.Count);
            Assert.IsTrue(entries.All(e => e.baselineJson == "base" && e.expectedJson == "base+A"));

            plugin.ThrowOnRestoreGroup = null;
            EditorStateApplier.SuspendForBuild(entries, Resolve);
            Assert.IsEmpty(EditorStateApplier.ResumeAfterBuild(entries, profileA, Resolve).Failures);
            Assert.IsEmpty(EditorStateApplier.Drift(entries, Resolve), "A later retry can still recover from the ledger.");
        }

        [Test]
        public void RestoreFailures_NameTheirEntryAndReason()
        {
            Apply(profileA, BuildTargetGroup.Standalone);
            plugin.ThrowOnRestoreGroup = BuildTargetGroup.Standalone;
            entries.Add(new EditorStateEntry { key = "Gone|Android", baselineJson = "x", expectedJson = "x" });
            LogAssert.Expect(LogType.Warning, new Regex("Gone\\|Android: plugin not found"));
            LogAssert.Expect(LogType.Error, new Regex("Fake\\|Standalone: restore failed"));

            var failure = Assert.Throws<AggregateException>(() => EditorStateApplier.RevertAll(entries, Resolve));

            var reasons = failure.InnerExceptions.Cast<EditorStateRestoreException>().ToDictionary(u => u.Key, u => u.Reason);
            Assert.AreEqual("plugin not found; cannot restore its settings.", reasons["Gone|Android"]);
            Assert.AreEqual("restore failed. restore boom", reasons["Fake|Standalone"]);
        }

        [Test]
        public void Suspend_MissingPlugin_FailsAndKeepsBaseline()
        {
            entries.Add(new EditorStateEntry
            {
                key = "Gone|Android",
                baselineJson = "baseline",
                expectedJson = "applied"
            });
            LogAssert.Expect(LogType.Warning, new Regex("Gone\\|Android: plugin not found"));

            Assert.Throws<AggregateException>(() => EditorStateApplier.SuspendForBuild(entries, Resolve));

            Assert.AreEqual("baseline", entries.Single().baselineJson);
        }

        [Test]
        public void Resume_ApplyFailure_DoesNotAcceptFailedStateAndContinuesOtherEntries()
        {
            Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone);
            EditorStateApplier.SuspendForBuild(entries, Resolve);
            plugin.ThrowOnApplyGroup = BuildTargetGroup.Android;
            LogAssert.Expect(LogType.Error, new Regex("Android: re-apply after build failed"));

            var result = EditorStateApplier.ResumeAfterBuild(entries, profileB, Resolve);

            Assert.AreEqual(1, result.Failures.Count);
            Assert.IsTrue(result.Changed, "The successfully re-applied entry must still be saved.");
            Assert.AreEqual("base+A", entries[0].expectedJson, "Do not record the failed apply as expected state.");
            Assert.AreEqual("base+B", entries[1].expectedJson);
            Assert.IsTrue(entries.All(e => e.baselineJson == "base"));
            CollectionAssert.AreEqual(new[] { "Fake|Android" }, EditorStateApplier.Drift(entries, Resolve));
        }

        [Test]
        public void Resume_CaptureFailure_ReportsFailureWithoutReplacingExpectedState()
        {
            Apply(profileA, BuildTargetGroup.Android);
            EditorStateApplier.SuspendForBuild(entries, Resolve);
            plugin.ThrowOnCaptureGroup = BuildTargetGroup.Android;
            LogAssert.Expect(LogType.Error, new Regex("re-apply after build failed"));

            var result = EditorStateApplier.ResumeAfterBuild(entries, profileB, Resolve);

            Assert.AreEqual(1, result.Failures.Count);
            Assert.IsFalse(result.Changed);
            Assert.AreEqual("base+A", entries.Single().expectedJson);
        }

        [Test]
        public void Resume_MissingPlugin_ReportsFailureAndKeepsLedger()
        {
            entries.Add(new EditorStateEntry
            {
                key = "Gone|Android",
                baselineJson = "baseline",
                expectedJson = "applied"
            });

            var result = EditorStateApplier.ResumeAfterBuild(entries, profileA, Resolve);

            Assert.AreEqual(1, result.Failures.Count);
            Assert.AreEqual("baseline", entries.Single().baselineJson);
            Assert.AreEqual("applied", entries.Single().expectedJson);
        }
    }
}
