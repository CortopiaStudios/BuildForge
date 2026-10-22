using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.TestTools;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// Profile A is applied. Then its stored plugin settings stop loading: the file names a
    /// settings class that no longer exists, while the plugin itself still loads. Apply's
    /// readiness check (ForgePluginRegistry.BlockingError) now refuses A. A build of
    /// another profile, B, checks only B, suspends A for the build and applies A again after
    /// it. A's settings would read as the plugin's defaults, apply nothing and be recorded
    /// as A applied, so the readiness check stops that too, and A is treated like a deleted
    /// profile: the settings the build put back stay at baseline and are no longer tracked,
    /// nothing is applied, and a warning names A and the missing class. A suspension that
    /// stops before the build doesn't apply A again either: what it restored stays at
    /// baseline, tracked, for the Revert to Baseline its message asks for.
    /// The probe stands in for the two bundled editor plugins. XR Loaders: default settings
    /// are off, and ApplyToEditor then leaves the current loaders alone. OpenXR: default
    /// settings hold no overrides, and ApplyToEditor then writes nothing.
    /// The tests apply A through EditorStateApplier.Apply with the checkpoint
    /// ForgeEditorState.Apply uses. They don't call ForgeEditorState.Apply itself, because it
    /// would activate A and recompile the host. After that they use the entry points a build
    /// uses: ForgeEditorState.SuspendForBuild and ForgeEditorState.ResumeAfterBuild.
    /// </summary>
    public class ResumeReadinessTests
    {
        const string SettingsKey = "ResumeProbe";
        const string MissingClassName = "GoneProbeSettings";
        const string ProfileAName = "Resume Profile A";
        const string ProfileBName = "Resume Profile B";
        const string Base = "base";
        const string Requested = "requested-by-A";

        // Stands in for a project setting a plugin writes, such as the XR loader list.
        static string editorSetting;

        [Serializable]
        public class ResumeProbeSettings
        {
            public bool enabled;
            public string value;
        }

        [Serializable]
        sealed class Snapshot
        {
            public string value;
        }

        static string SnapshotOf(string value) => JsonUtility.ToJson(new Snapshot { value = value });

        sealed class ResumeProbePlugin : IForgePlugin, IForgeEditorApplicable
        {
            readonly bool switchedBySettings;

            public ResumeProbePlugin(bool switchedBySettings)
            {
                this.switchedBySettings = switchedBySettings;
            }

            static ResumeProbeSettings Settings(ForgeProfile profile) => profile.GetPluginConfig<ResumeProbeSettings>(SettingsKey);

            public string DisplayName => "Resume Probe";
            public bool IsApplicable(BuildProfile buildProfile) => buildProfile != null;
            public bool? IsEnabled(ForgeProfile forgeProfile) => switchedBySettings ? Settings(forgeProfile).enabled : (bool?)null;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }

            public string CaptureEditorState(BuildTargetGroup group) => SnapshotOf(editorSetting);

            public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group, string variant)
            {
                var settings = Settings(forgeProfile);
                // XR Loaders: settings that are off leave the current state alone.
                if (switchedBySettings && !settings.enabled)
                    return;
                // OpenXR: settings without overrides write nothing.
                if (string.IsNullOrEmpty(settings.value))
                    return;
                editorSetting = settings.value;
            }

            public void RestoreEditorState(BuildTargetGroup group, string json)
                => editorSetting = JsonUtility.FromJson<Snapshot>(json).value;
        }

        // A second setting group: one whose restore fails, which stops a build before it
        // starts, or one the build can't put back.
        sealed class StuckRestorePlugin : IForgePlugin, IForgeEditorApplicable
        {
            public bool FailRestore;
            public string DisplayName => "Stuck Restore";
            public bool IsApplicable(BuildProfile buildProfile) => buildProfile != null;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
            public string CaptureEditorState(BuildTargetGroup group) => "{}";
            public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group, string variant) { }

            public void RestoreEditorState(BuildTargetGroup group, string json)
            {
                if (FailRestore)
                    throw new InvalidOperationException("stuck restore");
            }
        }

        sealed class Scenario
        {
            public ForgeEditorStateStore Store;
            public string PathA;
            public ForgeProfile AppliedA;
            public ForgeProfile ForgeB;
            public string ProbeEntryKey;
            public string StuckEntryKey;
        }

        sealed class Outcome
        {
            public Exception Error;
            public readonly List<(LogType Type, string Message)> Reports = new List<(LogType, string)>();
        }

        // The resume a build makes in its finally block.
        [Test]
        public void ResumeAfterBuild_DoesNotRecordUnreadableSettingsAsApplied_WhenDefaultsSwitchThePluginOff()
            => ResumeAfterBuildingB(switchedBySettings: true);

        [Test]
        public void ResumeAfterBuild_DoesNotRecordUnreadableSettingsAsApplied_WhenDefaultsHoldNoOverrides()
            => ResumeAfterBuildingB(switchedBySettings: false);

        // As with a deleted profile, a setting group the build could not put back stays
        // recorded for Revert to Baseline, and so does A while it does.
        [Test]
        public void ResumeAfterBuild_KeepsWhatTheBuildDidNotPutBack_ForUnreadableSettings()
        {
            var stuck = new StuckRestorePlugin();
            WithAppliedProfileWhoseSettingsBecameUnreadable(true, stuck, s =>
            {
                var suspension = ForgeEditorState.SuspendForBuild(s.ForgeB);
                Assert.IsNotNull(suspension, "A is applied, so the build of B suspends it.");

                var outcome = Capture(() => ForgeEditorState.ResumeAfterBuild(suspension, new[] { s.StuckEntryKey }));

                var entry = EditorStateLedger.Find(s.Store.EditorState, s.ProbeEntryKey);
                var left = EditorStateLedger.Find(s.Store.EditorState, s.StuckEntryKey);
                var observed = Describe(outcome, entry);
                Assert.IsNull(entry, "The build put the probe's settings back, so its entry is dropped. Observed: " + observed);
                Assert.IsNotNull(left, "The group the build could not put back stays recorded. Observed: " + observed);
                Assert.AreEqual(EditorStatePhase.Suspended, left.phase, "Undoing it restores its baseline whole.");
                Assert.IsTrue(ForgeEditorState.HasAppliedGuid, "A's traces stay while an entry does. Observed: " + observed);
                Assert.IsNotNull(outcome.Error, "The build records a post-build failure for the group it could not put back.");
                StringAssert.Contains("stays recorded for Revert to Baseline", Flatten(outcome.Error));
                AssertWarned(outcome, observed);
                Assert.AreEqual(Base, editorSetting, "The editor holds the state from before A's Apply. Observed: " + observed);
                AssertStoredSettingsKept(s);
                AssertRecoverable(s);
            });
        }

        // The same re-apply when a suspension stops: a setting group that can't be restored
        // stops the build, and the groups already restored would be applied again.
        [Test]
        public void StoppedSuspension_DoesNotRecordUnreadableSettingsAsAppliedAgain()
        {
            var stuck = new StuckRestorePlugin();
            WithAppliedProfileWhoseSettingsBecameUnreadable(true, stuck, s =>
            {
                stuck.FailRestore = true;
                var outcome = Capture(() => ForgeEditorState.SuspendForBuild(s.ForgeB));
                Assert.IsNotNull(outcome.Error, "Precondition: a setting group that can't be restored stops the build before it starts.");

                var entry = EditorStateLedger.Find(s.Store.EditorState, s.ProbeEntryKey);
                var observed = Describe(outcome, entry);
                // As without the applied profile: what was restored stays restored, and stays
                // recorded with the group that couldn't be, for the Revert to Baseline the
                // message asks for. Builds stop until then.
                Assert.IsNotNull(entry, "The restored group stays recorded. Observed: " + observed);
                Assert.AreEqual(EditorStatePhase.Suspended, entry.phase,
                    $"'{ProfileAName}', whose Build Forge settings Unity cannot load, must not be applied again with the " +
                    $"plugin's default settings and recorded as applied. Observed: {observed}");
                Assert.AreEqual(SnapshotOf(Base), entry.baselineJson,
                    "The entry keeps the state from before A's Apply, for Revert to Baseline. Observed: " + observed);
                Assert.IsTrue(ForgeEditorState.HasAppliedGuid, "A's traces stay while its entries do. Observed: " + observed);
                Assert.IsNotNull(ForgeEditorState.InterruptedError(), "Builds stop until Revert to Baseline. Observed: " + observed);
                var message = Flatten(outcome.Error);
                StringAssert.DoesNotContain("applied again", message, "Nothing was applied again. Observed: " + observed);
                StringAssert.Contains(ProfileAName, message, "The message names A. Observed: " + observed);
                StringAssert.Contains(MissingClassName, message, "The message names the missing class. Observed: " + observed);
                Assert.AreEqual(Base, editorSetting, "The editor holds the state from before A's Apply. Observed: " + observed);
                AssertStoredSettingsKept(s);

                stuck.FailRestore = false;
                AssertRecoverable(s);
            });
        }

        static void ResumeAfterBuildingB(bool switchedBySettings)
        {
            WithAppliedProfileWhoseSettingsBecameUnreadable(switchedBySettings, null, s =>
            {
                // ForgeBuildRunner.RunBuild: SuspendForBuild before the player build...
                var suspension = ForgeEditorState.SuspendForBuild(s.ForgeB);
                Assert.IsNotNull(suspension, "A is applied, so the build of B suspends it.");
                Assert.AreEqual(Base, editorSetting, "B builds on the state from before A's Apply.");
                Assert.AreEqual(EditorStatePhase.Suspended, EditorStateLedger.Find(s.Store.EditorState, s.ProbeEntryKey).phase);

                // ...and ResumeAfterBuild in its finally block.
                var outcome = Capture(() => ForgeEditorState.ResumeAfterBuild(suspension));

                var entry = EditorStateLedger.Find(s.Store.EditorState, s.ProbeEntryKey);
                var observed = Describe(outcome, entry);
                Assert.IsTrue(entry == null && !ForgeEditorState.HasAppliedGuid,
                    $"'{ProfileAName}', whose Build Forge settings Unity cannot load, must be treated like a deleted profile: " +
                    $"its entry dropped and nothing applied. Observed: {observed}");
                Assert.IsNull(outcome.Error, "Nothing failed: the build put every setting back. Observed: " + observed);
                AssertWarned(outcome, observed);
                Assert.IsNull(ForgeEditorState.InterruptedError(), "Nothing stops builds or Apply. Observed: " + observed);
                Assert.AreEqual(Base, editorSetting, "The editor holds the state from before A's Apply. Observed: " + observed);
                AssertStoredSettingsKept(s);
                AssertRecoverable(s);
            });
        }

        static void WithAppliedProfileWhoseSettingsBecameUnreadable(bool switchedBySettings, StuckRestorePlugin stuck,
            Action<Scenario> body)
        {
            var store = ForgeEditorStateStore.instance;
            var previousStore = EditorJsonUtility.ToJson(store);
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var previousIgnore = LogAssert.ignoreFailingMessages;
            var previousSetting = editorSetting;
            var folderName = "BuildForgeResumeReadinessTest-" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            var probe = new ResumeProbePlugin(switchedBySettings);
            var testPlugins = stuck != null ? new IForgePlugin[] { probe, stuck } : new IForgePlugin[] { probe };
            // The editor resolves ledger entries through the registry, so the probe joins its
            // cached list for this test and leaves it in the finally block.
            var registry = ForgePluginRegistry.GetPlugins() as List<IForgePlugin>;
            BuildProfile nativeB = null;
            ForgeProfile forgeB = null;
            try
            {
                Assert.IsNotNull(registry, "Precondition: the plugin registry caches a List<IForgePlugin>.");
                // What is reported is up to the assertions, not to the log check.
                LogAssert.ignoreFailingMessages = true;
                registry.AddRange(testPlugins);

                editorSetting = Base;
                store.EditorState.Clear();
                store.AppliedBuildProfileGuid = "";
                store.AppliedVariant = "";
                store.SaveState();
                ForgeEditorState.InvalidateDriftCache();
                var settings = new SerializedObject(ForgeSettings.instance);
                settings.FindProperty("buildVariants").arraySize = 0;
                settings.FindProperty("variantRules").arraySize = 0;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsNull(InterruptedBuildMarker.Error(), "Precondition: no unfinished-build marker in this host.");

                // Profile A: a Unity Build Profile asset with its Build Forge settings stored in it.
                AssetDatabase.CreateFolder("Assets", folderName);
                var pathA = folder + "/" + ProfileAName + ".asset";
                AssetDatabase.CreateAsset(VariantRulesTests.CreateNativeProfile(), pathA);
                var buildProfileA = AssetDatabase.LoadAssetAtPath<BuildProfile>(pathA);
                var forgeA = ForgeProfileEmbedding.Add(buildProfileA);
                forgeA.SetPluginConfig(SettingsKey, new ResumeProbeSettings { enabled = true, value = Requested });
                AssetDatabase.SaveAssetIfDirty(forgeA);
                var guidA = AssetDatabase.AssetPathToGUID(pathA);
                StringAssert.Contains(nameof(ResumeProbeSettings), File.ReadAllText(pathA), "Precondition: A's file stores its settings.");
                Assert.IsNull(ForgePluginRegistry.BlockingError(forgeA), "Precondition: Apply's readiness check accepts A.");

                // Apply A as ForgeEditorState.Apply does after its checks, without activating A.
                var pairs = ForgeEditorState.GetParticipatingPairs(forgeA, testPlugins, guarded: false);
                Assert.AreEqual(testPlugins.Length, pairs.Count, "Precondition: the test plugins take part in A's Apply.");
                EditorStateApplier.Apply(store.EditorState, pairs, forgeA, ResolveLikeTheEditor, () =>
                {
                    store.AppliedBuildProfileGuid = guidA;
                    store.AppliedVariant = "";
                    store.SaveState();
                });
                store.SaveState();
                ForgeEditorState.InvalidateDriftCache();
                var probeEntryKey = EditorStateLedger.MakeKey(typeof(ResumeProbePlugin).FullName, pairs[0].Group);
                var applied = EditorStateLedger.Find(store.EditorState, probeEntryKey);
                Assert.IsNotNull(applied, "Precondition: A's Apply recorded the probe.");
                Assert.AreEqual(Requested, editorSetting, "Precondition: A's Apply wrote its setting.");
                Assert.AreEqual(EditorStatePhase.Applied, applied.phase);
                Assert.AreEqual(SnapshotOf(Base), applied.baselineJson);
                Assert.AreEqual(SnapshotOf(Requested), applied.expectedJson);
                Assert.IsTrue(ForgeEditorState.IsApplied(forgeA), "Precondition: A counts as applied.");
                CollectionAssert.IsEmpty(ForgeEditorState.GetDrift(force: true), "Precondition: no drift after the Apply.");

                // A pull changes A's file: its settings now name a class that doesn't exist.
                // The probe plugin still loads.
                File.WriteAllText(pathA, File.ReadAllText(pathA).Replace(nameof(ResumeProbeSettings), MissingClassName));
                AssetDatabase.ImportAsset(pathA, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var appliedA = ForgeEditorState.AppliedProfile;
                Assert.IsNotNull(appliedA, "Precondition: A still resolves as the applied profile through the ledger's GUID.");
                var readBack = appliedA.GetPluginConfig<ResumeProbeSettings>(SettingsKey);
                Assert.IsFalse(readBack.enabled, "Precondition: A's settings read as the plugin's defaults.");
                Assert.IsNull(readBack.value, "Precondition: A's settings read as the plugin's defaults.");
                StringAssert.Contains(MissingClassName, ForgePluginRegistry.BlockingError(appliedA) ?? "(no error)",
                    "Precondition: Apply's readiness check now refuses A.");

                // Profile B, the one built: its own checks pass, and they are the build's only ones
                // (ForgeBuildRunner.CheckBeforeBuild: BlockingError for B, then InterruptedError).
                nativeB = VariantRulesTests.CreateNativeProfile();
                nativeB.name = ProfileBName;
                forgeB = ScriptableObject.CreateInstance<ForgeProfile>();
                var fb = new SerializedObject(forgeB);
                fb.FindProperty("buildProfile").objectReferenceValue = nativeB;
                fb.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsNull(ForgePluginRegistry.BlockingError(forgeB), "The build of B checks B's settings, which load.");
                Assert.IsNull(ForgeEditorState.InterruptedError(), "Nothing unfinished stops the build of B.");

                body(new Scenario
                {
                    Store = store,
                    PathA = pathA,
                    AppliedA = appliedA,
                    ForgeB = forgeB,
                    ProbeEntryKey = probeEntryKey,
                    StuckEntryKey = EditorStateLedger.MakeKey(typeof(StuckRestorePlugin).FullName, pairs[0].Group),
                });
            }
            finally
            {
                if (registry != null)
                    foreach (var plugin in testPlugins)
                        registry.Remove(plugin);
                EditorJsonUtility.FromJsonOverwrite(previousStore, store);
                store.SaveState();
                ForgeEditorState.InvalidateDriftCache();
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                editorSetting = previousSetting;
                if (forgeB != null)
                    UnityEngine.Object.DestroyImmediate(forgeB);
                if (nativeB != null)
                    UnityEngine.Object.DestroyImmediate(nativeB);
                if (AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        // As ForgeEditorState.ResolvePlugin, without the Play Mode mirrors these tests don't use.
        static IForgeEditorApplicable ResolveLikeTheEditor(string typeName)
            => ForgePluginRegistry.GetPlugins().FirstOrDefault(p => p.GetType().FullName == typeName) as IForgeEditorApplicable;

        // Runs the step, keeping what it throws and the warnings and errors Build Forge logs meanwhile.
        static Outcome Capture(Action step)
        {
            var outcome = new Outcome();
            Application.LogCallback collect = (message, stackTrace, type) =>
            {
                if (type != LogType.Log && message != null && message.Contains("[Build Forge"))
                    outcome.Reports.Add((type, message));
            };
            Application.logMessageReceived += collect;
            try
            {
                step();
            }
            catch (Exception e)
            {
                outcome.Error = e;
            }
            finally
            {
                Application.logMessageReceived -= collect;
            }
            return outcome;
        }

        static string Describe(Outcome outcome, EditorStateEntry entry)
            => $"threw: {(outcome.Error == null ? "nothing" : Flatten(outcome.Error))}; " +
               $"Build Forge warnings and errors: [{string.Join(" | ", outcome.Reports.Select(r => $"{r.Type}: {r.Message}"))}]; " +
               (entry == null
                   ? "probe entry: removed; "
                   : $"probe entry: phase {entry.phase}, baseline {entry.baselineJson}, expected {entry.expectedJson}; ") +
               $"applied GUID kept: {ForgeEditorState.HasAppliedGuid}; " +
               $"InterruptedError: {ForgeEditorState.InterruptedError() ?? "null"}; " +
               $"drift: [{string.Join(", ", ForgeEditorState.GetDrift(force: true))}]; " +
               $"setting: {editorSetting}";

        // The resume warns, naming A and the class whose settings can't load.
        static void AssertWarned(Outcome outcome, string observed)
        {
            Assert.IsTrue(outcome.Reports.Any(r => r.Type == LogType.Warning
                                                   && r.Message.Contains(ProfileAName) && r.Message.Contains(MissingClassName)),
                $"No warning said that '{ProfileAName}' was not applied again because its settings cannot be loaded. Observed: {observed}");
        }

        static void AssertStoredSettingsKept(Scenario s)
            => StringAssert.Contains(MissingClassName, File.ReadAllText(s.PathA),
                "A's stored settings stay as they are, for the user to fix.");

        // Re-apply stops on the readiness check, before it changes anything. Revert to Baseline
        // leaves the state from before A's Apply and nothing tracked.
        static void AssertRecoverable(Scenario s)
        {
            // Safe to call: BlockingError refuses A (asserted above) before Apply writes or activates anything.
            var reapply = Assert.Throws<InvalidOperationException>(() => ForgeEditorState.Apply(s.AppliedA));
            StringAssert.Contains(MissingClassName, reapply.Message, "Re-apply of A stops on its unreadable settings.");

            ForgeEditorState.RevertToBaseline();
            CollectionAssert.IsEmpty(s.Store.EditorState, "Revert to Baseline leaves nothing tracked.");
            Assert.IsFalse(ForgeEditorState.HasAppliedGuid, "Nothing is applied after Revert to Baseline.");
            Assert.AreEqual(Base, editorSetting, "Revert to Baseline leaves the state from before A's Apply.");
            AssertStoredSettingsKept(s);
        }

        static string Flatten(Exception error)
        {
            var messages = new List<string>();

            void Add(Exception e)
            {
                if (e == null)
                    return;
                messages.Add(e.Message);
                if (e is AggregateException aggregate)
                    foreach (var inner in aggregate.InnerExceptions)
                        Add(inner);
                else
                    Add(e.InnerException);
            }

            Add(error);
            return string.Join(" / ", messages.Distinct());
        }
    }
}
