using System;
using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// One (plugin, build target group) participating in an editor apply.
    /// </summary>
    internal sealed class EditorStatePair
    {
        public string PluginTypeName;
        public BuildTargetGroup Group;
        public IForgeEditorApplicable Plugin;
    }

    internal struct ResumeResult
    {
        /// <summary>Some entry's expected snapshot or phase changed, or entries were dropped; the caller should save.</summary>
        public bool Changed;
        /// <summary>The applied profile no longer exists; every entry was cleared and the caller should clear the applied GUID.</summary>
        public bool Cleared;
        /// <summary>Entries that could not be re-applied or verified; their previous snapshots remain intact.</summary>
        public List<Exception> Failures;
    }

    /// <summary>A recorded setting group whose baseline could not be restored.</summary>
    internal sealed class EditorStateRestoreException : InvalidOperationException
    {
        /// <summary>The ledger key (plugin type name and target group).</summary>
        public string Key { get; }
        /// <summary>Why, without the key: the failure and the plugin's own message.</summary>
        public string Reason { get; }

        public EditorStateRestoreException(string key, string reason, Exception inner = null)
            : base($"{key}: {reason}", inner)
        {
            Key = key;
            Reason = inner == null ? reason : $"{reason} {ForgeEditorState.MessageOrType(inner)}";
        }
    }

    /// <summary>A plugin's capture or apply that failed and so stopped an Apply.</summary>
    internal sealed class EditorStateApplyException : InvalidOperationException
    {
        /// <summary>The ledger key (plugin type name and target group).</summary>
        public string Key { get; }
        /// <summary>The plugin's own message.</summary>
        public string Reason { get; }

        // Read guarded: a plugin's message that throws here would end the Apply's catch before
        // it restores what the Apply wrote.
        public EditorStateApplyException(string key, string what, Exception inner)
            : base($"{key}: {what}. {ForgeEditorState.MessageOrType(inner)}", inner)
        {
            Key = key;
            Reason = ForgeEditorState.MessageOrType(inner);
        }
    }

    /// <summary>
    /// Pure orchestration of the editor-state invariant: applied state is always
    /// baseline + one profile's deltas, never stacked on a previous profile.
    /// Operates on plain entry lists and a plugin resolver so it can be tested
    /// without the ForgeSettings singleton or any Unity asset API. Build recovery
    /// attempts every entry and reports failures without discarding its snapshots.
    /// </summary>
    internal static class EditorStateApplier
    {
        const string LogPrefix = "[Build Forge/EditorState]";

        /// <summary>
        /// Undoes every entry's Apply (reverse order; see <see cref="RestoreEntry"/>),
        /// removing only successful entries. Failed entries remain available for
        /// retry, even without the previously applied profile asset.
        /// </summary>
        public static void RevertAll(List<EditorStateEntry> entries, Func<string, IForgeEditorApplicable> resolve)
        {
            var failures = new List<Exception>();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var failure = RestoreEntry(entries[i], resolve, out _);
                if (failure == null)
                    entries.RemoveAt(i);
                else
                    failures.Add(failure);
            }
            if (failures.Count > 0)
                throw new AggregateException(
                    "Could not revert all editor settings. Their baselines were kept, so Revert to Baseline can " +
                    "retry once the cause is fixed, or stop tracking them.",
                    failures);
        }

        /// <summary>
        /// Reverts whatever is applied, then for each pair captures the baseline,
        /// applies the profile, captures the expected state, and records an entry.
        /// <paramref name="saveCheckpoint"/> is invoked after the revert (previous
        /// entries gone, list empty), after each baseline is recorded (before the
        /// plugin writes anything), and after the last pair. A capture or apply that
        /// throws stops the Apply, as a failing plugin stops a build: what it wrote
        /// so far is restored and an <see cref="EditorStateApplyException"/> names
        /// the pair. An entry that cannot be restored stays for a retry; the
        /// exception is then an <see cref="AggregateException"/> that also holds
        /// those restore failures.
        /// </summary>
        public static void Apply(
            List<EditorStateEntry> entries,
            IReadOnlyList<EditorStatePair> pairs,
            ForgeProfile forgeProfile,
            Func<string, IForgeEditorApplicable> resolve,
            Action saveCheckpoint,
            string variant = null)
        {
            RevertAll(entries, resolve);
            saveCheckpoint?.Invoke();

            foreach (var pair in pairs)
            {
                var key = EditorStateLedger.MakeKey(pair.PluginTypeName, pair.Group);

                string baseline;
                try
                {
                    baseline = pair.Plugin.CaptureEditorState(pair.Group);
                }
                catch (Exception e)
                {
                    throw Stop(entries, resolve, saveCheckpoint, new EditorStateApplyException(key, "capture failed", e));
                }

                if (baseline == null)
                    continue;

                // Record and persist the baseline before applying so a crash
                // mid-apply still leaves a revertable entry on disk. The plugin
                // may flush its own assets (OpenXR calls SaveAssets), and the
                // ledger must reach disk before those writes do. Until the result
                // is captured the entry is Applying: what differs from its baseline
                // then is the Apply's own writes, which an undo removes whole.
                EditorStateLedger.Set(entries, key, baseline, baseline, EditorStatePhase.Applying);
                saveCheckpoint?.Invoke();

                try
                {
                    pair.Plugin.ApplyToEditor(forgeProfile, pair.Group, variant);
                }
                catch (Exception e)
                {
                    throw Stop(entries, resolve, saveCheckpoint, new EditorStateApplyException(key, "apply failed", e));
                }

                string expected;
                try
                {
                    expected = pair.Plugin.CaptureEditorState(pair.Group)
                               ?? throw new InvalidOperationException("The applied settings could not be captured.");
                }
                catch (Exception e)
                {
                    throw Stop(entries, resolve, saveCheckpoint, new EditorStateApplyException(key, "capture after apply failed", e));
                }
                EditorStateLedger.Set(entries, key, baseline, expected);
            }

            saveCheckpoint?.Invoke();
        }

        /// <summary>
        /// Ends an Apply that a plugin stopped: restores what the Apply wrote, newest
        /// first, and returns the exception to throw. An entry that cannot be
        /// restored stays recorded with its baseline, and the result then also holds
        /// its restore failure.
        /// </summary>
        static Exception Stop(List<EditorStateEntry> entries, Func<string, IForgeEditorApplicable> resolve,
            Action saveCheckpoint, EditorStateApplyException failure)
        {
            Debug.LogError($"{LogPrefix} {failure.Message}");
            // Nothing but this Apply ran since it reverted the previous profile, so
            // every recorded entry holds only the Apply's writes: undo them whole.
            foreach (var entry in entries)
                entry.phase = EditorStatePhase.Applying;
            try
            {
                RevertAll(entries, resolve);
                return failure;
            }
            catch (AggregateException notRestored)
            {
                return new AggregateException(failure.Message,
                    new Exception[] { failure }.Concat(notRestored.InnerExceptions));
            }
            finally
            {
                saveCheckpoint?.Invoke();
            }
        }

        /// <summary>True when a plugin's failure stopped an Apply; <paramref name="stopped"/> is that failure.</summary>
        public static bool StoppedByPlugin(Exception e, out EditorStateApplyException stopped)
        {
            stopped = e as EditorStateApplyException
                ?? (e as AggregateException)?.InnerExceptions.OfType<EditorStateApplyException>().FirstOrDefault();
            return stopped != null;
        }

        /// <summary>Suffix of a drift key whose Apply did not finish (see <see cref="EditorStatePhase.Applying"/>).</summary>
        public const string ApplyUnfinishedSuffix = " (Apply did not finish)";
        /// <summary>Suffix of a drift key whose build did not put its settings back or apply them again (see <see cref="EditorStatePhase.Suspended"/>).</summary>
        public const string BuildUnfinishedSuffix = " (build did not finish)";
        /// <summary>Suffix of a drift key whose plugin can no longer be resolved.</summary>
        public const string PluginMissingSuffix = " (plugin missing)";

        /// <summary>
        /// Keys whose current state differs from the expected snapshot. A key
        /// whose plugin can no longer be resolved, and one whose Apply or build
        /// did not finish, is always reported, with a suffix that says so.
        /// </summary>
        public static List<string> Drift(List<EditorStateEntry> entries, Func<string, IForgeEditorApplicable> resolve)
        {
            var drift = new List<string>();
            foreach (var entry in entries)
            {
                if (entry.phase != EditorStatePhase.Applied)
                {
                    drift.Add(entry.key + (entry.phase == EditorStatePhase.Applying ? ApplyUnfinishedSuffix : BuildUnfinishedSuffix));
                    continue;
                }
                if (!TryResolve(entry, resolve, out var plugin, out var group))
                {
                    drift.Add(entry.key + PluginMissingSuffix);
                    continue;
                }

                var current = TryCapture(plugin, group, entry.key);
                if (current != entry.expectedJson)
                    drift.Add(entry.key);
            }
            return drift;
        }

        /// <summary>
        /// Makes the current state the expected state. When the plugin can merge
        /// (see <see cref="IForgeEditorApplicable.MergeEditorState"/>), the accepted
        /// changes also go into the baseline, so undoing the Apply later keeps them;
        /// otherwise the baseline is untouched. An entry whose Apply or build did not
        /// finish is left as it is: a partly applied profile must not look applied.
        /// </summary>
        public static bool DismissDrift(List<EditorStateEntry> entries, Func<string, IForgeEditorApplicable> resolve)
        {
            var changed = false;
            foreach (var entry in entries)
            {
                if (entry.phase != EditorStatePhase.Applied)
                    continue;
                if (!TryResolve(entry, resolve, out var plugin, out var group))
                    continue;
                var current = TryCapture(plugin, group, entry.key);
                if (current == null || current == entry.expectedJson)
                    continue;
                try
                {
                    var merged = plugin.MergeEditorState(group, entry.baselineJson, entry.expectedJson, current);
                    if (merged != null)
                        entry.baselineJson = merged;
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogPrefix} {entry.key}: merge failed; the baseline is unchanged. {ForgeEditorState.MessageOrType(e)}");
                }
                entry.expectedJson = current;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Undoes every entry's Apply but keeps the entries, so the applied state
        /// can be resumed after a build. What was restored becomes the entry's
        /// baseline, so changes kept because they were made after the Apply (see
        /// <see cref="RestoreEntry"/>) stay kept when the profile is reverted later,
        /// and the entry is <see cref="EditorStatePhase.Suspended"/> until
        /// <see cref="ResumeAfterBuild"/>: should the editor quit during the build,
        /// undoing it restores that baseline whole instead of keeping the build's
        /// writes as changes made after the Apply.
        /// When an entry can't be restored, the build doesn't run, so the entries
        /// already restored get <paramref name="appliedProfile"/>'s settings again
        /// before this throws: a stopped build leaves the editor as it was.
        /// <paramref name="notReady"/> says why the profile can't be applied again (see
        /// <see cref="ForgePluginRegistry.BlockingError(ForgeProfile)"/>); those entries then
        /// stay restored, as without the profile.
        /// </summary>
        public static void SuspendForBuild(List<EditorStateEntry> entries, Func<string, IForgeEditorApplicable> resolve,
            ForgeProfile appliedProfile, string variant, string notReady = null)
        {
            var failures = new List<Exception>();
            var restoredEntries = new List<EditorStateEntry>();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var failure = RestoreEntry(entries[i], resolve, out var restored);
                if (failure != null)
                    failures.Add(failure);
                else
                {
                    entries[i].baselineJson = restored;
                    // An Apply that never finished stays so; it is not applied again after the build.
                    if (entries[i].phase == EditorStatePhase.Applied)
                        entries[i].phase = EditorStatePhase.Suspended;
                    restoredEntries.Add(entries[i]);
                }
            }

            if (failures.Count == 0)
                return;

            var reapplied = "";
            if (restoredEntries.Count > 0)
            {
                if (appliedProfile != null && notReady == null)
                {
                    // Restored newest first; applied again in the Apply's order, so a
                    // Play Mode mirror copies its platform as the plugins leave it.
                    restoredEntries.Reverse();
                    var reapply = ResumeAfterBuild(restoredEntries, appliedProfile, resolve, variant);
                    failures.AddRange(reapply.Failures);
                    reapplied = reapply.Failures.Count == 0
                        ? " The settings it had restored were applied again."
                        : " Some of the settings it had restored could not be applied again.";
                }
                else if (appliedProfile != null)
                    reapplied = " The settings restored so far stay as they were before the Apply, because " +
                                $"'{appliedProfile.DisplayName}' can't be applied: {notReady}";
                else
                    reapplied = " The applied profile is gone, so the settings it had restored stay as they were before the Apply.";
            }
            throw new AggregateException(
                "Could not restore the applied editor settings before the build, so the build did not start." + reapplied +
                " Use Revert to Baseline in the Build Forge window: it restores what it can and offers to stop tracking the rest.",
                failures);
        }

        /// <summary>
        /// Re-applies the applied profile for every entry, in the Apply's order, and
        /// recaptures expected state; an entry re-applied this way is
        /// <see cref="EditorStatePhase.Applied"/> again.
        /// Some entries are not re-applied; they stay unfinished and are reported in
        /// the failures: one whose Apply never finished (it isn't completed behind
        /// the user's back), one the build left in an unknown state (see
        /// <see cref="BuildLeftUnknown"/>), one whose re-apply fails (it holds what
        /// that wrote), and a Play Mode mirror after any platform entry of those,
        /// since it copies its platform as the plugins leave it.
        /// <paramref name="notRestored"/> holds the ledger keys (plugin type and
        /// built group) whose settings could not be put back after the build.
        /// A null profile (asset deleted during the build, or one whose Apply would
        /// stop) can't be applied again: the entries the build left in an unknown
        /// state stay, reported, for Revert to Baseline, and the rest are cleared,
        /// their baselines already restored by <see cref="SuspendForBuild"/>.
        /// </summary>
        public static ResumeResult ResumeAfterBuild(
            List<EditorStateEntry> entries, ForgeProfile appliedProfile, Func<string, IForgeEditorApplicable> resolve,
            string variant = null, ICollection<string> notRestored = null)
        {
            var result = new ResumeResult { Failures = new List<Exception>() };
            if (appliedProfile == null)
            {
                var removed = entries.RemoveAll(e => !BuildLeftUnknown(e, resolve, notRestored));
                foreach (var kept in entries)
                    result.Failures.Add(new InvalidOperationException(
                        $"{kept.key}: its settings could not be put back after the build, so it stays recorded for Revert to Baseline."));
                result.Cleared = removed > 0 && entries.Count == 0;
                result.Changed = removed > 0 && entries.Count > 0;
                return result;
            }

            var platformIncomplete = false;
            foreach (var entry in entries)
            {
                var resolved = TryResolve(entry, resolve, out var plugin, out var group);
                var mirror = resolved && plugin is IForgePlayModeMirror;
                if (entry.phase == EditorStatePhase.Applying)
                {
                    platformIncomplete |= !mirror;
                    result.Failures.Add(new InvalidOperationException(
                        $"{entry.key}: not applied again, because its Apply did not finish."));
                    continue;
                }
                if (!resolved)
                {
                    platformIncomplete = true;
                    result.Failures.Add(new InvalidOperationException(
                        $"{entry.key}: plugin not found; cannot re-apply its settings."));
                    continue;
                }
                if (BuildLeftUnknown(entry, resolve, notRestored))
                {
                    platformIncomplete |= !mirror;
                    result.Failures.Add(new InvalidOperationException(
                        $"{entry.key}: not applied again, because its settings could not be put back after the build."));
                    continue;
                }
                if (mirror && platformIncomplete)
                {
                    result.Failures.Add(new InvalidOperationException(
                        $"{entry.key}: not applied again, because the platform settings it copies were not."));
                    continue;
                }

                try
                {
                    plugin.ApplyToEditor(appliedProfile, group, variant);
                    var expected = plugin.CaptureEditorState(group);
                    if (expected == null)
                        throw new InvalidOperationException("The re-applied settings could not be captured.");

                    if (expected != entry.expectedJson || entry.phase != EditorStatePhase.Applied)
                    {
                        entry.expectedJson = expected;
                        entry.phase = EditorStatePhase.Applied;
                        result.Changed = true;
                    }
                }
                catch (Exception e)
                {
                    platformIncomplete |= !mirror;
                    Debug.LogError($"{LogPrefix} {entry.key}: re-apply after build failed. {ForgeEditorState.MessageOrType(e)}");
                    result.Failures.Add(new InvalidOperationException(
                        $"{entry.key}: re-apply after build failed.", e));
                }
            }
            return result;
        }

        /// <summary>
        /// Whether the build left <paramref name="entry"/>'s settings in an unknown state:
        /// its plugin could not put them back (<paramref name="notRestored"/> holds the
        /// ledger keys of those), or it is a Play Mode mirror and its group, Standalone,
        /// was left so, since a mirror copies into the settings the plugins restore there.
        /// </summary>
        static bool BuildLeftUnknown(EditorStateEntry entry, Func<string, IForgeEditorApplicable> resolve,
            ICollection<string> notRestored)
        {
            if (notRestored == null || notRestored.Count == 0)
                return false;
            if (notRestored.Contains(entry.key))
                return true;
            return TryResolve(entry, resolve, out var plugin, out var group) && plugin is IForgePlayModeMirror
                && notRestored.Any(key => EditorStateLedger.TryParseKey(key, out _, out var failed) && failed == group);
        }

        /// <summary>
        /// Undoes an entry's Apply. While the settings are as the Apply left them,
        /// that is the baseline. Once they changed after the Apply, as with a git
        /// pull, the baseline would undo those changes too, so the plugin merges
        /// the three states to undo only the Apply (see
        /// <see cref="IForgeEditorApplicable.MergeEditorState"/>). An entry whose
        /// Apply or build did not finish has no state the Apply left to tell
        /// changes from, so its baseline is restored whole. <paramref name="restored"/>
        /// is the snapshot written.
        /// </summary>
        static Exception RestoreEntry(EditorStateEntry entry, Func<string, IForgeEditorApplicable> resolve, out string restored)
        {
            restored = null;
            if (!TryResolve(entry, resolve, out var plugin, out var group))
            {
                Debug.LogWarning($"{LogPrefix} {entry.key}: plugin not found, cannot restore its settings.");
                return new EditorStateRestoreException(entry.key, "plugin not found; cannot restore its settings.");
            }

            try
            {
                var json = UndoApply(entry, plugin, group);
                plugin.RestoreEditorState(group, json);
                restored = json;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} {entry.key}: restore failed. {ForgeEditorState.MessageOrType(e)}");
                return new EditorStateRestoreException(entry.key, "restore failed.", e);
            }

            return null;
        }

        static string UndoApply(EditorStateEntry entry, IForgeEditorApplicable plugin, BuildTargetGroup group)
        {
            if (entry.phase != EditorStatePhase.Applied)
                return entry.baselineJson;

            var current = TryCapture(plugin, group, entry.key);
            if (current == null || current == entry.expectedJson || current == entry.baselineJson)
                return entry.baselineJson;

            var merged = plugin.MergeEditorState(group, entry.baselineJson, entry.expectedJson, current);
            if (merged == null)
            {
                Debug.LogWarning($"{LogPrefix} {entry.key}: the settings changed after the Apply, and the plugin can't " +
                                 "tell those changes from the Apply's, so restoring the state from before the Apply may undo them.");
                return entry.baselineJson;
            }
            Debug.Log($"{LogPrefix} {entry.key}: kept the changes made to the settings after the Apply and undid only the Apply.");
            return merged;
        }

        static bool TryResolve(
            EditorStateEntry entry, Func<string, IForgeEditorApplicable> resolve,
            out IForgeEditorApplicable plugin, out BuildTargetGroup group)
        {
            plugin = null;
            if (!EditorStateLedger.TryParseKey(entry.key, out var typeName, out group))
                return false;
            plugin = resolve(typeName);
            return plugin != null;
        }

        static string TryCapture(IForgeEditorApplicable plugin, BuildTargetGroup group, string key)
        {
            try
            {
                return plugin.CaptureEditorState(group);
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} {key}: capture failed. {ForgeEditorState.MessageOrType(e)}");
                return null;
            }
        }
    }
}
