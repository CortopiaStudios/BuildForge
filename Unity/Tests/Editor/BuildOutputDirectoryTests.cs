using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BuildForge.Editor.Core;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// Remove Previous Build Output deletes the previous build's recorded files
    /// and the artifact file, never anything the build did not produce:
    /// neighbours in a shared folder, files changed since, files already there
    /// before the build, paths outside the destination, files behind links or
    /// in nested projects, another project's output and files another profile
    /// produced all survive.
    /// </summary>
    public class BuildOutputDirectoryTests
    {
        string root;
        string project;
        string destination;
        BuildOutputDirectory.Roots roots;
        readonly List<string> links = new();
        string savedLedger;

        static bool Windows => Application.platform == RuntimePlatform.WindowsEditor;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "BuildForge-output-tests", System.Guid.NewGuid().ToString("N"));
            project = Path.Combine(root, "Checkout", "Unity Project");
            destination = Path.Combine(root, "Checkout", "Build", "Players", "Quest", "Default");
            Directory.CreateDirectory(project);
            // PrepareIn drops records of other project folders; keep the real
            // project's records intact whatever the tests do.
            savedLedger = EditorJsonUtility.ToJson(BuildOutputLedger.instance);
            roots = new BuildOutputDirectory.Roots
            {
                Project = project,
                Repository = Path.Combine(root, "Checkout"),
                Editor = Path.Combine(root, "Hub", "Editor", "6000.3.0f1"),
            };
        }

        [TearDown]
        public void TearDown()
        {
            EditorJsonUtility.FromJsonOverwrite(savedLedger, BuildOutputLedger.instance);
            BuildOutputLedger.instance.Persist();
            // Unlink links without recursion before removing this test's tree.
            foreach (var link in links)
            {
                if (Windows) Directory.Delete(link);
                else File.Delete(link);
            }
            links.Clear();
            Assert.That(Path.GetFullPath(root),
                Does.StartWith(Path.Combine(Path.GetTempPath(), "BuildForge-output-tests") + Path.DirectorySeparatorChar));
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(root, true);
            }
            catch (Exception) when (Windows)
            {
                // Paths past 260 characters: let the shell remove them.
                using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c rd /s /q \"{BuildOutputDirectory.ExtendedPath(root)}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                process.WaitForExit();
            }
        }

        // ------------------------------------------------------------ resolving

        [TestCase(BuildTarget.Android, "Game.apk", false)]
        [TestCase(BuildTarget.Android, "Game.aab", false)]
        [TestCase(BuildTarget.StandaloneWindows, "Game.exe", false)]
        [TestCase(BuildTarget.StandaloneWindows64, "Game.exe", false)]
        [TestCase(BuildTarget.StandaloneLinux64, "Game", false)]
        [TestCase(BuildTarget.StandaloneOSX, "Game.app", false)]
        public void ArtifactTargets_ResolveToTheParentAndNameTheArtifact(BuildTarget target, string artifact, bool export)
        {
            var output = Path.Combine("..", "Build", "Players", "Quest", "Default", artifact);
            Assert.AreEqual(destination, BuildOutputDirectory.Resolve(output, target, export, project));
            Assert.AreEqual(Path.Combine(destination, artifact), BuildOutputDirectory.ArtifactPath(output, target, export, project));
        }

        [TestCase(BuildTarget.WebGL, "Web", false)]
        [TestCase(BuildTarget.iOS, "Xcode", false)]
        [TestCase(BuildTarget.Android, "Export.apk", true)]
        public void DirectoryTargets_ResolveToThemselvesWithoutArtifact(BuildTarget target, string folder, bool export)
        {
            var directory = Path.Combine(project, "Builds", folder);
            Assert.AreEqual(directory, BuildOutputDirectory.Resolve(directory, target, export, project));
            Assert.IsNull(BuildOutputDirectory.ArtifactPath(directory, target, export, project));
        }

        // ------------------------------------------------------------ first build

        [Test]
        public void WithoutRecord_OnlyTheArtifactFileIsDeleted()
        {
            // An APK written straight into a shared folder such as the Desktop.
            var artifact = Write(Path.Combine(destination, "Game.apk"));
            var neighbour = Write(Path.Combine(destination, "Notes.txt"));
            var nested = Write(Path.Combine(destination, "Photos", "Holiday.jpg"));

            Delete(null, null, artifact);

            Assert.IsFalse(File.Exists(artifact));
            Assert.IsTrue(File.Exists(neighbour));
            Assert.IsTrue(File.Exists(nested));
            Assert.IsTrue(Directory.Exists(destination));
        }

        [Test]
        public void ArtifactThatIsAFolder_IsLeftAlone()
        {
            var bundle = Path.Combine(destination, "Game.app");
            var inside = Write(Path.Combine(bundle, "Contents", "Info.plist"));
            Delete(null, null, bundle);
            Assert.IsTrue(File.Exists(inside));
        }

        // ------------------------------------------------------------ recorded builds

        [Test]
        public void RecordedFiles_AreDeleted_NeighboursAndDestinationSurvive()
        {
            var neighbour = Write(Path.Combine(destination, "Keep.txt"));
            var before = BuildOutputDirectory.List(destination, strict: true);
            var files = new[]
            {
                Write(Path.Combine(destination, "Game.exe")),
                Write(Path.Combine(destination, "Game_Data", "Managed", "Assembly-CSharp.dll")),
                Write(Path.Combine(destination, "Game_BackUpThisFolder_ButDontShipItWithYourGame", "il2cppOutput", "a.cpp")),
            };
            var record = BuildOutputDirectory.ComputeRecord(destination, before, Array.Empty<string>(), roots);
            Assert.AreEqual(3, record.Files.Count, "every new file is recorded, the neighbour is not");

            Delete(record.Files, record.Folders, null);

            Assert.IsTrue(files.All(f => !File.Exists(f)));
            Assert.IsFalse(Directory.Exists(Path.Combine(destination, "Game_Data")), "folders the build created are removed");
            Assert.IsFalse(Directory.Exists(Path.Combine(destination, "Game_BackUpThisFolder_ButDontShipItWithYourGame")));
            Assert.IsTrue(File.Exists(neighbour));
            Assert.IsTrue(Directory.Exists(destination), "the destination itself is never removed");
        }

        [Test]
        public void RenamedPlayer_PreviousNameIsRemoved()
        {
            var before = BuildOutputDirectory.List(destination, strict: false);
            var oldExe = Write(Path.Combine(destination, "Old Name.exe"));
            var oldData = Write(Path.Combine(destination, "Old Name_Data", "level0"));
            var record = BuildOutputDirectory.ComputeRecord(destination, before, Array.Empty<string>(), roots);

            Delete(record.Files, record.Folders, Path.Combine(destination, "New Name.exe"));

            Assert.IsFalse(File.Exists(oldExe));
            Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(oldData)));
        }

        [Test]
        public void FileChangedSinceTheBuild_IsKeptAndReported()
        {
            var before = BuildOutputDirectory.List(destination, strict: false);
            var edited = Write(Path.Combine(destination, "config.ini"));
            var record = BuildOutputDirectory.ComputeRecord(destination, before, Array.Empty<string>(), roots);
            File.AppendAllText(edited, "edited by the user");

            var skipped = Delete(record.Files, record.Folders, null);

            Assert.IsTrue(File.Exists(edited));
            CollectionAssert.Contains(skipped, "config.ini");
        }

        [Test]
        public void FileWithOnlyANewWriteTime_IsKept()
        {
            // A same-length edit (a flag flipped in a config file) changes only the time.
            var before = BuildOutputDirectory.List(destination, strict: false);
            var edited = Write(Path.Combine(destination, "config.ini"));
            var record = BuildOutputDirectory.ComputeRecord(destination, before, Array.Empty<string>(), roots);
            File.SetLastWriteTimeUtc(edited, File.GetLastWriteTimeUtc(edited).AddSeconds(2));

            var skipped = Delete(record.Files, record.Folders, null);

            Assert.IsTrue(File.Exists(edited));
            CollectionAssert.Contains(skipped, "config.ini");
        }

        [Test]
        public void SameStamp_ToleratesOnlyAWholeHourShiftAndOnlyOnFat()
        {
            var t = new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc).Ticks;
            var hour = TimeSpan.FromHours(1).Ticks;
            Assert.IsTrue(BuildOutputDirectory.SameStamp(10, t, 10, t, false));
            Assert.IsFalse(BuildOutputDirectory.SameStamp(10, t + hour, 10, t, false));
            Assert.IsTrue(BuildOutputDirectory.SameStamp(10, t + hour, 10, t, true));
            Assert.IsTrue(BuildOutputDirectory.SameStamp(10, t - hour, 10, t, true));
            Assert.IsFalse(BuildOutputDirectory.SameStamp(10, t + 2 * hour, 10, t, true));
            Assert.IsFalse(BuildOutputDirectory.SameStamp(11, t, 10, t, true));
        }

        [Test]
        public void MissingRecordedFile_IsTolerated()
        {
            var before = BuildOutputDirectory.List(destination, strict: false);
            var gone = Write(Path.Combine(destination, "Game.exe"));
            var record = BuildOutputDirectory.ComputeRecord(destination, before, Array.Empty<string>(), roots);
            File.Delete(gone);
            Assert.DoesNotThrow(() => Delete(record.Files, record.Folders, null));
        }

        [Test]
        public void ReadOnlyRecordedFile_IsDeleted()
        {
            var before = BuildOutputDirectory.List(destination, strict: false);
            var file = Write(Path.Combine(destination, "Game.exe"));
            File.SetAttributes(file, FileAttributes.ReadOnly);
            var record = BuildOutputDirectory.ComputeRecord(destination, before, Array.Empty<string>(), roots);
            Delete(record.Files, record.Folders, null);
            Assert.IsFalse(File.Exists(file));
        }

        [Test]
        public void UnsafeRecordedPaths_AreNeverDeleted()
        {
            var outside = Write(Path.Combine(root, "Outside", "Keep.txt"));
            var gitFile = Write(Path.Combine(destination, ".git", "config"));
            var rooted = outside;
            var upwards = Path.Combine("..", "..", "..", "..", "..", "Outside", "Keep.txt");
            var sideways = Path.Combine("a", "..", "..", "..", "..", "..", "..", "Outside", "Keep.txt");
            Assert.AreEqual(outside, Path.GetFullPath(Path.Combine(destination, upwards)), "the records must aim at the protected file");
            Assert.AreEqual(outside, Path.GetFullPath(Path.Combine(destination, sideways)));
            var record = new List<RecordedFile>
            {
                Stamped(rooted, outside),
                Stamped(upwards, outside),
                Stamped(sideways, outside),
                Stamped(Path.Combine(".git", "config"), gitFile),
                new RecordedFile { path = "" },
                null,
            };

            var skipped = Delete(record, null, null);

            Assert.IsTrue(File.Exists(outside));
            Assert.IsTrue(File.Exists(gitFile));
            CollectionAssert.AreEquivalent(new[] { rooted, upwards, sideways, Path.Combine(".git", "config") }, skipped);
        }

        [Test]
        public void StreamLikeRecordedPath_IsRejectedWithoutHanging()
        {
            var file = Write(Path.Combine(destination, "UnityPlayer.dll"));
            var record = new List<RecordedFile> { Stamped("UnityPlayer.dll::$DATA", file), Stamped(Path.Combine("sub", "a::b"), file) };
            Assert.DoesNotThrow(() => Delete(record, null, null));
            Assert.IsTrue(File.Exists(file));
        }

        [Test]
        public void FileAnotherProfileProducedTheSame_Survives()
        {
            var before = BuildOutputDirectory.List(destination, strict: false);
            var sharedFile = Write(Path.Combine(destination, "UnityPlayer.dll"));
            var own = Write(Path.Combine(destination, "Quest.exe"));
            var record = BuildOutputDirectory.ComputeRecord(destination, before, Array.Empty<string>(), roots);
            var shared = new List<string>();

            BuildOutputDirectory.DeletePrevious(destination, destination, null, record.Files, record.Folders,
                Claims(Stamped("UnityPlayer.dll", sharedFile)), roots, new List<RecordedFile>(), new List<string>(), shared);

            Assert.IsTrue(File.Exists(sharedFile));
            Assert.IsFalse(File.Exists(own));
            CollectionAssert.AreEqual(new[] { "UnityPlayer.dll" }, shared);
        }

        [Test]
        public void AnotherProfilesOutdatedClaim_DoesNotProtectAFile()
        {
            // A profile that stopped building here recorded an older copy; the
            // file is now what this profile wrote, and it must go.
            var file = Write(Path.Combine(destination, "RuntimeActionBindings.json"));
            var mine = Stamped("RuntimeActionBindings.json", file);
            var outdated = new RecordedFile { path = mine.path, size = mine.size, lastWriteUtcTicks = mine.lastWriteUtcTicks - TimeSpan.FromMinutes(5).Ticks };

            BuildOutputDirectory.DeletePrevious(destination, destination, null, new List<RecordedFile> { mine }, null,
                Claims(outdated), roots, new List<RecordedFile>(), new List<string>());

            Assert.IsFalse(File.Exists(file));
        }

        [Test]
        public void FolderThatExistedBeforeTheBuild_Survives()
        {
            var userFolder = Path.Combine(destination, "Data");
            Directory.CreateDirectory(userFolder);
            var before = BuildOutputDirectory.List(destination, strict: true);
            Write(Path.Combine(userFolder, "runtime.json")); // a package post-processor writes into it
            Write(Path.Combine(destination, "Game_Data", "level0"));
            var record = BuildOutputDirectory.ComputeRecord(destination, before, Array.Empty<string>(), roots);
            CollectionAssert.AreEquivalent(new[] { "Game_Data" }, record.Folders);

            Delete(record.Files, record.Folders, null);

            Assert.IsTrue(Directory.Exists(userFolder), "a folder the build did not create stays, even when emptied");
            Assert.IsFalse(Directory.Exists(Path.Combine(destination, "Game_Data")));
        }

        [Test]
        public void LockedFile_AbortsAndKeepsTheRestRecorded()
        {
            if (!Windows)
                Assert.Ignore("Windows file sharing semantics");
            var before = BuildOutputDirectory.List(destination, strict: false);
            var first = Write(Path.Combine(destination, "a.dll"));
            var locked = Write(Path.Combine(destination, "b.exe"));
            var last = Write(Path.Combine(destination, "c.dll"));
            var record = BuildOutputDirectory.ComputeRecord(destination, before, Array.Empty<string>(), roots);
            var remaining = new List<RecordedFile>();

            using (File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<IOException>(() => BuildOutputDirectory.DeletePrevious(destination, destination, null,
                    record.Files, record.Folders, null, roots, remaining, new List<string>()));

            Assert.IsFalse(File.Exists(first));
            Assert.IsTrue(File.Exists(locked));
            Assert.IsTrue(File.Exists(last));
            CollectionAssert.AreEqual(new[] { "b.exe", "c.dll" }, remaining.Select(f => f.path));
        }

        [Test]
        public void LockedArtifact_AbortsBeforeAnythingElse()
        {
            if (!Windows)
                Assert.Ignore("Windows file sharing semantics");
            var before = BuildOutputDirectory.List(destination, strict: false);
            var other = Write(Path.Combine(destination, "Old.exe"));
            var record = BuildOutputDirectory.ComputeRecord(destination, before, Array.Empty<string>(), roots);
            var artifact = Write(Path.Combine(destination, "Game.exe"));
            var remaining = new List<RecordedFile>();

            using (File.Open(artifact, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<IOException>(() => BuildOutputDirectory.DeletePrevious(destination, destination, artifact,
                    record.Files, record.Folders, null, roots, remaining, new List<string>()));

            Assert.IsTrue(File.Exists(other));
            CollectionAssert.AreEqual(new[] { "Old.exe" }, remaining.Select(f => f.path));
        }

        // ------------------------------------------------------------ recording

        [Test]
        public void Record_TakesNewFilesAndRewrittenReportEntries_Only()
        {
            var editorFile = Write(Path.Combine(roots.Editor, "Editor", "Data", "MonoBleedingEdge", "mono.dll"));
            var libraryFile = Write(Path.Combine(project, "Library", "Bee", "artifact.o"));
            var existing = Write(Path.Combine(destination, "Keep.txt"));
            var untouchedReported = Write(Path.Combine(destination, "Readme.txt"));
            var overwritten = Write(Path.Combine(destination, "UnityPlayer.dll"));
            var before = BuildOutputDirectory.List(destination, strict: true);
            File.WriteAllText(overwritten, "rewritten by the build, a different length");
            Write(Path.Combine(destination, "Game.exe"));
            Write(Path.Combine(destination, "RuntimeActionBindings.json")); // a package post-processor's file

            var record = BuildOutputDirectory.ComputeRecord(destination, before, new[]
            {
                editorFile, libraryFile, untouchedReported, overwritten.Replace('\\', '/'), Path.Combine(destination, "Missing.dll"),
            }, roots);

            CollectionAssert.AreEquivalent(new[] { "Game.exe", "RuntimeActionBindings.json", "UnityPlayer.dll" },
                record.Files.Select(f => f.path));
            Assert.IsTrue(File.Exists(existing));
        }

        [Test]
        public void Record_IgnoresReportEntriesTheBuildDidNotWrite()
        {
            // macOS: a player written to /Applications, where Unity Hub keeps the
            // editor. A failed 6000.3 build's report lists the editor's module
            // files as sources; they are inside the destination but unchanged.
            var editorModule = Write(Path.Combine(destination, "Unity", "Hub", "Editor", "6000.3.0f1", "PlaybackEngines", "UnityPlayer"));
            var before = BuildOutputDirectory.List(destination, strict: true);
            Write(Path.Combine(destination, "Game.app", "Contents", "Info.plist"));

            var record = BuildOutputDirectory.ComputeRecord(destination, before, new[] { editorModule }, roots);

            CollectionAssert.AreEquivalent(new[] { Path.Combine("Game.app", "Contents", "Info.plist") }, record.Files.Select(f => f.path));
            Delete(record.Files, record.Folders, null);
            Assert.IsTrue(File.Exists(editorModule));
        }

        [Test]
        public void Record_SkipsEverythingBelowAFolderUnreadableBeforeTheBuild()
        {
            // A network hiccup while listing "Other team" before the build must
            // not make that team's files look new afterwards.
            var before = BuildOutputDirectory.List(destination, strict: false);
            before.Unreadable.Add("Other team");
            var theirs = Write(Path.Combine(destination, "Other team", "Their build.apk"));
            Write(Path.Combine(destination, "Game.exe"));

            var record = BuildOutputDirectory.ComputeRecord(destination, before, new[] { theirs }, roots);

            CollectionAssert.AreEquivalent(new[] { "Game.exe" }, record.Files.Select(f => f.path));
            CollectionAssert.IsEmpty(record.Folders);
        }

        [Test]
        public void UnreadableFolder_OnDisk_IsReportedAndNothingBelowItIsRecorded()
        {
            if (!Windows)
                Assert.Ignore("Uses a Windows ACL to make a folder unreadable");
            var locked = Path.Combine(destination, "Other team");
            var theirs = Write(Path.Combine(locked, "Their build.apk"));
            Icacls($"\"{locked}\" /deny *S-1-1-0:(RX)");
            try
            {
                var before = BuildOutputDirectory.List(destination, strict: true);
                CollectionAssert.Contains(before.Unreadable, "Other team");
                Write(Path.Combine(destination, "Game.exe"));
                Icacls($"\"{locked}\" /remove:d *S-1-1-0"); // readable again when the build is recorded

                var record = BuildOutputDirectory.ComputeRecord(destination, before, new[] { theirs }, roots);

                CollectionAssert.AreEquivalent(new[] { "Game.exe" }, record.Files.Select(f => f.path));
            }
            finally { Icacls($"\"{locked}\" /remove:d *S-1-1-0", check: false); }
        }

        [Test]
        public void UnreadableDestination_FailsStrictListing()
        {
            if (!Windows)
                Assert.Ignore("Uses a Windows ACL to make a folder unreadable");
            var unreadable = Path.Combine(root, "Unreadable");
            Write(Path.Combine(unreadable, "Game.exe"));
            Icacls($"\"{unreadable}\" /deny *S-1-1-0:(RX)");
            try
            {
                Assert.Throws<IOException>(() => BuildOutputDirectory.List(unreadable, strict: true));
                Assert.IsEmpty(BuildOutputDirectory.List(unreadable, strict: false).Files);
            }
            finally { Icacls($"\"{unreadable}\" /remove:d *S-1-1-0", check: false); }
        }

        [Test]
        public void List_StrictFailsForAMissingDestination_OtherwiseReadsEmpty()
        {
            var missing = Path.Combine(root, "Missing");
            Assert.Throws<IOException>(() => BuildOutputDirectory.List(missing, strict: true));
            Assert.IsEmpty(BuildOutputDirectory.List(missing, strict: false).Files);
        }

        [Test]
        public void List_NeverEntersNestedUnityProjectsOrCheckouts()
        {
            Write(Path.Combine(destination, "Other Project", "ProjectSettings", "ProjectVersion.txt"));
            Write(Path.Combine(destination, "Other Project", "Library", "cache.bin"));
            Write(Path.Combine(destination, "Releases", ".git", "HEAD"));
            Write(Path.Combine(destination, "Releases", "notes.md"));
            Write(Path.Combine(destination, "Game.exe"));

            var listing = BuildOutputDirectory.List(destination, strict: true);

            CollectionAssert.AreEquivalent(new[] { "Game.exe" }, listing.Files.Keys);
            CollectionAssert.IsEmpty(listing.Folders);
        }

        [Test]
        public void Record_StoresTheSizeAndTimeOnDisk()
        {
            var before = BuildOutputDirectory.List(destination, strict: false);
            var file = Write(Path.Combine(destination, "Game.exe"));
            var info = new FileInfo(file);
            var entry = BuildOutputDirectory.ComputeRecord(destination, before, Array.Empty<string>(), roots).Files.Single();
            Assert.AreEqual(info.Length, entry.size);
            Assert.AreEqual(info.LastWriteTimeUtc.Ticks, entry.lastWriteUtcTicks);
        }

        // ------------------------------------------------------------ links

        [Test]
        public void LinkedFolderInsideDestination_IsNeitherRecordedNorFollowed()
        {
            var outside = Write(Path.Combine(root, "Outside", "Keep.txt"));
            var before = BuildOutputDirectory.List(destination, strict: false);
            CreateLink(Path.Combine(destination, "Linked"), Path.GetDirectoryName(outside));

            var record = BuildOutputDirectory.ComputeRecord(destination, before, new[] { Path.Combine(destination, "Linked", "Keep.txt") }, roots);
            Assert.IsEmpty(record.Files);
            Assert.IsEmpty(record.Folders);

            Delete(new List<RecordedFile> { Stamped(Path.Combine("Linked", "Keep.txt"), outside) }, new[] { "Linked" }, null);
            Assert.IsTrue(File.Exists(outside));
            Assert.IsTrue(Directory.Exists(Path.Combine(destination, "Linked")), "a link is never removed as a folder");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LinkAtOrAboveTheDestination_IsAllowed(bool child)
        {
            // Projects under a junction (or a redirected Documents folder) must build.
            var real = Path.Combine(root, "Real");
            Directory.CreateDirectory(real);
            var link = Path.Combine(root, "Checkout", "Linked");
            CreateLink(link, real);
            var target = child ? Path.Combine(link, "Nested") : link;
            Directory.CreateDirectory(target);

            Assert.DoesNotThrow(() => BuildOutputDirectory.ValidateDestination(target, roots));
            var artifact = Write(Path.Combine(target, "Game.apk"));
            BuildOutputDirectory.DeletePrevious(target, target, artifact, null, null, null, roots, new List<RecordedFile>(), new List<string>());
            Assert.IsFalse(File.Exists(artifact));
        }

        [TestCase(0xA0000003u, true)]  // mount point (junction)
        [TestCase(0xA000000Cu, true)]  // symbolic link
        [TestCase(0xA000001Du, true)]  // WSL symlink
        [TestCase(0x9000001Au, false)] // cloud file placeholder (OneDrive)
        [TestCase(0x9000701Au, false)] // cloud file placeholder, another provider
        [TestCase(0x80000013u, false)] // deduplication
        public void NameSurrogateBit_IdentifiesLinkTags(uint tag, bool isLink)
            => Assert.AreEqual(isLink, BuildOutputDirectory.IsNameSurrogate(tag));

        [Test]
        public void PlainFilesAndFolders_AreNotLinks()
        {
            var file = Write(Path.Combine(destination, "Game.exe"));
            Assert.IsFalse(BuildOutputDirectory.IsLink(new FileInfo(file)));
            Assert.IsFalse(BuildOutputDirectory.IsLink(new DirectoryInfo(destination)));
        }

        [Test]
        public void ReparsePointThatIsNotALink_IsAnOrdinaryFile_AlsoOnLongPaths()
        {
            // Cloud placeholders (OneDrive) carry the reparse attribute with a
            // tag that is not a name surrogate; a third-party tag behaves alike.
            if (!Windows)
                Assert.Ignore("Windows reparse points");
            var before = BuildOutputDirectory.List(destination, strict: false);
            var shallow = Write(Path.Combine(destination, "Game.apk"));
            var deepFolder = Path.Combine(new[] { destination }.Concat(Enumerable.Repeat("il2cppOutput_generated_sources", 9)).ToArray());
            var deep = WriteLong(Path.Combine(deepFolder, "Unity.RenderPipelines.Universal.Runtime_CodeGen.c"));
            Assert.That(deep.Length, Is.GreaterThan(260), "the second file must exceed MAX_PATH");
            TagReparsePoint(shallow);
            TagReparsePoint(deep);
            Assert.IsTrue((File.GetAttributes(deep) & FileAttributes.ReparsePoint) != 0, "the test file must carry the reparse attribute");

            Assert.IsFalse(BuildOutputDirectory.IsLink(new FileInfo(shallow)));
            Assert.IsFalse(BuildOutputDirectory.IsLink(new FileInfo(deep)));
            var record = BuildOutputDirectory.ComputeRecord(destination, before, Array.Empty<string>(), roots);
            Assert.AreEqual(2, record.Files.Count, "both files are listed and recorded");

            Delete(record.Files, record.Folders, null);
            Assert.IsFalse(File.Exists(shallow));
            Assert.IsFalse(File.Exists(deep));
        }

        [Test]
        public void ExtendedPath_PrefixesLocalAndUncPaths()
        {
            Assert.AreEqual(@"\\?\C:\Builds\Game", BuildOutputDirectory.ExtendedPath(@"C:\Builds\Game"));
            Assert.AreEqual(@"\\?\C:\Builds\Game", BuildOutputDirectory.ExtendedPath("C:/Builds/Game/"));
            Assert.AreEqual(@"\\?\UNC\nas\builds\Game", BuildOutputDirectory.ExtendedPath(@"\\nas\builds\Game"));
            Assert.AreEqual(@"\\?\C:\Builds", BuildOutputDirectory.ExtendedPath(@"\\?\C:\Builds"));
        }

        // ------------------------------------------------------------ destinations

        [TestCase("Assets")]
        [TestCase("Packages")]
        [TestCase("ProjectSettings")]
        [TestCase("UserSettings")]
        [TestCase("Library")]
        [TestCase("Temp")]
        [TestCase("Logs")]
        [TestCase(".git")]
        public void ProjectOwnedDirectories_AreRejected(string folder)
            => Assert.Throws<InvalidOperationException>(() =>
                BuildOutputDirectory.ValidateDestination(Path.Combine(project, folder, "Nested"), roots));

        [TestCase(".")]
        [TestCase("..")]
        [TestCase("Builds/..")]
        [TestCase("../..")]
        public void ProjectOrAncestor_IsRejected(string relative)
            => Assert.Throws<InvalidOperationException>(() =>
                BuildOutputDirectory.ValidateDestination(Path.Combine(project, relative), roots));

        [Test]
        public void ShortNameOfAProjectFolder_IsRejected()
        {
            if (!Windows)
                Assert.Ignore("Short (8.3) names exist only on Windows");
            var settings = Path.Combine(project, "ProjectSettings");
            Directory.CreateDirectory(settings);
            var buffer = new StringBuilder(1024);
            var length = GetShortPathNameW(settings, buffer, (uint)buffer.Capacity);
            var shortPath = length > 0 && length < buffer.Capacity ? buffer.ToString() : settings;
            if (string.Equals(Path.GetFileName(shortPath), "ProjectSettings", StringComparison.OrdinalIgnoreCase))
                Assert.Ignore("Short names are not generated on this volume");
            Assert.Throws<InvalidOperationException>(() =>
                BuildOutputDirectory.ValidateDestination(Path.Combine(shortPath, "Out"), roots));
        }

        [Test]
        public void FilesystemRoot_IsRejected()
        {
            if (!Windows)
                Assert.Ignore("On macOS and Linux the only root contains the project, which another rule rejects first.");
            // A drive other than the project's, so only the filesystem-root rule can apply.
            var projectDrive = char.ToUpperInvariant(Path.GetPathRoot(project)[0]);
            var other = projectDrive == 'Q' ? "R:\\" : "Q:\\";
            var error = Assert.Throws<InvalidOperationException>(() => BuildOutputDirectory.ValidateDestination(other, roots));
            StringAssert.Contains("filesystem root", error.Message);
        }

        [Test]
        public void SeparateRepositoryRoot_IsRejected()
        {
            var repository = Path.Combine(root, "Separate Repository");
            var separate = new BuildOutputDirectory.Roots { Project = project, Repository = repository };
            Assert.Throws<InvalidOperationException>(() => BuildOutputDirectory.ValidateDestination(repository, separate));
        }

        [Test]
        public void LocalPackageSource_IsRejected()
        {
            var package = Path.Combine(root, "Package");
            var withPackage = new BuildOutputDirectory.Roots { Project = project, Package = package };
            Assert.Throws<InvalidOperationException>(() =>
                BuildOutputDirectory.ValidateDestination(Path.Combine(package, "Editor"), withPackage));
        }

        [Test]
        public void EditorInstallation_IsRejectedFromInsideAndAbove()
        {
            StringAssert.Contains("editor installation", Assert.Throws<InvalidOperationException>(() =>
                BuildOutputDirectory.ValidateDestination(Path.Combine(roots.Editor, "Builds"), roots)).Message);
            StringAssert.Contains("editor installation", Assert.Throws<InvalidOperationException>(() =>
                BuildOutputDirectory.ValidateDestination(Path.Combine(root, "Hub"), roots)).Message);
        }

        [Test]
        public void AnotherUnityProjectsOwnFolders_AreRejected()
        {
            // Also covers this project reached through a junction or another alias.
            var other = Path.Combine(root, "Other Project");
            Write(Path.Combine(other, "ProjectSettings", "ProjectVersion.txt"));
            Assert.Throws<InvalidOperationException>(() =>
                BuildOutputDirectory.ValidateDestination(Path.Combine(other, "Library", "Out"), roots));
            Assert.Throws<InvalidOperationException>(() => BuildOutputDirectory.ValidateDestination(other, roots));
            Assert.DoesNotThrow(() => BuildOutputDirectory.ValidateDestination(Path.Combine(other, "Builds"), roots));
        }

        [Test]
        public void CheckoutAsDestination_IsRejected()
        {
            var releases = Path.Combine(root, "Releases");
            Write(Path.Combine(releases, ".git", "HEAD"));
            Assert.Throws<InvalidOperationException>(() => BuildOutputDirectory.ValidateDestination(releases, roots));
        }

        [Test]
        public void DedicatedFolderInsideTheProject_IsAccepted()
            => Assert.DoesNotThrow(() =>
                BuildOutputDirectory.ValidateDestination(Path.Combine(project, "Builds", "Android"), roots));

        // ------------------------------------------------------------ prepare and record

        [Test]
        public void PrepareAndRecord_RemoveThePreviousBuildOnly()
        {
            var guid = Guid();
            var notes = Write(Path.Combine(destination, "Notes.txt"));
            var alpha = Path.Combine(destination, "Alpha.exe");

            var first = Prepare(guid, destination, alpha);
            var built = new[]
            {
                Write(alpha),
                Write(Path.Combine(destination, "Alpha_Data", "level0")),
                Write(Path.Combine(destination, "RuntimeActionBindings.json")),
            };
            BuildOutputDirectory.RecordIn(BuildOutputLedger.instance, first, new[] { alpha });

            Prepare(guid, destination, Path.Combine(destination, "Beta.exe"));

            Assert.IsTrue(built.All(f => !File.Exists(f)));
            Assert.IsFalse(Directory.Exists(Path.Combine(destination, "Alpha_Data")));
            Assert.IsTrue(File.Exists(notes));
            Assert.IsNull(BuildOutputLedger.instance.Find(guid, null, out _, out _), "a completed cleanup clears the record");
        }

        [Test]
        public void Prepare_UnsafeDestinationThrowsBeforeDeletingAnything()
        {
            var unsafeDestination = Path.Combine(project, "Assets", "Out");
            var artifact = Write(Path.Combine(unsafeDestination, "Game.apk"));
            Assert.Throws<InvalidOperationException>(() => Prepare(Guid(), unsafeDestination, artifact));
            Assert.IsTrue(File.Exists(artifact));
        }

        [Test]
        public void Prepare_DestinationBlockedByAFileFailsAndKeepsTheFile()
        {
            Write(destination);
            Assert.Throws<IOException>(() => Prepare(Guid(), destination, null));
            Assert.IsTrue(File.Exists(destination));
        }

        [Test]
        public void Prepare_ChangedDestination_CleansWhereRecorded_NotTheNewDestination()
        {
            var guid = Guid();
            var oldDestination = Path.Combine(root, "Checkout", "Build", "A");
            var newDestination = Path.Combine(root, "Checkout", "Build", "B");
            var first = Prepare(guid, oldDestination, Path.Combine(oldDestination, "Game.exe"));
            var built = Write(Path.Combine(oldDestination, "Game_Data", "level0"));
            Write(Path.Combine(oldDestination, "Game.exe"));
            BuildOutputDirectory.RecordIn(BuildOutputLedger.instance, first, Array.Empty<string>());
            // An Explorer copy keeps size and write time.
            var copy = Path.Combine(newDestination, "Game_Data", "level0");
            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            File.Copy(built, copy);

            Prepare(guid, newDestination, Path.Combine(newDestination, "Other.exe"));

            Assert.IsFalse(File.Exists(built), "the previous build's files go from where they were recorded");
            Assert.IsTrue(File.Exists(copy), "a copy in the new destination is not the previous build");
        }

        [Test]
        public void Prepare_LockedFileInThePreviousDestination_DoesNotBlockTheBuild()
        {
            if (!Windows)
                Assert.Ignore("Windows file sharing semantics");
            var guid = Guid();
            var oldDestination = Path.Combine(root, "Checkout", "Build", "Old");
            var first = Prepare(guid, oldDestination, Path.Combine(oldDestination, "Game.exe"));
            var exe = Write(Path.Combine(oldDestination, "Game.exe"));
            var data = Write(Path.Combine(oldDestination, "Game_Data", "level0"));
            BuildOutputDirectory.RecordIn(BuildOutputLedger.instance, first, new[] { exe });

            using (File.Open(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.DoesNotThrow(() => Prepare(guid, destination, Path.Combine(destination, "Game.exe")));

            Assert.IsTrue(File.Exists(exe), "the running player is left where it was");
            Assert.IsFalse(File.Exists(data));
        }

        [Test]
        public void Prepare_CopiedProject_NeverCleansTheOriginalsOutput()
        {
            // Duplicating a project folder copies UserSettings, and with it the
            // original's record of its absolute destination.
            var guid = Guid();
            var originalDestination = Path.Combine(project, "Builds", "StandaloneWindows64", "Quest");
            var first = Prepare(guid, originalDestination, Path.Combine(originalDestination, "Game.exe"));
            var exe = Write(Path.Combine(originalDestination, "Game.exe"));
            var data = Write(Path.Combine(originalDestination, "Game_Data", "level0"));
            BuildOutputDirectory.RecordIn(BuildOutputLedger.instance, first, new[] { exe });

            var copy = Path.Combine(root, "Checkout", "Unity Project Copy");
            Directory.CreateDirectory(copy);
            var copyRoots = new BuildOutputDirectory.Roots { Project = copy, Repository = roots.Repository, Editor = roots.Editor };
            var copyDestination = Path.Combine(copy, "Builds", "StandaloneWindows64", "Quest");
            BuildOutputDirectory.PrepareIn(BuildOutputLedger.instance, guid, null, copyDestination,
                Path.Combine(copyDestination, "Game.exe"), false, copyRoots, (g, v) => true);

            Assert.IsTrue(File.Exists(exe));
            Assert.IsTrue(File.Exists(data));
            Assert.IsNull(BuildOutputLedger.instance.Find(guid, null, out _, out _), "the original's record is dropped in the copy");
        }

        [Test]
        public void Prepare_PreviousDestinationInsideAnotherProject_IsLeftAlone()
        {
            var guid = Guid();
            var other = Path.Combine(root, "Other Project");
            Write(Path.Combine(other, "ProjectSettings", "ProjectVersion.txt"));
            var oldDestination = Path.Combine(other, "Builds", "Quest");
            var file = Write(Path.Combine(oldDestination, "Game.exe"));
            BuildOutputLedger.instance.Set(guid, null, roots.Project, oldDestination, new List<RecordedFile> { Stamped("Game.exe", file) });

            Prepare(guid, destination, Path.Combine(destination, "Game.exe"));

            Assert.IsTrue(File.Exists(file));
        }

        [Test]
        public void Prepare_UnreachableRecordedVolume_KeepsTheRecord()
        {
            if (!Windows)
                Assert.Ignore("Uses a drive letter that is not present");
            var letter = Enumerable.Range('D', 23).Select(c => (char)c).Reverse().FirstOrDefault(c => !Directory.Exists(c + ":\\"));
            if (letter == default)
                Assert.Ignore("Every drive letter is in use");
            var guid = Guid();
            var offline = letter + @":\Builds\Quest";
            BuildOutputLedger.instance.Set(guid, null, roots.Project, offline, new List<RecordedFile>
            {
                new RecordedFile { path = "Game.exe", size = 1, lastWriteUtcTicks = 1 },
            });

            Assert.Throws<IOException>(() => Prepare(guid, offline, Path.Combine(offline, "Game.exe")));

            Assert.IsNotNull(BuildOutputLedger.instance.Find(guid, null, out _, out _), "an offline destination keeps its record");
        }

        [Test]
        public void Prepare_SwitchingAndroidFromApkToExport_RemovesTheApkThatBlocksTheFolder()
        {
            var guid = Guid();
            var apk = Path.Combine(destination, "Game.apk");
            var first = Prepare(guid, destination, apk);
            Write(apk);
            Write(Path.Combine(destination, "Game_BackUpThisFolder_ButDontShipItWithYourGame", "il2cppOutput", "a.cpp"));
            BuildOutputDirectory.RecordIn(BuildOutputLedger.instance, first, new[] { apk });

            Prepare(guid, apk, null); // export: the same path now names the output folder

            Assert.IsTrue(Directory.Exists(apk));
            Assert.IsFalse(Directory.Exists(Path.Combine(destination, "Game_BackUpThisFolder_ButDontShipItWithYourGame")));
        }

        [Test]
        public void Prepare_SwitchingAndroidFromExportToApk_RemovesTheExportFolder()
        {
            var guid = Guid();
            var exportFolder = Path.Combine(destination, "Game.apk");
            var first = Prepare(guid, exportFolder, null);
            Write(Path.Combine(exportFolder, "build.gradle"));
            Write(Path.Combine(exportFolder, "unityLibrary", "src", "main", "Unity.java"));
            BuildOutputDirectory.RecordIn(BuildOutputLedger.instance, first, Array.Empty<string>());

            Prepare(guid, destination, exportFolder);

            Assert.IsFalse(Directory.Exists(exportFolder), "the APK can be written where the export folder was");
        }

        [Test]
        public void Prepare_ExportFolderWithFilesTheExportDidNotWrite_FailsBeforeTheBuild()
        {
            var guid = Guid();
            var exportFolder = Path.Combine(destination, "Game.apk");
            var first = Prepare(guid, exportFolder, null);
            var gradle = Write(Path.Combine(exportFolder, "build.gradle"));
            BuildOutputDirectory.RecordIn(BuildOutputLedger.instance, first, Array.Empty<string>());
            var sync = Write(Path.Combine(exportFolder, ".gradle", "8.4", "checksums.lock")); // Android Studio's Gradle sync

            var error = Assert.Throws<IOException>(() => Prepare(guid, destination, exportFolder));

            StringAssert.Contains(".gradle", error.Message);
            Assert.IsFalse(File.Exists(gradle), "the export's own files are removed");
            Assert.IsTrue(File.Exists(sync), "files the export did not write are kept");
        }

        [Test]
        public void Prepare_ForgetsRecordsOfProfilesThatNoLongerExist()
        {
            var live = Guid();
            var stale = Guid();
            var player = Write(Path.Combine(destination, "UnityPlayer.dll"));
            BuildOutputLedger.instance.Set(live, null, roots.Project, destination, new List<RecordedFile> { Stamped("UnityPlayer.dll", player) });
            BuildOutputLedger.instance.Set(stale, null, roots.Project, destination, new List<RecordedFile> { Stamped("UnityPlayer.dll", player) });

            BuildOutputDirectory.PrepareIn(BuildOutputLedger.instance, live, null, destination, null, false, roots, (g, v) => g != stale);

            Assert.IsNull(BuildOutputLedger.instance.Find(stale, null, out _, out _));
            Assert.IsFalse(File.Exists(player), "no longer protected by the deleted profile's record");
        }

        [TestCase(null)]
        [TestCase("project")]
        [TestCase("elsewhere")]
        public void Prepare_ChangedDestinationBesideTheProject_InItsOwnRepository_IsCleaned(string repository)
        {
            // The checkout around the project is the project's own, whether Git is
            // unavailable, the project is a nested repository, or Git spells the
            // path differently (a junction resolved to another drive).
            Directory.CreateDirectory(Path.Combine(root, "Checkout", ".git"));
            roots.Repository = repository == "project" ? project
                : repository == "elsewhere" ? Path.Combine(root, "Resolved", "Checkout") : null;
            var guid = Guid();
            var oldDestination = Path.Combine(root, "Checkout", "Build", "Quest");
            var first = Prepare(guid, oldDestination, Path.Combine(oldDestination, "Game.apk"));
            var apk = Write(Path.Combine(oldDestination, "Game.apk"));
            var backup = Write(Path.Combine(oldDestination, "Game_BackUpThisFolder_ButDontShipItWithYourGame", "a.cpp"));
            BuildOutputDirectory.RecordIn(BuildOutputLedger.instance, first, new[] { apk });

            var newDestination = Path.Combine(oldDestination, "Default"); // the first variant was added
            Prepare(guid, newDestination, Path.Combine(newDestination, "Game.apk"));

            Assert.IsFalse(File.Exists(apk));
            Assert.IsFalse(File.Exists(backup));
        }

        [Test]
        public void Prepare_MissingRecordedDestination_KeepsTheRecord()
        {
            // A folder behind an unmounted link or an ejected macOS volume reads as
            // missing while its drive root exists.
            var guid = Guid();
            var gone = Path.Combine(root, "Checkout", "Build", "Mounted", "Quest");
            BuildOutputLedger.instance.Set(guid, null, roots.Project, gone, new List<RecordedFile>
            {
                new RecordedFile { path = "Game.exe", size = 1, lastWriteUtcTicks = 1 },
            });

            Prepare(guid, gone, Path.Combine(gone, "Game.exe"));

            Assert.IsNotNull(BuildOutputLedger.instance.Find(guid, null, out _, out _), "the record waits for the destination to come back");
        }

        [Test]
        public void Prepare_ExportFolderWithoutRecord_SaysSo()
        {
            var exportFolder = Path.Combine(destination, "Game.apk");
            Write(Path.Combine(exportFolder, "build.gradle"));

            var error = Assert.Throws<IOException>(() => Prepare(Guid(), destination, exportFolder));

            StringAssert.Contains("no record", error.Message);
            Assert.IsTrue(File.Exists(Path.Combine(exportFolder, "build.gradle")));
        }

        [Test]
        public void ShortSpelledProjectRoot_StillProtectsTheProjectFolders()
        {
            if (!Windows)
                Assert.Ignore("Short (8.3) names exist only on Windows");
            var buffer = new StringBuilder(1024);
            var length = GetShortPathNameW(project, buffer, (uint)buffer.Capacity);
            var shortProject = length > 0 && length < buffer.Capacity ? buffer.ToString() : project;
            if (shortProject.IndexOf('~') < 0)
                Assert.Ignore("Short names are not generated on this volume");
            var shortRoots = new BuildOutputDirectory.Roots { Project = shortProject, Editor = roots.Editor };
            Assert.Throws<InvalidOperationException>(() =>
                BuildOutputDirectory.ValidateDestination(Path.Combine(project, "Assets", "Out"), shortRoots));
            Assert.Throws<InvalidOperationException>(() =>
                BuildOutputDirectory.ValidateDestination(Path.GetDirectoryName(project), shortRoots));
        }

        // ------------------------------------------------------------ ledger

        [Test]
        public void Ledger_KeepsOneRecordPerProfileAndVariant()
        {
            var ledger = BuildOutputLedger.instance;
            var guid = Guid();
            var other = Guid();
            ledger.Set(guid, null, project, destination, new List<RecordedFile> { new RecordedFile { path = "Game.exe" } }, new List<string> { "Game_Data" });
            ledger.Set(guid, "Internal", project, destination, new List<RecordedFile> { new RecordedFile { path = "Game (Internal).exe" } });
            ledger.Set(other, null, project, destination, new List<RecordedFile> { new RecordedFile { path = "UnityPlayer.dll" } });

            Assert.AreEqual("Game.exe", ledger.Find(guid, null, out var recordedAt, out var folders).Single().path);
            Assert.AreEqual(destination, recordedAt);
            CollectionAssert.AreEqual(new[] { "Game_Data" }, folders);
            Assert.AreEqual("Game.exe", ledger.Find(guid, "", out _, out _).Single().path, "null and empty both mean the default build");
            CollectionAssert.AreEquivalent(new[] { "Game (Internal).exe", "UnityPlayer.dll" },
                ledger.RecordsOfOthers(destination, guid, null).Keys);

            ledger.Set(guid, null, project, destination, new List<RecordedFile>());
            Assert.IsNull(ledger.Find(guid, null, out _, out _), "an empty record removes the entry");
        }

        // ------------------------------------------------------------ helpers

        string Guid()
        {
            return "buildforge-test-" + System.Guid.NewGuid().ToString("N");
        }

        BuildOutputDirectory.Preparation Prepare(string guid, string target, string artifact)
            => BuildOutputDirectory.PrepareIn(BuildOutputLedger.instance, guid, null, target, artifact, false, roots, (g, v) => true);

        List<string> Delete(IReadOnlyList<RecordedFile> record, IReadOnlyList<string> folders, string artifact)
        {
            var skipped = new List<string>();
            var remaining = new List<RecordedFile>();
            BuildOutputDirectory.DeletePrevious(destination, destination, artifact, record, folders, null, roots, remaining, skipped);
            Assert.IsEmpty(remaining);
            return skipped;
        }

        static Dictionary<string, List<RecordedFile>> Claims(params RecordedFile[] claims)
        {
            var map = new Dictionary<string, List<RecordedFile>>(BuildOutputDirectory.PathComparer);
            foreach (var claim in claims)
            {
                if (!map.TryGetValue(claim.path, out var list))
                    map[claim.path] = list = new List<RecordedFile>();
                list.Add(claim);
            }
            return map;
        }

        static RecordedFile Stamped(string path, string stampFrom)
        {
            var info = new FileInfo(stampFrom);
            return new RecordedFile { path = path, size = info.Length, lastWriteUtcTicks = info.LastWriteTimeUtc.Ticks };
        }

        void CreateLink(string path, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var start = Windows
                ? new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"")
                : new ProcessStartInfo("ln", $"-s \"{target}\" \"{path}\"");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using var process = Process.Start(start);
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, "Could not create the test link");
            links.Add(path);
        }

        static void Icacls(string arguments, bool check = true)
        {
            using var process = Process.Start(new ProcessStartInfo("icacls.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process.WaitForExit();
            if (check)
                Assert.AreEqual(0, process.ExitCode, $"icacls {arguments} failed");
        }

        static string Write(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "build output or protected file");
            return path;
        }

        // Unity's Mono cannot create files past 260 characters; the Win32 API
        // with the \\?\ prefix can. Only Windows tests use this.
        static string WriteLong(string path)
        {
            var folder = Path.GetDirectoryName(path);
            var missing = new Stack<string>();
            for (var current = folder; current != null && !Directory.Exists(current); current = Path.GetDirectoryName(current))
                missing.Push(current);
            foreach (var level in missing)
                Assert.IsTrue(CreateDirectoryW(BuildOutputDirectory.ExtendedPath(level), IntPtr.Zero) || Marshal.GetLastWin32Error() == 183,
                    $"Could not create '{level}' (error {Marshal.GetLastWin32Error()})");
            using (var handle = CreateFileW(BuildOutputDirectory.ExtendedPath(path), 0x40000000 /* GENERIC_WRITE */, 0, IntPtr.Zero,
                       2 /* CREATE_ALWAYS */, 0x80 /* NORMAL */, IntPtr.Zero))
            {
                Assert.IsFalse(handle.IsInvalid, $"Could not create '{path}' (error {Marshal.GetLastWin32Error()})");
                using var stream = new FileStream(handle, FileAccess.Write);
                var bytes = Encoding.UTF8.GetBytes("build output");
                stream.Write(bytes, 0, bytes.Length);
            }
            return path;
        }

        // A third-party reparse tag: bit 31 clear (needs a GUID buffer), bit 29
        // clear (not a name surrogate), like cloud-file placeholders. Setting it
        // needs no privilege; the file stays deletable.
        static void TagReparsePoint(string path)
        {
            const uint tag = 0x00001234;
            var buffer = new byte[24 + 4];
            BitConverter.GetBytes(tag).CopyTo(buffer, 0);
            BitConverter.GetBytes((ushort)4).CopyTo(buffer, 4);
            new System.Guid("5a8c7e21-3d0b-4c7f-9a51-6f1e2b8d4c30").ToByteArray().CopyTo(buffer, 8);
            BitConverter.GetBytes(0xBF0B5E15u).CopyTo(buffer, 24);
            using var handle = CreateFileW(BuildOutputDirectory.ExtendedPath(path), 0x40000000 /* GENERIC_WRITE */, 7, IntPtr.Zero,
                3 /* OPEN_EXISTING */, 0x00200000 | 0x02000000 /* OPEN_REPARSE_POINT | BACKUP_SEMANTICS */, IntPtr.Zero);
            if (handle.IsInvalid)
                Assert.Ignore($"Cannot open '{path}' to set a reparse point (error {Marshal.GetLastWin32Error()})");
            if (!DeviceIoControl(handle, 0x000900A4 /* FSCTL_SET_REPARSE_POINT */, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
                Assert.Ignore($"Cannot set a reparse point on this volume (error {Marshal.GetLastWin32Error()})");
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint bufferLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateDirectoryW(string path, IntPtr security);
    }
}
