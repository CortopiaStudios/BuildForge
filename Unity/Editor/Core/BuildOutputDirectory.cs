using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Removes what the previous build of a profile and variant left in its
    /// destination. After each build the files and folders it produced are
    /// recorded per user (<see cref="BuildOutputLedger"/>): whatever is new in
    /// the destination since just before the build, plus the build report's
    /// entries there that the build rewrote. Report entries alone are not
    /// enough: packages write files the report never lists, and on Android the
    /// IL2CPP backup folder is missing from it. Before the next build exactly
    /// those files are deleted, and only while they are still inside the
    /// destination, outside protected folders, not recorded by another profile
    /// with the same content and unchanged since. The artifact file (APK, AAB,
    /// Windows or Linux executable) is always removed first so a failed build
    /// never leaves an old one behind; a macOS .app bundle and directory outputs
    /// are cleaned only from the record. Files that land in a shared destination
    /// while a build runs count as that build's output. See ARCHITECTURE.md,
    /// Build Runner and "BuildReport.GetFiles() Is Not the Build's Output".
    /// </summary>
    internal static class BuildOutputDirectory
    {
        static readonly StringComparison PathComparison = Application.platform == RuntimePlatform.LinuxEditor
            ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        internal static readonly StringComparer PathComparer = Application.platform == RuntimePlatform.LinuxEditor
            ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        static readonly string[] ProjectFolders = { "Assets", "Packages", "ProjectSettings", "UserSettings", "Library", "Temp", "Logs" };

        // FAT stores local time: a daylight-saving change shifts every write time by exactly one hour.
        const long OneHourTicks = 36_000_000_000L;

        static bool IsWindows => Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>Folders cleanup and recording must never reach into.</summary>
        internal sealed class Roots
        {
            public string Project;
            public string Repository;
            public string Package;
            public string Editor;

            bool normalized;

            /// <summary>
            /// A copy with every root in normalized form (full, long names), made
            /// once per operation so per-file checks never re-normalize them.
            /// </summary>
            public Roots Normalized()
            {
                if (normalized)
                    return this;
                return new Roots
                {
                    Project = Normalize(Project),
                    Repository = string.IsNullOrWhiteSpace(Repository) ? null : Normalize(Repository),
                    Package = string.IsNullOrWhiteSpace(Package) ? null : Normalize(Package),
                    Editor = string.IsNullOrWhiteSpace(Editor) ? null : Normalize(Editor),
                    normalized = true,
                };
            }

            public static Roots Current() => new Roots
            {
                Project = Path.GetDirectoryName(Application.dataPath),
                Repository = Plugins.GitHelper.Run("rev-parse --show-toplevel"),
                Package = PackageInfo.FindForAssembly(typeof(BuildOutputDirectory).Assembly)?.resolvedPath,
                Editor = EditorInstallation(),
            };

            // The version folder of the running editor: it holds the editor and
            // its platform modules, whose files a failed build's report lists.
            static string EditorInstallation()
            {
                var folder = Path.GetDirectoryName(EditorApplication.applicationPath);
                return string.Equals(Path.GetFileName(folder), "Editor", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(folder) : folder;
            }
        }

        internal readonly struct FileStamp : IEquatable<FileStamp>
        {
            public readonly long Size;
            public readonly long Ticks;
            public FileStamp(long size, long ticks) { Size = size; Ticks = ticks; }
            public bool Equals(FileStamp other) => Size == other.Size && Ticks == other.Ticks;
            public override bool Equals(object obj) => obj is FileStamp other && Equals(other);
            public override int GetHashCode() => Size.GetHashCode() ^ Ticks.GetHashCode();
        }

        /// <summary>What a destination held: files by relative path, folders, and folders that could not be read.</summary>
        internal sealed class Listing
        {
            public readonly Dictionary<string, FileStamp> Files = new(PathComparer);
            public readonly HashSet<string> Folders = new(PathComparer);
            public readonly List<string> Unreadable = new();
        }

        /// <summary>What one build produced, relative to its destination.</summary>
        internal sealed class BuildRecord
        {
            public readonly List<RecordedFile> Files = new();
            public readonly List<string> Folders = new();
        }

        /// <summary>What <see cref="Prepare"/> hands to <see cref="Record"/> across the build.</summary>
        internal sealed class Preparation
        {
            public string BuildProfileGuid;
            public string Variant;
            public string Destination;
            public Roots Roots;
            public Listing Before;
        }

        /// <summary>
        /// Before a build: deletes the previous build's files and lists what the
        /// destination holds, so <see cref="Record"/> can tell what this build
        /// added. Throws, aborting the build, when the destination is unsafe or
        /// unreadable, a file in it cannot be deleted, or a folder blocks the artifact.
        /// </summary>
        public static Preparation Prepare(string buildProfileGuid, string variant, string outputPath, BuildTarget target)
        {
            var roots = Roots.Current();
            var export = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            var destination = Resolve(outputPath, target, export, roots.Project);
            var artifact = ArtifactPath(outputPath, target, export, roots.Project);
            return PrepareIn(BuildOutputLedger.instance, buildProfileGuid, variant, destination, artifact,
                artifactIsFolder: target == BuildTarget.StandaloneOSX, roots, IsLiveRecord);
        }

        /// <summary><see cref="Prepare"/> without Unity lookups, for tests.</summary>
        internal static Preparation PrepareIn(BuildOutputLedger ledger, string buildProfileGuid, string variant,
            string destination, string artifact, bool artifactIsFolder, Roots roots, Func<string, string, bool> isLive)
        {
            roots = roots.Normalized();
            destination = ValidateDestination(destination, roots);
            if (artifact != null)
                artifact = ExpandShortNames(Path.GetFullPath(artifact));

            foreach (var dropped in ledger.DropForeign(roots.Project))
                Debug.Log($"[Build Forge] Forgot a build record made in another project folder, such as the original of a copied project ({dropped}); its files are left in place.");
            foreach (var dropped in ledger.DropStale(isLive))
                Debug.Log($"[Build Forge] Forgot the build record of a Unity Build Profile or variant that no longer exists ({dropped}); its files are left in place.");

            var usedRecord = Clean(ledger, buildProfileGuid, variant, destination, artifact, roots);

            // Unity cannot write the artifact where a folder is; it would find out
            // only after the whole build. Typically an old Android export folder
            // that Android Studio or Gradle added files to.
            if (artifact != null && !artifactIsFolder && Directory.Exists(artifact))
            {
                var entries = new DirectoryInfo(artifact).EnumerateFileSystemInfos().Take(5).Select(e => e.Name).ToList();
                var held = entries.Count == 0 ? ""
                    : usedRecord ? $" and still holds files Build Forge did not remove ({string.Join(", ", entries)}); recorded files it left are named in the warning above"
                    : $" and Build Forge has no record of what the previous build wrote there ({string.Join(", ", entries)})";
                throw new IOException($"Build Forge cannot build '{artifact}': a folder is in the way{held}. Remove the folder, then build again.");
            }

            try { Directory.CreateDirectory(destination); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new IOException($"Build Forge could not create build destination '{destination}': {e.Message} Build aborted.", e);
            }

            var before = List(destination, strict: true);
            if (before.Unreadable.Count > 0)
                Debug.LogWarning($"[Build Forge] Could not read {before.Unreadable.Count} folder(s) in '{destination}' " +
                                 $"({string.Join(", ", before.Unreadable.Take(5))}); files appearing there during this build are not recorded.");
            return new Preparation
            {
                BuildProfileGuid = buildProfileGuid,
                Variant = variant,
                Destination = destination,
                Roots = roots,
                Before = before,
            };
        }

        /// <summary>
        /// After a build, successful or not: records what the build produced.
        /// <paramref name="report"/> is null when BuildPlayer threw.
        /// </summary>
        public static void Record(Preparation preparation, BuildReport report)
        {
            var reportPaths = report != null ? report.GetFiles().Select(f => f.path) : Enumerable.Empty<string>();
            RecordIn(BuildOutputLedger.instance, preparation, reportPaths);
        }

        internal static void RecordIn(BuildOutputLedger ledger, Preparation preparation, IEnumerable<string> reportPaths)
        {
            var record = ComputeRecord(preparation.Destination, preparation.Before, reportPaths, preparation.Roots);
            ledger.Set(preparation.BuildProfileGuid, preparation.Variant, preparation.Roots.Project,
                preparation.Destination, record.Files, record.Folders);
        }

        /// <returns>True when a record of the previous build was used.</returns>
        static bool Clean(BuildOutputLedger ledger, string buildProfileGuid, string variant, string destination,
            string artifact, Roots roots)
        {
            var recorded = ledger.Find(buildProfileGuid, variant, out var recordedDestination, out var recordedFolders);
            var recordRoot = destination;
            var elsewhere = false;
            if (recorded != null && !SamePath(recordedDestination, destination))
            {
                // Remove the previous build's files where they were recorded, with
                // the same checks as always; resolving them against the new
                // destination could hit a copy of that build.
                try
                {
                    recordRoot = ValidateDestination(recordedDestination, roots);
                    var foreign = ForeignProjectOrCheckout(recordRoot, roots);
                    if (foreign != null)
                        throw new InvalidOperationException($"it is inside another Unity project or Git checkout, '{foreign}'");
                    elsewhere = true;
                }
                catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is IOException)
                {
                    Debug.Log($"[Build Forge] The build destination changed from '{recordedDestination}' to '{destination}'; " +
                              $"the previous build's files there are left in place ({e.Message}).");
                    recorded = null;
                    recordedFolders = null;
                    recordRoot = destination;
                }
            }

            if (recorded != null && (!VolumeReachable(recordRoot) || !Directory.Exists(recordRoot) || !Readable(recordRoot)))
            {
                // An offline share, an unplugged drive, a folder behind an
                // unmounted link or a link whose target is gone: every file would
                // read as gone and the record would be lost. A deliberately
                // deleted destination is harmless too: this build's own record
                // replaces the kept one, and if the destination cannot be created
                // the build stops with its own error.
                if (!elsewhere)
                {
                    Debug.Log($"[Build Forge] '{recordRoot}' does not exist or is not reachable; nothing was removed, " +
                              "and the record of the previous build is kept until a build records its own output.");
                    return false;
                }
                // One record per profile and variant: the new destination's record replaces it.
                Debug.Log($"[Build Forge] The build destination changed from '{recordedDestination}' to '{destination}'; " +
                          "the previous destination is missing or not reachable, so its files are left in place.");
                recorded = null;
                recordedFolders = null;
                recordRoot = destination;
                elsewhere = false;
            }
            else if (elsewhere)
            {
                Debug.Log($"[Build Forge] The build destination changed from '{recordedDestination}' to '{destination}'; " +
                          "removing the previous build's files from where they were recorded.");
            }

            var remaining = new List<RecordedFile>();
            var skipped = new List<string>();
            var shared = new List<string>();
            int deleted;
            try
            {
                deleted = DeletePrevious(recordRoot, destination, artifact, recorded, recordedFolders,
                    ledger.RecordsOfOthers(recordRoot, buildProfileGuid, variant), roots, remaining, skipped, shared,
                    abortOnFailure: !elsewhere);
            }
            finally
            {
                // The file that could not be deleted and those after it stay
                // recorded, so a retry resumes; skipped files leave the record.
                ledger.Set(buildProfileGuid, variant, roots.Project, recordRoot, remaining,
                    remaining.Count > 0 ? recordedFolders?.ToList() : null);
            }

            if (skipped.Count > 0)
                Debug.LogWarning($"[Build Forge] Left {skipped.Count} file(s) from the previous build in '{recordRoot}' " +
                                 "because they changed since that build, could not be deleted or are no longer safe to delete: " +
                                 string.Join(", ", skipped.Take(10)) + (skipped.Count > 10 ? ", ..." : "") + ".");
            if (shared.Count > 0)
                Debug.Log($"[Build Forge] Kept {shared.Count} file(s) in '{recordRoot}' that another profile or variant also produced there.");
            if (artifact != null && Directory.Exists(artifact))
                Debug.Log($"[Build Forge] The artifact '{artifact}' is a folder; it is removed only through the build record.");
            Debug.Log(recorded == null
                ? $"[Build Forge] No record of a previous build in '{destination}'; removed only the artifact file, if present."
                : $"[Build Forge] Removed {deleted} file(s) of the previous build from '{recordRoot}'.");
            return recorded != null;
        }

        internal static string Resolve(string outputPath, BuildTarget target, bool exportAndroidProject, string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new InvalidOperationException("Build Forge requires a non-empty build output path.");

            var path = Path.GetFullPath(Path.Combine(projectRoot, outputPath));
            return NamesArtifact(target, exportAndroidProject) ? Path.GetDirectoryName(path) : path;
        }

        /// <summary>The artifact a build writes, or null for targets whose output path names a directory.</summary>
        internal static string ArtifactPath(string outputPath, BuildTarget target, bool exportAndroidProject, string projectRoot)
            => NamesArtifact(target, exportAndroidProject) && !string.IsNullOrWhiteSpace(outputPath)
                ? Path.GetFullPath(Path.Combine(projectRoot, outputPath))
                : null;

        // These paths name an artifact, with supporting files beside it. Other
        // targets (and Android exports) name the output directory.
        static bool NamesArtifact(BuildTarget target, bool exportAndroidProject)
        {
            switch (target)
            {
                case BuildTarget.Android when !exportAndroidProject:
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneLinux64:
                case BuildTarget.StandaloneOSX:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Rejects destinations cleanup and recording must stay out of: one
        /// containing the project or repository root, one inside a Unity
        /// project's own folders (this project's, or any project's found on disk,
        /// which also catches this project reached through a junction or alias),
        /// Git metadata, the package source or the editor installation, a Unity
        /// project or Git checkout itself, and a filesystem root. Short (8.3)
        /// names are expanded first. Links at or above the destination are fine:
        /// the build writes through them, so deleting through them hits the same files.
        /// </summary>
        internal static string ValidateDestination(string directory, Roots roots)
        {
            directory = Normalize(directory);
            var projectRoot = Normalize(roots.Project);
            if (IsWithin(projectRoot, directory) ||
                (!string.IsNullOrEmpty(roots.Repository) && IsWithin(Normalize(roots.Repository), directory)))
                Reject(directory, "it contains the project or repository root");

            if (!string.IsNullOrEmpty(roots.Package) &&
                (IsWithin(directory, Normalize(roots.Package)) || IsWithin(Normalize(roots.Package), directory)))
                Reject(directory, "it overlaps the Build Forge package source");

            if (!string.IsNullOrEmpty(roots.Editor) &&
                (IsWithin(directory, Normalize(roots.Editor)) || IsWithin(Normalize(roots.Editor), directory)))
                Reject(directory, "it overlaps the Unity editor installation");

            foreach (var name in ProjectFolders)
            {
                if (IsWithin(directory, Path.Combine(projectRoot, name)))
                    Reject(directory, $"it is inside the project's {name} directory");
            }

            if (Directory.Exists(directory) && IsUnityProjectOrCheckout(directory))
                Reject(directory, "it is a Unity project or a Git checkout");

            string below = null;
            for (var ancestor = new DirectoryInfo(directory); ancestor != null; below = ancestor.Name, ancestor = ancestor.Parent)
            {
                if (ancestor.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    Reject(directory, "it is inside Git metadata");
                if (below != null && ProjectFolders.Contains(below, StringComparer.OrdinalIgnoreCase) && IsUnityProject(ancestor.FullName))
                    Reject(directory, $"it is inside the {below} directory of the Unity project '{ancestor.FullName}'");
                if (ancestor.Parent == null && string.Equals(Normalize(ancestor.FullName), directory, PathComparison))
                    Reject(directory, "it is a filesystem root");
            }

            return directory;
        }

        /// <summary>
        /// Deletes the artifact file, then every recorded file that is still
        /// safe to delete, then the recorded folders that became empty.
        /// Recorded paths resolve against <paramref name="recordRoot"/>, where
        /// they were recorded; the artifact belongs to <paramref name="destination"/>.
        /// Files that changed since they were recorded, or that another profile
        /// produced with the same content, are left alone. On an error the file
        /// being deleted and all after it go into <paramref name="remaining"/>
        /// before the exception propagates; with <paramref name="abortOnFailure"/>
        /// off (cleaning a previous destination) a recorded file that cannot be
        /// deleted is only skipped. The artifact always aborts.
        /// </summary>
        internal static int DeletePrevious(string recordRoot, string destination, string artifactPath,
            IReadOnlyList<RecordedFile> recorded, IReadOnlyList<string> recordedFolders,
            IReadOnlyDictionary<string, List<RecordedFile>> recordedByOthers,
            Roots roots, List<RecordedFile> remaining, List<string> skipped, List<string> shared = null,
            bool abortOnFailure = true)
        {
            roots = roots.Normalized();
            recordRoot = Normalize(recordRoot);
            destination = Normalize(destination);
            var fat = IsFatVolume(recordRoot);
            var verdicts = new Dictionary<string, bool>(PathComparer);
            var deleted = 0;
            var index = 0;
            try
            {
                // The artifact is the exact file this build writes, recorded or
                // not; going first means a running player aborts the build before
                // anything else is touched.
                if (artifactPath != null)
                {
                    var artifact = new FileInfo(Path.GetFullPath(artifactPath));
                    if (artifact.Exists && !IsLink(artifact) && IsSafe(artifact.FullName, destination, roots))
                    {
                        Delete(artifact.FullName, destination);
                        deleted++;
                    }
                }

                for (; recorded != null && index < recorded.Count; index++)
                {
                    var file = recorded[index];
                    var full = ResolveRecorded(recordRoot, file?.path, roots, verdicts: verdicts);
                    if (full == null)
                    {
                        if (!string.IsNullOrEmpty(file?.path)) skipped.Add(file.path);
                        continue;
                    }
                    if (recordedByOthers != null && recordedByOthers.TryGetValue(file.path, out var claims)
                        && claims.Any(c => c != null && SameStamp(c.size, c.lastWriteUtcTicks, file.size, file.lastWriteUtcTicks, fat)))
                    {
                        shared?.Add(file.path);
                        continue;
                    }
                    var info = new FileInfo(full);
                    if (!info.Exists)
                        continue;
                    if (IsLink(info) || !SameStamp(info.Length, info.LastWriteTimeUtc.Ticks, file.size, file.lastWriteUtcTicks, fat))
                    {
                        skipped.Add(file.path);
                        continue;
                    }
                    try
                    {
                        Delete(full, recordRoot);
                        deleted++;
                    }
                    catch (IOException) when (!abortOnFailure)
                    {
                        skipped.Add(file.path);
                    }
                }
            }
            catch
            {
                if (recorded != null)
                    for (var i = index; i < recorded.Count; i++)
                        if (recorded[i] != null)
                            remaining.Add(recorded[i]);
                throw;
            }

            RemoveEmptyFolders(recordRoot, recordedFolders, roots, verdicts);

            // Switching an Android profile from export to APK: the old export
            // folder sits where the APK goes. It was this profile's destination.
            if (artifactPath != null && !SamePath(recordRoot, destination) && SamePath(recordRoot, artifactPath))
                RemoveIfEmpty(new DirectoryInfo(recordRoot));

            return deleted;
        }

        /// <summary>
        /// Same size and write time. On FAT volumes, which store local time, a
        /// difference of exactly one hour (a daylight-saving change) also counts.
        /// </summary>
        internal static bool SameStamp(long size, long ticks, long recordedSize, long recordedTicks, bool fatVolume)
            => size == recordedSize && (ticks == recordedTicks || fatVolume && Math.Abs(ticks - recordedTicks) == OneHourTicks);

        /// <summary>
        /// The record for a finished build: files and folders that are new since
        /// <paramref name="before"/>, plus report entries inside the destination
        /// that the build rewrote. An unchanged report entry is never recorded:
        /// a failed build's report can list source files, in the editor
        /// installation or anywhere else. Nothing below a folder that could not
        /// be read before the build is recorded, and neither is anything that is
        /// not safe to delete later.
        /// </summary>
        internal static BuildRecord ComputeRecord(string destination, Listing before, IEnumerable<string> reportPaths, Roots roots)
        {
            roots = roots.Normalized();
            destination = Normalize(destination);
            before ??= new Listing();
            var after = List(destination, strict: false);
            var files = new Dictionary<string, RecordedFile>(PathComparer);
            var verdicts = new Dictionary<string, bool>(PathComparer);
            bool Unknown(string relative) => before.Unreadable.Any(folder => IsWithinRelative(relative, folder));

            foreach (var path in reportPaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                string full;
                try
                {
                    full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(roots.Project, path));
                    if (!IsWithin(full, destination))
                        full = ExpandShortNames(full);
                }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { continue; }
                if (!IsWithin(full, destination) || string.Equals(full, destination, PathComparison))
                    continue;
                var relative = Relative(destination, full);
                if (after.Files.TryGetValue(relative, out var stamp) && !Unknown(relative)
                    && (!before.Files.TryGetValue(relative, out var previous) || !previous.Equals(stamp)))
                    files[relative] = new RecordedFile { path = relative, size = stamp.Size, lastWriteUtcTicks = stamp.Ticks };
            }

            foreach (var entry in after.Files)
            {
                if (before.Files.ContainsKey(entry.Key) || Unknown(entry.Key))
                    continue;
                files[entry.Key] = new RecordedFile { path = entry.Key, size = entry.Value.Size, lastWriteUtcTicks = entry.Value.Ticks };
            }

            var record = new BuildRecord();
            record.Files.AddRange(files.Values
                .Where(f => ResolveRecorded(destination, f.path, roots, verdicts: verdicts) != null)
                .OrderBy(f => f.path, StringComparer.Ordinal));
            record.Folders.AddRange(after.Folders
                .Where(folder => !before.Folders.Contains(folder) && !Unknown(folder))
                .Where(folder => ResolveRecorded(destination, folder, roots, isFolder: true, verdicts: verdicts) != null)
                .OrderBy(folder => folder, StringComparer.Ordinal));
            return record;
        }

        /// <summary>
        /// Lists the files and folders under <paramref name="directory"/>. Links
        /// are neither listed nor followed, and neither are Git metadata, nested
        /// Unity projects and nested checkouts. A subfolder that cannot be read
        /// is reported in <see cref="Listing.Unreadable"/>; with
        /// <paramref name="strict"/>, a destination that is missing or cannot be
        /// read throws instead of reading as empty.
        /// </summary>
        internal static Listing List(string directory, bool strict)
        {
            var listing = new Listing();
            directory = Normalize(directory);
            if (!Directory.Exists(directory))
            {
                if (strict)
                    throw new IOException($"Build Forge could not read build destination '{directory}'. Build aborted.");
                return listing;
            }

            var root = new DirectoryInfo(directory);
            var pending = new Stack<DirectoryInfo>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                List<FileSystemInfo> children;
                try { children = current.EnumerateFileSystemInfos().ToList(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
                {
                    if (ReferenceEquals(current, root))
                    {
                        if (strict)
                            throw new IOException($"Build Forge could not read build destination '{directory}': {e.Message} Build aborted.", e);
                        return new Listing();
                    }
                    listing.Unreadable.Add(Relative(directory, current.FullName));
                    continue;
                }

                foreach (var child in children)
                {
                    var kind = Classify(child);
                    if (kind == LinkKind.Link)
                        continue;
                    var relative = Relative(directory, child.FullName);
                    if (kind == LinkKind.Unknown)
                    {
                        // Its reparse tag could not be read. Before a build that
                        // must not read as "absent": it would look new afterwards.
                        if (strict)
                            listing.Unreadable.Add(relative);
                        continue;
                    }
                    if (child is DirectoryInfo folder)
                    {
                        if (folder.Name.Equals(".git", StringComparison.OrdinalIgnoreCase) || IsUnityProjectOrCheckout(folder.FullName))
                            continue;
                        listing.Folders.Add(relative);
                        pending.Push(folder);
                    }
                    else if (child is FileInfo file)
                    {
                        listing.Files[relative] = new FileStamp(file.Length, file.LastWriteTimeUtc.Ticks);
                    }
                }
            }
            return listing;
        }

        /// <summary>
        /// The full path of a recorded file or folder when it is still safe to
        /// delete: a relative path of plain names (no ".", "..", drive, stream or
        /// other invalid characters), inside <paramref name="recordRoot"/>, not
        /// below a link, a Unity project or a checkout inside it, and not in
        /// protected folders. Null otherwise. <paramref name="recordRoot"/> and
        /// <paramref name="roots"/> must already be normalized (callers do it once
        /// per operation); <paramref name="verdicts"/> caches folder checks.
        /// </summary>
        internal static string ResolveRecorded(string recordRoot, string relative, Roots roots, bool isFolder = false,
            Dictionary<string, bool> verdicts = null)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                return null;
            var invalid = Path.GetInvalidFileNameChars();
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(s => s.Length == 0 || s == "." || s == ".." || s.IndexOfAny(invalid) >= 0
                                  || s.Equals(".git", StringComparison.OrdinalIgnoreCase)))
                return null;
            string full;
            try { full = Path.GetFullPath(Path.Combine(recordRoot, relative)); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { return null; }
            if (!IsWithin(full, recordRoot) || string.Equals(full, recordRoot, PathComparison) || !IsSafe(full, recordRoot, roots))
                return null;

            // Every folder between the entry and the root must be a plain folder.
            var folder = isFolder ? full : Path.GetDirectoryName(full);
            for (var steps = 0; folder != null && !string.Equals(folder, recordRoot, PathComparison); steps++)
            {
                if (steps > segments.Length || !IsWithin(folder, recordRoot))
                    return null;
                if (verdicts == null || !verdicts.TryGetValue(folder, out var plain))
                {
                    var info = new DirectoryInfo(folder);
                    plain = !info.Exists || !(IsLink(info) || IsUnityProjectOrCheckout(folder));
                    if (verdicts != null) verdicts[folder] = plain;
                }
                if (!plain)
                    return null;
                folder = Path.GetDirectoryName(folder);
            }
            return full;
        }

        // Defence in depth; ValidateDestination already keeps destinations out of these.
        static bool IsSafe(string full, string root, Roots roots)
        {
            if (!IsWithin(full, root))
                return false;
            if (ProjectFolders.Any(name => IsWithin(full, Path.Combine(roots.Project, name))))
                return false;
            if (!string.IsNullOrEmpty(roots.Package) && IsWithin(full, roots.Package))
                return false;
            if (!string.IsNullOrEmpty(roots.Editor) && IsWithin(full, roots.Editor))
                return false;
            return !full.Substring(root.Length).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(s => s.Equals(".git", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The nearest Unity project or Git checkout at or above <paramref name="directory"/>
        /// that does not hold this project, or null. A previous destination inside
        /// one is never cleaned: it is someone else's output. The walk stops at the
        /// first folder that holds this project: from there up, this project's own
        /// folder, its repository (however Git spells it, nested or not, or with Git
        /// unavailable) and everything around them are not someone else's output.
        /// </summary>
        internal static string ForeignProjectOrCheckout(string directory, Roots roots)
        {
            var projectRoot = Normalize(roots.Project);
            for (var ancestor = new DirectoryInfo(Normalize(directory)); ancestor != null; ancestor = ancestor.Parent)
            {
                var path = Normalize(ancestor.FullName);
                if (IsWithin(projectRoot, path))
                    return null;
                if (IsUnityProjectOrCheckout(path))
                    return path;
            }
            return null;
        }

        // A link at the destination whose target is offline passes Directory.Exists.
        static bool Readable(string directory)
        {
            try
            {
                using var entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
                entries.MoveNext();
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                return false;
            }
        }

        static bool VolumeReachable(string path)
        {
            try
            {
                var volume = Path.GetPathRoot(path);
                return !string.IsNullOrEmpty(volume) && Directory.Exists(volume);
            }
            catch (Exception) { return false; }
        }

        static bool IsFatVolume(string path)
        {
            try
            {
                var volume = Path.GetPathRoot(path);
                return !string.IsNullOrEmpty(volume) && !volume.StartsWith(@"\\", StringComparison.Ordinal)
                    && new DriveInfo(volume).DriveFormat.StartsWith("FAT", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        static void Delete(string file, string root)
        {
            try
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new IOException($"Build Forge could not delete '{file}' from the previous build in '{root}' " +
                                      $"(is it open or running?): {e.Message} Build aborted.", e);
            }
        }

        /// <summary>Removes the recorded folders that are now empty, deepest first; never the root or a link.</summary>
        static void RemoveEmptyFolders(string recordRoot, IReadOnlyList<string> folders, Roots roots, Dictionary<string, bool> verdicts)
        {
            if (folders == null)
                return;
            foreach (var relative in folders.Where(f => !string.IsNullOrEmpty(f)).OrderByDescending(f => f.Length))
            {
                var full = ResolveRecorded(recordRoot, relative, roots, isFolder: true, verdicts: verdicts);
                if (full != null)
                    RemoveIfEmpty(new DirectoryInfo(full));
            }
        }

        static void RemoveIfEmpty(DirectoryInfo folder)
        {
            try
            {
                if (folder.Exists && !IsLink(folder) && !folder.EnumerateFileSystemInfos().Any())
                    folder.Delete(false);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        static bool IsUnityProject(string folder)
            => File.Exists(Path.Combine(folder, "ProjectSettings", "ProjectVersion.txt"));

        static bool IsUnityProjectOrCheckout(string folder)
            => IsUnityProject(folder) || Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git"));

        // ------------------------------------------------------------ Windows specifics

        /// <summary>
        /// True for symbolic links and junctions. On Windows only name-surrogate
        /// reparse points count: cloud-file placeholders (OneDrive and others)
        /// also carry the reparse attribute but are ordinary files and folders.
        /// An entry whose reparse tag cannot be read counts as a link.
        /// </summary>
        internal static bool IsLink(FileSystemInfo info) => Classify(info) != LinkKind.Plain;

        enum LinkKind { Plain, Link, Unknown }

        static LinkKind Classify(FileSystemInfo info)
        {
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                return LinkKind.Plain;
            if (!IsWindows)
                return LinkKind.Link;
            var tag = ReparseTag(info.FullName);
            return tag == null ? LinkKind.Unknown : IsNameSurrogate(tag.Value) ? LinkKind.Link : LinkKind.Plain;
        }

        /// <summary>IsReparseTagNameSurrogate: bit 29 marks tags that name another entry (symlink, mount point).</summary>
        internal static bool IsNameSurrogate(uint tag) => (tag & 0x20000000) != 0;

        static uint? ReparseTag(string path)
        {
            var handle = FindFirstFileW(ExtendedPath(path), out var data);
            if (handle == InvalidHandle)
                return null;
            FindClose(handle);
            return (data.dwFileAttributes & (uint)FileAttributes.ReparsePoint) != 0 ? data.dwReserved0 : (uint?)null;
        }

        // Unity is not long-path aware: without the \\?\ prefix Win32 calls fail
        // at 260 characters. The prefix also stops wildcard interpretation.
        internal static string ExtendedPath(string path)
        {
            path = path.Replace('/', '\\').TrimEnd('\\');
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
                return path;
            return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path.Substring(2) : @"\\?\" + path;
        }

        /// <summary>Expands short (8.3) names in the existing part of a Windows path, so name checks see the real names.</summary>
        internal static string ExpandShortNames(string path)
        {
            if (!IsWindows || string.IsNullOrEmpty(path) || path.IndexOf('~') < 0)
                return path;
            var existing = path;
            var rest = new List<string>();
            while (existing != null && !Directory.Exists(existing) && !File.Exists(existing))
            {
                rest.Insert(0, Path.GetFileName(existing));
                existing = Path.GetDirectoryName(existing);
            }
            if (existing == null)
                return path;
            var buffer = new StringBuilder(1024);
            var length = GetLongPathNameW(existing, buffer, (uint)buffer.Capacity);
            if (length == 0 || length >= buffer.Capacity)
                return path;
            var expanded = buffer.ToString();
            return rest.Count == 0 ? expanded : Path.Combine(new[] { expanded }.Concat(rest).ToArray());
        }

        static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Win32FindData
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindFirstFileW(string fileName, out Win32FindData data);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FindClose(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, uint bufferLength);

        // ------------------------------------------------------------ paths

        internal static bool SamePath(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            try { return string.Equals(Normalize(a), Normalize(b), PathComparison); }
            catch (Exception) { return false; }
        }

        static string Relative(string root, string full)
            => full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        static bool IsWithinRelative(string relative, string folder)
            => string.Equals(relative, folder, PathComparison)
               || relative.StartsWith(folder + Path.DirectorySeparatorChar, PathComparison)
               || relative.StartsWith(folder + Path.AltDirectorySeparatorChar, PathComparison);

        // Full path, no trailing separator, short (8.3) names expanded, so every
        // comparison sees the same spelling of the project, roots and destination.
        static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Build Forge requires a non-empty build destination directory.");
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return ExpandShortNames(fullPath.Length == root.Length
                ? fullPath : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        static bool IsWithin(string path, string parent)
            => string.Equals(path, parent, PathComparison) || path.StartsWith(
                parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison);

        static void Reject(string directory, string reason)
            => throw new InvalidOperationException(
                $"Build Forge refuses to remove previous build output in '{directory}': {reason}. " +
                "Choose a dedicated build directory or turn off Remove Previous Build Output for this profile.");

        // ------------------------------------------------------------ liveness

        // A record belongs to a Unity Build Profile that still exists and to the
        // default build or a variant that is still configured.
        static bool IsLiveRecord(string buildProfileGuid, string variant)
            => !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(buildProfileGuid))
               && (string.IsNullOrEmpty(variant) || BuildVariants.Configured.Contains(variant));
    }
}
