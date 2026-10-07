using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>One file a build left in its destination, relative to it, with the size and time it had then.</summary>
    [Serializable]
    internal sealed class RecordedFile
    {
        public string path;
        public long size;
        public long lastWriteUtcTicks;
    }

    /// <summary>
    /// Per user: the files and folders the last build of each Unity Build
    /// Profile and variant produced in its destination, so the next build can
    /// remove exactly those (see <see cref="BuildOutputDirectory"/>). Kept under
    /// UserSettings so nothing is written into the build folder and shipped
    /// with it. Each record names the project folder it was made in; records
    /// from another folder (a copied or moved project) are dropped unused. A
    /// missing or lost record means less of that profile's output is cleaned up;
    /// it also stops protecting files it shares with other profiles, which their
    /// own next cleanup removes if their build produced them unchanged.
    /// </summary>
    [FilePath("UserSettings/ForgeBuildOutputs.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class BuildOutputLedger : ScriptableSingleton<BuildOutputLedger>
    {
        [Serializable]
        sealed class Entry
        {
            public string buildProfileGuid;
            public string variant;
            public string projectRoot;
            public string destination;
            public List<RecordedFile> files = new();
            public List<string> folders = new();
        }

        [SerializeField] List<Entry> entries = new();

        static string VariantKey(string variant) => variant ?? "";

        static bool Matches(Entry entry, string buildProfileGuid, string variant)
            => entry.buildProfileGuid == buildProfileGuid && entry.variant == VariantKey(variant);

        static string Describe(Entry entry)
            => $"{entry.buildProfileGuid}{(string.IsNullOrEmpty(entry.variant) ? "" : " " + entry.variant)} in {entry.destination}";

        /// <summary>The files recorded for the profile and variant, or null when there is no record.</summary>
        internal IReadOnlyList<RecordedFile> Find(string buildProfileGuid, string variant,
            out string destination, out IReadOnlyList<string> folders)
        {
            var entry = entries.Find(e => Matches(e, buildProfileGuid, variant));
            destination = entry?.destination;
            folders = entry?.folders;
            return entry?.files;
        }

        /// <summary>
        /// What other profiles or variants recorded in the same destination, by
        /// relative path. A file counts as theirs only while their record
        /// matches it (two Windows players in one folder share UnityPlayer.dll).
        /// </summary>
        internal Dictionary<string, List<RecordedFile>> RecordsOfOthers(string destination, string buildProfileGuid, string variant)
        {
            var claims = new Dictionary<string, List<RecordedFile>>(BuildOutputDirectory.PathComparer);
            foreach (var entry in entries)
            {
                if (Matches(entry, buildProfileGuid, variant) || entry.files == null
                    || !BuildOutputDirectory.SamePath(entry.destination, destination))
                    continue;
                foreach (var file in entry.files)
                {
                    if (string.IsNullOrEmpty(file?.path))
                        continue;
                    if (!claims.TryGetValue(file.path, out var list))
                        claims[file.path] = list = new List<RecordedFile>();
                    list.Add(file);
                }
            }
            return claims;
        }

        /// <summary>
        /// Forgets records made in another project folder: a copied project
        /// carries its UserSettings along, and the copy must never clean the
        /// original's output. Returns a description of each dropped record.
        /// </summary>
        internal List<string> DropForeign(string projectRoot)
            => Drop(entry => !BuildOutputDirectory.SamePath(entry.projectRoot, projectRoot));

        /// <summary>
        /// Forgets records whose Unity Build Profile or variant no longer exists,
        /// so they stop protecting files that a live profile also produces.
        /// Returns a description of each dropped record.
        /// </summary>
        internal List<string> DropStale(Func<string, string, bool> isLive)
            => Drop(entry => !isLive(entry.buildProfileGuid, entry.variant));

        List<string> Drop(Func<Entry, bool> shouldDrop)
        {
            var dropped = new List<string>();
            entries.RemoveAll(entry =>
            {
                if (entry == null)
                    return true;
                if (!shouldDrop(entry))
                    return false;
                dropped.Add(Describe(entry));
                return true;
            });
            if (dropped.Count > 0)
                Save(true);
            return dropped;
        }

        /// <summary>Writes the ledger to UserSettings (ScriptableSingleton.Save is protected).</summary>
        internal void Persist() => Save(true);

        /// <summary>Replaces the record for the profile and variant; an empty file list removes it.</summary>
        internal void Set(string buildProfileGuid, string variant, string projectRoot, string destination,
            List<RecordedFile> files, List<string> folders = null)
        {
            entries.RemoveAll(e => e == null || Matches(e, buildProfileGuid, variant));
            if (files != null && files.Count > 0)
                entries.Add(new Entry
                {
                    buildProfileGuid = buildProfileGuid,
                    variant = VariantKey(variant),
                    projectRoot = projectRoot,
                    destination = destination,
                    files = files,
                    folders = folders ?? new List<string>(),
                });
            Save(true);
        }
    }
}
