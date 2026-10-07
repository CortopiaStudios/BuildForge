using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// What editor Apply changed on each Unity Build Profile, per user: the
    /// profile's scripting defines just before and just after Build Forge wrote
    /// the variant, and the native build flags of entries recorded while Apply
    /// still wrote the rule's flags (pre-1.0). Revert, a variant switch and builds
    /// undo exactly that change, starting from the profile's current values, so
    /// edits made in Unity's Build Profiles window after Apply survive. A value
    /// the user has changed since keeps the user's value; a profile nobody
    /// touched returns to its exact previous content. Only an Apply that changes
    /// something leaves an entry.
    /// </summary>
    [FilePath("UserSettings/ForgeVariantDefines.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class VariantDefineLedger : ScriptableSingleton<VariantDefineLedger>
    {
        [Serializable]
        sealed class Entry
        {
            public string guid;
            public string[] originalDefines;
            public string[] writtenDefines;
            public string originalSettings;
            public string writtenSettings;

            // Entries saved before the ledger recorded what was written carry
            // neither list and are ignored (pre-1.0, no migration).
            public bool IsComplete => originalDefines != null && writtenDefines != null;
        }

        [SerializeField] List<Entry> entries = new();

        static string GuidFor(BuildProfile profile) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));

        Entry Find(BuildProfile profile)
        {
            var guid = GuidFor(profile);
            return string.IsNullOrEmpty(guid) ? null : entries.Find(e => e.guid == guid && e.IsComplete);
        }

        /// <summary>True when an Apply's change is recorded for the profile.</summary>
        internal bool HasEntry(BuildProfile profile) => Find(profile) != null;

        /// <summary>The ledger is keyed on the asset GUID, so an unsaved profile cannot take a variant.</summary>
        internal static void RequireSaved(BuildProfile profile)
        {
            if (string.IsNullOrEmpty(GuidFor(profile)))
                throw new InvalidOperationException("Save the Unity Build Profile before applying a variant.");
        }

        /// <summary>
        /// The profile's defines with the applied change undone: the user's own
        /// list, including edits made since Apply. The current list when nothing
        /// is applied.
        /// </summary>
        internal string[] UserDefines(BuildProfile profile)
        {
            var current = profile.scriptingDefines ?? Array.Empty<string>();
            var entry = Find(profile);
            return entry == null ? current : UndoDefines(current, entry.originalDefines, entry.writtenDefines);
        }

        /// <summary>
        /// Puts the native flags Build Forge changed back to their values before
        /// Apply, except where the user has changed them since; only entries
        /// recorded while Apply still wrote flags have any. In memory only; the
        /// caller saves or restores.
        /// </summary>
        internal void UndoBuildSettings(BuildProfile profile)
        {
            var entry = Find(profile);
            if (entry == null)
                return;
            VariantBuildSettings.Restore(profile, VariantBuildSettings.Undo(
                VariantBuildSettings.Capture(profile), entry.originalSettings, entry.writtenSettings));
        }

        /// <summary>
        /// Records what an Apply writes, replacing the previous entry. An Apply
        /// that changes nothing leaves no entry. The caller saves the profile at
        /// the user's values (the previous change undone) before this and writes
        /// the new change after it, so a crash at any point leaves either no
        /// change or a recorded one on disk.
        /// </summary>
        internal void Record(BuildProfile profile, string[] originalDefines, string originalSettings,
            string[] writtenDefines, string writtenSettings)
        {
            RequireSaved(profile);
            var guid = GuidFor(profile);
            entries.RemoveAll(e => e.guid == guid);
            originalDefines ??= Array.Empty<string>();
            writtenDefines ??= Array.Empty<string>();
            if (!originalDefines.SequenceEqual(writtenDefines) || originalSettings != writtenSettings)
                entries.Add(new Entry
                {
                    guid = guid,
                    originalDefines = originalDefines,
                    writtenDefines = writtenDefines,
                    originalSettings = originalSettings,
                    writtenSettings = writtenSettings,
                });
            Save(true);
        }

        /// <summary>
        /// Undoes the recorded change on the profile, saves it and forgets the
        /// entry. False when nothing is recorded for the profile.
        /// </summary>
        internal bool TryRestore(BuildProfile profile, out string[] defines)
        {
            defines = null;
            var guid = GuidFor(profile);
            var entry = Find(profile);
            if (entry == null)
            {
                if (!string.IsNullOrEmpty(guid) && entries.RemoveAll(e => e.guid == guid) > 0)
                    Save(true);
                return false;
            }

            defines = UndoDefines(profile.scriptingDefines ?? Array.Empty<string>(), entry.originalDefines, entry.writtenDefines);
            UndoBuildSettings(profile);
            profile.scriptingDefines = defines;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            entries.Remove(entry);
            Save(true);
            return true;
        }

        /// <summary>
        /// Undoes an Apply on a defines list. Untouched since Apply: the original
        /// list, exactly, so the asset returns to its previous content. Edited
        /// since: the current list without what Build Forge added and with what
        /// it removed, each put back after its original predecessor so the
        /// committed asset shows no reordering, and the user's own additions and
        /// removals survive. Never returns a BUILD_VARIANT_ define: those are
        /// applied state, and none stays on a profile that is not applied.
        /// </summary>
        internal static string[] UndoDefines(IReadOnlyList<string> current, IReadOnlyList<string> original,
            IReadOnlyList<string> written)
        {
            current ??= Array.Empty<string>();
            var before = (original ?? Array.Empty<string>()).ToList();
            written ??= Array.Empty<string>();

            List<string> result;
            if (current.SequenceEqual(written))
            {
                result = before;
            }
            else
            {
                var added = new HashSet<string>(written.Where(d => !before.Contains(d)), StringComparer.Ordinal);
                result = current.Where(d => !added.Contains(d)).ToList();
                for (var i = 0; i < before.Count; i++)
                {
                    var removed = before[i];
                    if (written.Contains(removed) || result.Contains(removed))
                        continue;
                    var insertAt = 0;
                    for (var j = i - 1; j >= 0; j--)
                    {
                        var at = result.IndexOf(before[j]);
                        if (at >= 0) { insertAt = at + 1; break; }
                    }
                    result.Insert(insertAt, removed);
                }
            }
            return result.Where(d => !BuildVariants.IsVariantDefine(d)).ToArray();
        }
    }
}
