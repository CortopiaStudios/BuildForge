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
                new[] { "capture:Android", "restore:Android:base", "capture:Android", "apply:B:Android", "capture:Android" }, plugin.Calls);
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

        // As a throwing plugin stops a build, it stops Apply, and nothing of the
        // Apply is kept: a partly applied profile would look applied.
        [Test]
        public void Apply_PluginThrows_StopsAndRestoresWhatItWrote()
        {
            plugin.ThrowOnApplyGroup = BuildTargetGroup.Standalone;
            LogAssert.Expect(LogType.Error, new Regex(@"Fake\|Standalone: apply failed\. boom"));

            var e = Assert.Throws<EditorStateApplyException>(() =>
                Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone));

            Assert.AreEqual("Fake|Standalone", e.Key);
            Assert.AreEqual("boom", e.Reason);
            Assert.IsEmpty(entries);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android], "The pair applied before the failure is restored.");
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Standalone]);
        }

        [Test]
        public void Apply_CaptureThrows_StopsAndRestoresWhatItWrote()
        {
            plugin.ThrowOnCaptureGroup = BuildTargetGroup.Standalone;
            LogAssert.Expect(LogType.Error, new Regex(@"Fake\|Standalone: capture failed\. capture boom"));

            Assert.Throws<EditorStateApplyException>(() =>
                Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone));

            Assert.IsEmpty(entries);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
            Assert.IsFalse(plugin.Calls.Contains("apply:A:Standalone"));
        }

        [Test]
        public void Apply_PluginThrows_AndARestoreFails_KeepsThatEntryForARetry()
        {
            plugin.ThrowOnApplyGroup = BuildTargetGroup.Standalone;
            plugin.ThrowOnRestoreGroup = BuildTargetGroup.Android;
            LogAssert.Expect(LogType.Error, new Regex(@"Fake\|Standalone: apply failed"));
            LogAssert.Expect(LogType.Error, new Regex(@"Fake\|Android: restore failed"));

            var e = Assert.Throws<AggregateException>(() =>
                Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone));

            Assert.IsTrue(EditorStateApplier.StoppedByPlugin(e, out var stopped));
            Assert.AreEqual("Fake|Standalone", stopped.Key);
            Assert.AreEqual("Fake|Android", e.InnerExceptions.OfType<EditorStateRestoreException>().Single().Key);
            Assert.AreEqual("Fake|Android", entries.Single().key);
            Assert.AreEqual("base", entries.Single().baselineJson);
        }

        // The caller keeps the previous profile applied when its revert fails, but
        // forgets everything when a plugin stopped the Apply; the two must differ.
        [Test]
        public void Apply_PreviousProfileFailsToRevert_IsNotStoppedByPlugin()
        {
            Apply(profileA, BuildTargetGroup.Android);
            plugin.ThrowOnRestoreGroup = BuildTargetGroup.Android;
            LogAssert.Expect(LogType.Error, new Regex(@"Fake\|Android: restore failed"));

            var e = Assert.Throws<AggregateException>(() => Apply(profileB, BuildTargetGroup.Android));

            Assert.IsFalse(EditorStateApplier.StoppedByPlugin(e, out _));
            Assert.AreEqual("base+A", entries.Single().expectedJson);
        }

        [Test]
        public void RevertAll_RestoresInReverseOrderAndClears()
        {
            Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone);
            plugin.Calls.Clear();

            EditorStateApplier.RevertAll(entries, Resolve);

            CollectionAssert.AreEqual(
                new[] { "capture:Standalone", "restore:Standalone:base", "capture:Android", "restore:Android:base" }, plugin.Calls,
                "Each restore first captures, to see whether the settings changed after the Apply.");
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
            CollectionAssert.AreEqual(new[] { "capture:Android", "restore:Android:base" }, plugin.Calls);
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

            EditorStateApplier.SuspendForBuild(entries, Resolve, profileA, null);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(EditorStatePhase.Suspended, entries[0].phase, "Until the profile is applied again after the build.");

            var result = EditorStateApplier.ResumeAfterBuild(entries, profileA, Resolve);
            Assert.AreEqual("base+A", plugin.World[BuildTargetGroup.Android]);
            Assert.AreEqual(EditorStatePhase.Applied, entries[0].phase);
            Assert.IsTrue(result.Changed, "Same state as before the build, but the entry is Applied again, which must be saved.");
            Assert.IsFalse(result.Cleared);
            Assert.IsEmpty(EditorStateApplier.Drift(entries, Resolve));
        }

        [Test]
        public void Resume_WithNullProfile_ClearsEntriesAndSignals()
        {
            Apply(profileA, BuildTargetGroup.Android);
            EditorStateApplier.SuspendForBuild(entries, Resolve, profileA, null);

            var result = EditorStateApplier.ResumeAfterBuild(entries, null, Resolve);

            Assert.IsTrue(result.Cleared);
            Assert.IsEmpty(entries);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
        }

        // Without the applied profile nothing is applied again, but a group the build did
        // not put back keeps its entry and baseline, so Revert to Baseline can still undo it.
        [Test]
        public void Resume_WithNullProfile_KeepsWhatTheBuildDidNotPutBack()
        {
            Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone);
            EditorStateApplier.SuspendForBuild(entries, Resolve, profileA, null);
            plugin.World[BuildTargetGroup.Android] = "built";

            var result = EditorStateApplier.ResumeAfterBuild(entries, null, Resolve, null,
                new[] { EditorStateLedger.MakeKey(PluginName, BuildTargetGroup.Android) });

            Assert.IsFalse(result.Cleared, "The applied profile's traces stay while an entry does.");
            Assert.IsTrue(result.Changed);
            StringAssert.Contains("stays recorded for Revert to Baseline", result.Failures.Single().Message);
            Assert.AreEqual("Fake|Android", entries.Single().key);
            Assert.AreEqual(EditorStatePhase.Suspended, entries.Single().phase);
            EditorStateApplier.RevertAll(entries, Resolve);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
        }

        // An entry that can't be restored stops the build, after the others were restored; those
        // get the applied profile's settings again, so the stopped build leaves the editor as it was.
        [Test]
        public void Suspend_RestoreFailure_ReappliesTheRestoredEntries_AndThrows()
        {
            Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone);
            plugin.ThrowOnRestoreGroup = BuildTargetGroup.Standalone;
            LogAssert.Expect(LogType.Error, new Regex("Standalone: restore failed"));

            var error = Assert.Throws<AggregateException>(
                () => EditorStateApplier.SuspendForBuild(entries, Resolve, profileA, null));

            Assert.AreEqual(1, error.InnerExceptions.Count);
            StringAssert.Contains("applied again", error.Message);
            Assert.AreEqual("base+A", plugin.World[BuildTargetGroup.Android], "The restored entry is applied again.");
            Assert.AreEqual("base+A", plugin.World[BuildTargetGroup.Standalone]);
            Assert.AreEqual(2, entries.Count);
            Assert.IsTrue(entries.All(e => e.baselineJson == "base" && e.expectedJson == "base+A"));
            Assert.IsEmpty(EditorStateApplier.Drift(entries, Resolve));

            plugin.ThrowOnRestoreGroup = null;
            EditorStateApplier.SuspendForBuild(entries, Resolve, profileA, null);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
            Assert.IsEmpty(EditorStateApplier.ResumeAfterBuild(entries, profileA, Resolve).Failures);
            Assert.IsEmpty(EditorStateApplier.Drift(entries, Resolve), "A later retry can still recover from the ledger.");
        }

        // Without the applied profile there is nothing to apply again: the restored entry stays
        // restored, the ledger is kept, and the message says so.
        [Test]
        public void Suspend_RestoreFailure_WithTheAppliedProfileGone_KeepsTheLedger()
        {
            Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone);
            plugin.ThrowOnRestoreGroup = BuildTargetGroup.Standalone;
            LogAssert.Expect(LogType.Error, new Regex("Standalone: restore failed"));

            var error = Assert.Throws<AggregateException>(
                () => EditorStateApplier.SuspendForBuild(entries, Resolve, null, null));

            StringAssert.Contains("applied profile is gone", error.Message);
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
            Assert.AreEqual(2, entries.Count);
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

            Assert.Throws<AggregateException>(() => EditorStateApplier.SuspendForBuild(entries, Resolve, profileA, null));

            Assert.AreEqual("baseline", entries.Single().baselineJson);
        }

        [Test]
        public void Resume_ApplyFailure_DoesNotAcceptFailedStateAndContinuesOtherEntries()
        {
            Apply(profileA, BuildTargetGroup.Android, BuildTargetGroup.Standalone);
            EditorStateApplier.SuspendForBuild(entries, Resolve, profileA, null);
            plugin.ThrowOnApplyGroup = BuildTargetGroup.Android;
            LogAssert.Expect(LogType.Error, new Regex("Android: re-apply after build failed"));

            var result = EditorStateApplier.ResumeAfterBuild(entries, profileB, Resolve);

            Assert.AreEqual(1, result.Failures.Count);
            Assert.IsTrue(result.Changed, "The successfully re-applied entry must still be saved.");
            Assert.AreEqual("base+A", entries[0].expectedJson, "Do not record the failed apply as expected state.");
            Assert.AreEqual(EditorStatePhase.Suspended, entries[0].phase, "Undoing it later restores its baseline whole.");
            Assert.AreEqual("base+B", entries[1].expectedJson);
            Assert.IsTrue(entries.All(e => e.baselineJson == "base"));
            CollectionAssert.AreEqual(new[] { "Fake|Android" + EditorStateApplier.BuildUnfinishedSuffix },
                EditorStateApplier.Drift(entries, Resolve));
        }

        [Test]
        public void Resume_CaptureFailure_ReportsFailureWithoutReplacingExpectedState()
        {
            Apply(profileA, BuildTargetGroup.Android);
            EditorStateApplier.SuspendForBuild(entries, Resolve, profileA, null);
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

        /// <summary>
        /// World state per group is a set of settings, "key=value" pairs sorted and
        /// joined with ';', starting as "x=1". Applying a profile sets the setting
        /// named after it, so profile A writes "A=on". Merges per setting.
        /// </summary>
        class KeyedPlugin : IForgeEditorApplicable
        {
            readonly Dictionary<BuildTargetGroup, SortedDictionary<string, string>> world = new();
            /// <summary>ApplyToEditor writes its setting, then throws.</summary>
            public bool ThrowAfterWriting;
            /// <summary>The capture right after ApplyToEditor throws ("throw") or returns null ("null").</summary>
            public string CaptureAfterApply;
            bool afterApply;

            SortedDictionary<string, string> Settings(BuildTargetGroup group)
                => world.TryGetValue(group, out var settings) ? settings : world[group] = Parse("x=1");

            public string State(BuildTargetGroup group) => Serialize(Settings(group));

            public void Set(BuildTargetGroup group, string key, string value) => Settings(group)[key] = value;

            public string CaptureEditorState(BuildTargetGroup group)
            {
                var failing = afterApply ? CaptureAfterApply : null;
                afterApply = false;
                if (failing == "throw")
                    throw new InvalidOperationException("capture after apply boom");
                return failing == "null" ? null : State(group);
            }

            public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group)
            {
                Set(group, forgeProfile.name, "on");
                afterApply = true;
                if (ThrowAfterWriting)
                    throw new InvalidOperationException("boom after writing");
            }

            public void RestoreEditorState(BuildTargetGroup group, string json) => world[group] = Parse(json);

            public string MergeEditorState(BuildTargetGroup group, string baselineJson, string expectedJson, string currentJson)
            {
                var baseline = Parse(baselineJson);
                var expected = Parse(expectedJson);
                var current = Parse(currentJson);
                var merged = new SortedDictionary<string, string>();
                foreach (var key in baseline.Keys.Union(current.Keys))
                {
                    baseline.TryGetValue(key, out var before);
                    expected.TryGetValue(key, out var applied);
                    current.TryGetValue(key, out var now);
                    var value = now == applied ? before : now;
                    if (value != null)
                        merged[key] = value;
                }
                return Serialize(merged);
            }

            static string Serialize(SortedDictionary<string, string> settings)
                => string.Join(";", settings.Select(p => p.Key + "=" + p.Value));

            static SortedDictionary<string, string> Parse(string json)
            {
                var settings = new SortedDictionary<string, string>();
                foreach (var pair in json.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    settings[pair.Split('=')[0]] = pair.Split('=')[1];
                return settings;
            }
        }

        const string KeyedName = "Keyed";
        static readonly Regex KeptLog = new Regex(@"Keyed\|Android: kept the changes made to the settings after the Apply");

        void ApplyKeyed(KeyedPlugin keyed, ForgeProfile profile)
            => EditorStateApplier.Apply(entries,
                new[] { new EditorStatePair { PluginTypeName = KeyedName, Group = BuildTargetGroup.Android, Plugin = keyed } },
                profile, name => name == KeyedName ? keyed : null, null);

        // A change made after the Apply, as by a git pull, survives the build: only
        // what the Apply wrote is undone, and the kept change becomes the baseline,
        // so reverting later keeps it too.
        [Test]
        public void Suspend_KeepsChangesMadeAfterTheApply_AndUndoesOnlyTheApply()
        {
            var keyed = new KeyedPlugin();
            Func<string, IForgeEditorApplicable> resolve = name => name == KeyedName ? keyed : null;
            ApplyKeyed(keyed, profileA);
            Assert.AreEqual("A=on;x=1", keyed.State(BuildTargetGroup.Android));
            keyed.Set(BuildTargetGroup.Android, "x", "2");
            LogAssert.Expect(LogType.Log, KeptLog);

            EditorStateApplier.SuspendForBuild(entries, resolve, profileA, null);

            Assert.AreEqual("x=2", keyed.State(BuildTargetGroup.Android));
            Assert.AreEqual("x=2", entries.Single().baselineJson);
            EditorStateApplier.ResumeAfterBuild(entries, profileA, resolve);
            Assert.AreEqual("A=on;x=2", keyed.State(BuildTargetGroup.Android));
            Assert.IsEmpty(EditorStateApplier.Drift(entries, resolve));
            EditorStateApplier.RevertAll(entries, resolve);
            Assert.AreEqual("x=2", keyed.State(BuildTargetGroup.Android));
        }

        [Test]
        public void RevertAll_KeepsChangesMadeAfterTheApply()
        {
            var keyed = new KeyedPlugin();
            ApplyKeyed(keyed, profileA);
            keyed.Set(BuildTargetGroup.Android, "x", "2");
            keyed.Set(BuildTargetGroup.Android, "y", "3");
            LogAssert.Expect(LogType.Log, KeptLog);

            EditorStateApplier.RevertAll(entries, name => name == KeyedName ? keyed : null);

            Assert.AreEqual("x=2;y=3", keyed.State(BuildTargetGroup.Android));
            Assert.IsEmpty(entries);
        }

        // Accepting drift takes the accepted changes into the baseline too, so
        // undoing the Apply later keeps them.
        [Test]
        public void DismissDrift_TakesTheAcceptedChangesIntoTheBaseline()
        {
            var keyed = new KeyedPlugin();
            Func<string, IForgeEditorApplicable> resolve = name => name == KeyedName ? keyed : null;
            ApplyKeyed(keyed, profileA);
            keyed.Set(BuildTargetGroup.Android, "x", "2");

            Assert.IsTrue(EditorStateApplier.DismissDrift(entries, resolve));

            Assert.AreEqual("x=2", entries.Single().baselineJson);
            Assert.AreEqual("A=on;x=2", entries.Single().expectedJson);
            EditorStateApplier.RevertAll(entries, resolve);
            Assert.AreEqual("x=2", keyed.State(BuildTargetGroup.Android));
        }

        // Re-applying keeps what changed after the Apply and writes the profile's own
        // settings again, so a pulled value of one of them returns on revert.
        [Test]
        public void Reapply_KeepsChangesMadeAfterTheApply_AndWritesTheProfilesSettingsAgain()
        {
            var keyed = new KeyedPlugin();
            ApplyKeyed(keyed, profileA);
            keyed.Set(BuildTargetGroup.Android, "A", "off");
            keyed.Set(BuildTargetGroup.Android, "x", "2");
            LogAssert.Expect(LogType.Log, KeptLog);

            ApplyKeyed(keyed, profileA);

            Assert.AreEqual("A=on;x=2", keyed.State(BuildTargetGroup.Android));
            Assert.AreEqual("A=off;x=2", entries.Single().baselineJson);
        }

        // Without MergeEditorState a plugin can't tell changes made after the Apply
        // from the Apply's: the baseline is restored, with a warning.
        [Test]
        public void ChangedAfterTheApply_WithoutMerge_RestoresTheBaselineAndWarns()
        {
            Apply(profileA, BuildTargetGroup.Android);
            plugin.World[BuildTargetGroup.Android] = "base+A+pulled";
            LogAssert.Expect(LogType.Warning, new Regex(@"Fake\|Android: the settings changed after the Apply"));

            EditorStateApplier.RevertAll(entries, Resolve);

            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
        }

        // ------------------------------------------------ an Apply or a build that did not finish

        Func<string, IForgeEditorApplicable> ResolveKeyed(KeyedPlugin keyed) => name => name == KeyedName ? keyed : null;

        static EditorStatePair KeyedPair(KeyedPlugin keyed)
            => new EditorStatePair { PluginTypeName = KeyedName, Group = BuildTargetGroup.Android, Plugin = keyed };

        // A merge would keep a failed plugin's own writes as changes made after the
        // Apply; they are the Apply's, so they are undone whole.
        [Test]
        public void Apply_MergingPluginFailsAfterWriting_RestoresItsBaselineWhole()
        {
            var keyed = new KeyedPlugin { ThrowAfterWriting = true };
            LogAssert.Expect(LogType.Error, new Regex(@"Keyed\|Android: apply failed\. boom after writing"));

            Assert.Throws<EditorStateApplyException>(() => ApplyKeyed(keyed, profileA));

            Assert.AreEqual("x=1", keyed.State(BuildTargetGroup.Android));
            Assert.IsEmpty(entries);
        }

        [Test]
        public void Reapply_MergingPluginFailsAfterWriting_RestoresTheBaselineFromBeforeTheFirstApply()
        {
            var keyed = new KeyedPlugin();
            ApplyKeyed(keyed, profileA);
            keyed.ThrowAfterWriting = true;
            LogAssert.Expect(LogType.Error, new Regex(@"Keyed\|Android: apply failed"));

            Assert.Throws<EditorStateApplyException>(() => ApplyKeyed(keyed, profileB));

            Assert.AreEqual("x=1", keyed.State(BuildTargetGroup.Android));
            Assert.IsEmpty(entries);
        }

        // Every pair recorded so far holds only this Apply's writes, so they are undone
        // whole too, even where a later plugin wrote into an earlier pair's group.
        [Test]
        public void Apply_StoppedByALaterPair_UndoesTheEarlierPairsWhole()
        {
            var keyed = new KeyedPlugin();
            plugin.ThrowOnApplyGroup = BuildTargetGroup.Standalone;
            plugin.World[BuildTargetGroup.Standalone] = "base";
            Func<string, IForgeEditorApplicable> resolve = name => name == KeyedName ? keyed : Resolve(name);
            LogAssert.Expect(LogType.Error, new Regex(@"Fake\|Standalone: apply failed"));

            Assert.Throws<EditorStateApplyException>(() => EditorStateApplier.Apply(entries,
                new[] { KeyedPair(keyed), Pair(BuildTargetGroup.Standalone) }, profileA, resolve,
                () => { if (entries.Count == 2) keyed.Set(BuildTargetGroup.Android, "y", "written later"); }));

            Assert.AreEqual("x=1", keyed.State(BuildTargetGroup.Android));
        }

        [TestCase("throw", "capture after apply failed. capture after apply boom")]
        [TestCase("null", "capture after apply failed. The applied settings could not be captured.")]
        public void Apply_CaptureAfterApplyFails_StopsAndRestoresTheBaseline(string failure, string logged)
        {
            var keyed = new KeyedPlugin { CaptureAfterApply = failure };
            LogAssert.Expect(LogType.Error, new Regex(@"Keyed\|Android: " + Regex.Escape(logged)));

            var stopped = Assert.Throws<EditorStateApplyException>(() => ApplyKeyed(keyed, profileA));

            Assert.AreEqual("Keyed|Android", stopped.Key);
            Assert.AreEqual("x=1", keyed.State(BuildTargetGroup.Android));
            Assert.IsEmpty(entries);
        }

        // The editor quit after the plugin saved its writes, before the result was
        // recorded: the ledger on disk is the checkpoint, and undoing it restores the
        // baseline whole instead of keeping the writes.
        [Test]
        public void Revert_AfterTheEditorQuitMidApply_RestoresTheBaselineWhole()
        {
            var keyed = new KeyedPlugin();
            List<EditorStateEntry> onDisk = null;
            EditorStateApplier.Apply(entries, new[] { KeyedPair(keyed) }, profileA, ResolveKeyed(keyed), () =>
            {
                if (entries.Count == 1 && keyed.State(BuildTargetGroup.Android) == "x=1")
                    onDisk = entries.Select(e => JsonUtility.FromJson<EditorStateEntry>(JsonUtility.ToJson(e))).ToList();
            });
            Assert.AreEqual(EditorStatePhase.Applying, onDisk.Single().phase);
            Assert.AreEqual("x=1", onDisk.Single().expectedJson);

            CollectionAssert.AreEqual(new[] { "Keyed|Android" + EditorStateApplier.ApplyUnfinishedSuffix },
                EditorStateApplier.Drift(onDisk, ResolveKeyed(keyed)));
            Assert.IsFalse(EditorStateApplier.DismissDrift(onDisk, ResolveKeyed(keyed)),
                "A partly applied profile must not be accepted as applied.");
            EditorStateApplier.RevertAll(onDisk, ResolveKeyed(keyed));

            Assert.AreEqual("x=1", keyed.State(BuildTargetGroup.Android));
            Assert.IsEmpty(onDisk);
        }

        [TestCase(nameof(EditorStatePhase.Applying))]
        [TestCase(nameof(EditorStatePhase.Suspended))]
        public void DismissDrift_LeavesAnEntryWhoseApplyOrBuildDidNotFinish(string phaseName)
        {
            var phase = (EditorStatePhase)Enum.Parse(typeof(EditorStatePhase), phaseName);
            var keyed = new KeyedPlugin();
            ApplyKeyed(keyed, profileA);
            keyed.Set(BuildTargetGroup.Android, "x", "2");
            var entry = entries.Single();
            entry.phase = phase;
            var baseline = entry.baselineJson;
            var expected = entry.expectedJson;

            Assert.IsFalse(EditorStateApplier.DismissDrift(entries, ResolveKeyed(keyed)),
                "A partly applied profile must not be accepted as applied.");
            Assert.AreEqual(baseline, entry.baselineJson);
            Assert.AreEqual(expected, entry.expectedJson);
            Assert.AreEqual(phase, entry.phase);
        }

        // Ledgers written before the phase existed have no such field: their entries
        // read as Applied and keep the three-way merge.
        [Test]
        public void EntryWrittenWithoutAPhase_ReadsAsApplied()
        {
            var entry = JsonUtility.FromJson<EditorStateEntry>(
                "{\"key\":\"Keyed|Android\",\"baselineJson\":\"x=1\",\"expectedJson\":\"A=on;x=1\"}");
            Assert.AreEqual(EditorStatePhase.Applied, entry.phase);
        }

        // The editor quit during a build: what the build wrote would look like changes
        // made after the Apply, so undoing it restores the baseline the suspension
        // restored, whole.
        [Test]
        public void Revert_AfterTheEditorQuitDuringABuild_RestoresTheSuspendedBaselineWhole()
        {
            var keyed = new KeyedPlugin();
            ApplyKeyed(keyed, profileA);
            keyed.Set(BuildTargetGroup.Android, "x", "2");
            LogAssert.Expect(LogType.Log, KeptLog);
            EditorStateApplier.SuspendForBuild(entries, ResolveKeyed(keyed), profileA, null);
            Assert.AreEqual(EditorStatePhase.Suspended, entries.Single().phase);
            keyed.Set(BuildTargetGroup.Android, "B", "on");
            keyed.Set(BuildTargetGroup.Android, "x", "3");

            CollectionAssert.AreEqual(new[] { "Keyed|Android" + EditorStateApplier.BuildUnfinishedSuffix },
                EditorStateApplier.Drift(entries, ResolveKeyed(keyed)));
            EditorStateApplier.RevertAll(entries, ResolveKeyed(keyed));

            Assert.AreEqual("x=2", keyed.State(BuildTargetGroup.Android), "The change made before the build stays.");
        }

        [Test]
        public void Resume_DoesNotCompleteAnApplyThatNeverFinished()
        {
            entries.Add(new EditorStateEntry
            {
                key = "Fake|Android", baselineJson = "base", expectedJson = "base", phase = EditorStatePhase.Applying
            });

            var result = EditorStateApplier.ResumeAfterBuild(entries, profileA, Resolve);

            Assert.IsEmpty(plugin.Calls.Where(c => c.StartsWith("apply")));
            Assert.IsFalse(result.Changed);
            StringAssert.Contains("its Apply did not finish", result.Failures.Single().Message);
            Assert.AreEqual(EditorStatePhase.Applying, entries.Single().phase);
        }

        // A plugin whose settings could not be put back after the build holds an unknown
        // state: the profile isn't applied on top of it, nor is the mirror that copies it.
        [Test]
        public void Resume_LeavesAPluginThatWasNotRestored_AndTheMirrorAfterIt()
        {
            var mirror = new CopyMirror(plugin, BuildTargetGroup.Android);
            Func<string, IForgeEditorApplicable> resolve = name => name == MirrorName ? mirror : Resolve(name);
            EditorStateApplier.Apply(entries, new[] { Pair(BuildTargetGroup.Android), MirrorPair(mirror) }, profileA, resolve, null);
            EditorStateApplier.SuspendForBuild(entries, resolve, profileA, null);

            var result = EditorStateApplier.ResumeAfterBuild(entries, profileA, resolve, null,
                new[] { EditorStateLedger.MakeKey(PluginName, BuildTargetGroup.Android) });

            Assert.AreEqual(2, result.Failures.Count);
            StringAssert.Contains("could not be put back after the build", result.Failures[0].Message);
            StringAssert.Contains("the platform settings it copies were not", result.Failures[1].Message);
            Assert.IsTrue(entries.All(e => e.phase == EditorStatePhase.Suspended));
            Assert.AreEqual("base", plugin.World[BuildTargetGroup.Android]);
            Assert.AreEqual("standalone", mirror.State, "The mirror doesn't copy a platform that wasn't put back.");
        }

        // A build of another platform touches only that platform's settings: the applied
        // group is applied again, but the mirrors write Standalone, which the build left.
        [Test]
        public void Resume_RestoreFailedForTheStandaloneBuild_ReappliesTheAppliedGroup_ButNotTheMirror()
        {
            var mirror = new CopyMirror(plugin, BuildTargetGroup.Android);
            Func<string, IForgeEditorApplicable> resolve = name => name == MirrorName ? mirror : Resolve(name);
            EditorStateApplier.Apply(entries, new[] { Pair(BuildTargetGroup.Android), MirrorPair(mirror) }, profileA, resolve, null);
            EditorStateApplier.SuspendForBuild(entries, resolve, profileA, null);

            var result = EditorStateApplier.ResumeAfterBuild(entries, profileA, resolve, null,
                new[] { EditorStateLedger.MakeKey(PluginName, BuildTargetGroup.Standalone) });

            StringAssert.Contains("Mirror|Standalone: not applied again, because its settings could not be put back",
                result.Failures.Single().Message);
            Assert.AreEqual("base+A", plugin.World[BuildTargetGroup.Android]);
            Assert.AreEqual(EditorStatePhase.Applied, entries[0].phase);
            Assert.AreEqual(EditorStatePhase.Suspended, entries[1].phase);
            Assert.AreEqual("standalone", mirror.State);
        }

        [Test]
        public void Resume_PlatformReapplyFails_LeavesTheMirrorAfterIt()
        {
            var mirror = new CopyMirror(plugin, BuildTargetGroup.Android);
            Func<string, IForgeEditorApplicable> resolve = name => name == MirrorName ? mirror : Resolve(name);
            EditorStateApplier.Apply(entries, new[] { Pair(BuildTargetGroup.Android), MirrorPair(mirror) }, profileA, resolve, null);
            EditorStateApplier.SuspendForBuild(entries, resolve, profileA, null);
            plugin.ThrowOnApplyGroup = BuildTargetGroup.Android;
            LogAssert.Expect(LogType.Error, new Regex(@"Fake\|Android: re-apply after build failed"));

            var result = EditorStateApplier.ResumeAfterBuild(entries, profileA, resolve);

            Assert.AreEqual(2, result.Failures.Count);
            Assert.AreEqual("standalone", mirror.State, "The mirror doesn't copy a platform that wasn't applied again.");
            Assert.IsTrue(entries.All(e => e.phase == EditorStatePhase.Suspended));
        }

        // The runner records, by ledger key, the editor-applicable plugins that could not put
        // the built group back; another plugin's failure is only reported.
        [Test]
        public void RunPostBuild_RecordsTheEditorApplicablePluginsWhoseRestoreFailed()
        {
            var order = new List<string>();
            var plugins = new IForgePlugin[]
            {
                new ApplicablePostBuildPlugin("applicable", order, fails: true),
                new PostBuildPlugin("plain", order, fails: true),
                new ApplicablePostBuildPlugin("restored", order, fails: false),
            };
            var context = new ForgeBuildContext(null, profileA, BuildTarget.StandaloneWindows64, "unused", true);
            LogAssert.Expect(LogType.Error, new Regex("Post-build restore failed in plain"));
            LogAssert.Expect(LogType.Error, new Regex("Post-build restore failed in applicable"));

            var notRestored = ForgeBuildRunner.RunPostBuild(plugins, context);

            CollectionAssert.AreEqual(new[] { "restored", "plain", "applicable" }, order, "In reverse order of OnPreBuild.");
            CollectionAssert.AreEquivalent(
                new[] { EditorStateLedger.MakeKey(typeof(ApplicablePostBuildPlugin).FullName, BuildTargetGroup.Standalone) },
                notRestored);
            Assert.AreEqual(2, context.PostBuildFailures.Count);
        }

        class PostBuildPlugin : IForgePlugin
        {
            readonly List<string> order;
            readonly bool fails;

            public PostBuildPlugin(string name, List<string> order, bool fails) { DisplayName = name; this.order = order; this.fails = fails; }

            public string DisplayName { get; }
            public bool IsApplicable(UnityEditor.Build.Profile.BuildProfile profile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context)
            {
                order.Add(DisplayName);
                if (fails)
                    throw new InvalidOperationException("restore boom");
            }
        }

        class ApplicablePostBuildPlugin : PostBuildPlugin, IForgeEditorApplicable
        {
            public ApplicablePostBuildPlugin(string name, List<string> order, bool fails) : base(name, order, fails) { }

            public string CaptureEditorState(BuildTargetGroup group) => null;
            public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group) { }
            public void RestoreEditorState(BuildTargetGroup group, string json) { }
        }

        // A stopped build re-applies what it restored in the Apply's order: a Play Mode
        // mirror copies its platform as the plugins leave it, so it comes after them.
        [Test]
        public void Suspend_RestoreFailure_ReappliesInTheApplysOrder()
        {
            var loaders = new ChainPlugin("loaders-before", () => "loaders-applied");
            var source = new ChainPlugin("source-before", () => "source-applied");
            var mirror = new ChainPlugin("standalone-before", () => source.State);
            var plugins = new Dictionary<string, IForgeEditorApplicable>
            {
                { "Loaders", loaders }, { "Source", source }, { "Mirror", mirror },
            };
            Func<string, IForgeEditorApplicable> resolve = name => plugins.TryGetValue(name, out var p) ? p : null;
            EditorStateApplier.Apply(entries, new[]
            {
                new EditorStatePair { PluginTypeName = "Loaders", Group = BuildTargetGroup.Android, Plugin = loaders },
                new EditorStatePair { PluginTypeName = "Source", Group = BuildTargetGroup.Android, Plugin = source },
                new EditorStatePair { PluginTypeName = "Mirror", Group = BuildTargetGroup.Standalone, Plugin = mirror },
            }, profileA, resolve, null);
            Assert.AreEqual("source-applied", mirror.State);
            loaders.FailRestore = true;
            LogAssert.Expect(LogType.Error, new Regex(@"Loaders\|Android: restore failed"));

            Assert.Throws<AggregateException>(() => EditorStateApplier.SuspendForBuild(entries, resolve, profileA, null));

            Assert.AreEqual("source-applied", source.State);
            Assert.AreEqual("source-applied", mirror.State);
            Assert.IsTrue(entries.All(e => e.phase == EditorStatePhase.Applied));
        }

        /// <summary>A plugin whose state is one string, which applying sets from <c>apply</c>.</summary>
        class ChainPlugin : IForgeEditorApplicable
        {
            public string State;
            public bool FailRestore;
            readonly Func<string> apply;

            public ChainPlugin(string state, Func<string> apply) { State = state; this.apply = apply; }

            public string CaptureEditorState(BuildTargetGroup group) => State;
            public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group) => State = apply();
            public void RestoreEditorState(BuildTargetGroup group, string json)
            {
                if (FailRestore)
                    throw new InvalidOperationException("restore boom");
                State = json;
            }
        }

        const string MirrorName = "Mirror";

        static EditorStatePair MirrorPair(CopyMirror mirror)
            => new EditorStatePair { PluginTypeName = MirrorName, Group = BuildTargetGroup.Standalone, Plugin = mirror };

        /// <summary>
        /// A Play Mode mirror that copies the fake plugin's world for one group. It takes
        /// constructor arguments, so Build Forge's own discovery of mirrors (types with a
        /// parameterless constructor) never picks it up in an editor running these tests.
        /// </summary>
        class CopyMirror : IForgePlayModeMirror
        {
            readonly FakePlugin source;
            readonly BuildTargetGroup sourceGroup;
            public string State = "standalone";

            public CopyMirror(FakePlugin source, BuildTargetGroup sourceGroup) { this.source = source; this.sourceGroup = sourceGroup; }

            public string DisplayName => "Copy";
            public bool CanMirror(BuildTargetGroup group) => true;
            public string CaptureEditorState(BuildTargetGroup group) => State;
            public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group)
                => State = source.World.TryGetValue(sourceGroup, out var w) ? w : "base";
            public void RestoreEditorState(BuildTargetGroup group, string json) => State = json;
        }
    }
}
