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
        /// <summary>Some entry's expected snapshot changed; the caller should save.</summary>
        public bool Changed;
        /// <summary>The applied profile no longer exists; entries were cleared and the caller should clear the applied GUID.</summary>
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
            Reason = inner == null ? reason : $"{reason} {inner.Message}";
        }
    }

    /// <summary>A plugin's capture or apply that failed and so stopped an Apply.</summary>
    internal sealed class EditorStateApplyException : InvalidOperationException
    {
        /// <summary>The ledger key (plugin type name and target group).</summary>
        public string Key { get; }
        /// <summary>The plugin's own message.</summary>
        public string Reason { get; }

        public EditorStateApplyException(string key, string what, Exception inner)
            : base($"{key}: {what}. {inner.Message}", inner)
        {
            Key = key;
            Reason = inner.Message;
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
                // ledger must reach disk before those writes do.
                EditorStateLedger.Set(entries, key, baseline, baseline);
                saveCheckpoint?.Invoke();

                try
                {
                    pair.Plugin.ApplyToEditor(forgeProfile, pair.Group, variant);
                }
                catch (Exception e)
                {
                    throw Stop(entries, resolve, saveCheckpoint, new EditorStateApplyException(key, "apply failed", e));
                }

                var expected = TryCapture(pair.Plugin, pair.Group, key) ?? baseline;
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

        /// <summary>
        /// Keys whose current state differs from the expected snapshot. A key
        /// whose plugin can no longer be resolved is reported with a suffix.
        /// </summary>
        public static List<string> Drift(List<EditorStateEntry> entries, Func<string, IForgeEditorApplicable> resolve)
        {
            var drift = new List<string>();
            foreach (var entry in entries)
            {
                if (!TryResolve(entry, resolve, out var plugin, out var group))
                {
                    drift.Add(entry.key + " (plugin missing)");
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
        /// otherwise the baseline is untouched.
        /// </summary>
        public static bool DismissDrift(List<EditorStateEntry> entries, Func<string, IForgeEditorApplicable> resolve)
        {
            var changed = false;
            foreach (var entry in entries)
            {
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
                    Debug.LogError($"{LogPrefix} {entry.key}: merge failed; the baseline is unchanged. {e.Message}");
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
        /// <see cref="RestoreEntry"/>) stay kept when the profile is reverted later.
        /// </summary>
        public static void SuspendForBuild(List<EditorStateEntry> entries, Func<string, IForgeEditorApplicable> resolve)
        {
            var failures = new List<Exception>();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var failure = RestoreEntry(entries[i], resolve, out var restored);
                if (failure != null)
                    failures.Add(failure);
                else
                    entries[i].baselineJson = restored;
            }

            if (failures.Count > 0)
                throw new AggregateException(
                    "Could not restore the applied editor settings before the build. Use Revert to Baseline in the " +
                    "Build Forge window: it restores what it can and offers to stop tracking the rest.", failures);
        }

        /// <summary>
        /// Re-applies the applied profile for every entry and recaptures expected
        /// state. A null profile (asset deleted during the build) clears the entries;
        /// the baselines are already restored by <see cref="SuspendForBuild"/>.
        /// </summary>
        public static ResumeResult ResumeAfterBuild(
            List<EditorStateEntry> entries, ForgeProfile appliedProfile, Func<string, IForgeEditorApplicable> resolve,
            string variant = null)
        {
            var result = new ResumeResult { Failures = new List<Exception>() };
            if (appliedProfile == null)
            {
                result.Cleared = entries.Count > 0;
                entries.Clear();
                return result;
            }

            foreach (var entry in entries)
            {
                if (!TryResolve(entry, resolve, out var plugin, out var group))
                {
                    result.Failures.Add(new InvalidOperationException(
                        $"{entry.key}: plugin not found; cannot re-apply its settings."));
                    continue;
                }

                try
                {
                    plugin.ApplyToEditor(appliedProfile, group, variant);
                    var expected = plugin.CaptureEditorState(group);
                    if (expected == null)
                        throw new InvalidOperationException("The re-applied settings could not be captured.");

                    if (expected != entry.expectedJson)
                    {
                        entry.expectedJson = expected;
                        result.Changed = true;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogPrefix} {entry.key}: re-apply after build failed. {e.Message}");
                    result.Failures.Add(new InvalidOperationException(
                        $"{entry.key}: re-apply after build failed.", e));
                }
            }
            return result;
        }

        /// <summary>
        /// Undoes an entry's Apply. While the settings are as the Apply left them,
        /// that is the baseline. Once they changed after the Apply, as with a git
        /// pull, the baseline would undo those changes too, so the plugin merges
        /// the three states to undo only the Apply (see
        /// <see cref="IForgeEditorApplicable.MergeEditorState"/>). <paramref name="restored"/>
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
                Debug.LogError($"{LogPrefix} {entry.key}: restore failed. {e.Message}");
                return new EditorStateRestoreException(entry.key, "restore failed.", e);
            }

            return null;
        }

        static string UndoApply(EditorStateEntry entry, IForgeEditorApplicable plugin, BuildTargetGroup group)
        {
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
                Debug.LogError($"{LogPrefix} {key}: capture failed. {e.Message}");
                return null;
            }
        }
    }
}
