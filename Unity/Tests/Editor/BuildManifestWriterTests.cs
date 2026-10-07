using System;
using System.IO;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.TestTools;

namespace BuildForge.Tests.Editor
{
    public class BuildManifestWriterTests
    {
        string backup;
        bool writeManifest;
        bool logManifest;

        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(ForgeBuildRunner.IsForgeBuildInProgress,
                "Do not run this fixture during a player build.");
            writeManifest = ForgeSettings.instance.WriteBuildManifest;
            logManifest = ForgeSettings.instance.LogManifestContents;
            ForgeSettings.instance.WriteBuildManifest = true;
            ForgeSettings.instance.LogManifestContents = false;
            backup = BuildManifestWriter.ManifestDirectory + ".test-backup-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.GetDirectoryName(BuildManifestWriter.ManifestDirectory));
            if (Directory.Exists(BuildManifestWriter.ManifestDirectory))
                Directory.Move(BuildManifestWriter.ManifestDirectory, backup);
            else if (File.Exists(BuildManifestWriter.ManifestDirectory))
                File.Move(BuildManifestWriter.ManifestDirectory, backup);
        }

        [TearDown]
        public void TearDown()
        {
            SessionState.SetBool("BuildForge.BuildInProgress", false);
            ForgeSettings.instance.WriteBuildManifest = writeManifest;
            ForgeSettings.instance.LogManifestContents = logManifest;
            BuildManifestWriter.CleanupManifest();
            if (File.Exists(BuildManifestWriter.ManifestDirectory))
                File.Delete(BuildManifestWriter.ManifestDirectory);
            if (Directory.Exists(backup))
                Directory.Move(backup, BuildManifestWriter.ManifestDirectory);
            else if (File.Exists(backup))
                File.Move(backup, BuildManifestWriter.ManifestDirectory);
        }

        [Test]
        public void Write_ReplacesStaleContentsWithCurrentManifest()
        {
            Directory.CreateDirectory(BuildManifestWriter.ManifestDirectory);
            File.WriteAllText(Path.Combine(BuildManifestWriter.ManifestDirectory, "stale.json"), "old build");

            BuildManifestWriter.WriteManifest(new BuildManifest { ProductVersion = "2.4.6" });

            Assert.AreEqual("2.4.6",
                BuildManifest.FromJson(File.ReadAllText(BuildManifestWriter.ManifestPath)).ProductVersion);
            Assert.AreEqual(1, Directory.GetFiles(BuildManifestWriter.ManifestDirectory).Length);
        }

        [Test]
        public void Write_DirectoryBlockedByFile_PropagatesFailure()
        {
            File.WriteAllText(BuildManifestWriter.ManifestDirectory, "blocking file");
            LogAssert.Expect(LogType.Error, new Regex("Failed to write build manifest"));

            Assert.Throws<IOException>(() => BuildManifestWriter.WriteManifest(new BuildManifest()));
        }

        [Test]
        public void Inject_EnabledManifestMissing_FailsBuild()
        {
            SessionState.SetBool("BuildForge.BuildInProgress", true);

            Assert.Throws<BuildFailedException>(() => new BuildManifestInjector().PrepareForBuild(null));
        }

        [Test]
        public void Inject_DisabledManifest_DoesNotRequireOrInjectStaleFile()
        {
            SessionState.SetBool("BuildForge.BuildInProgress", true);
            ForgeSettings.instance.WriteBuildManifest = false;
            Directory.CreateDirectory(BuildManifestWriter.ManifestDirectory);
            File.WriteAllText(BuildManifestWriter.ManifestPath, "stale manifest");

            Assert.DoesNotThrow(() => new BuildManifestInjector().PrepareForBuild(null));
        }

        [Test]
        public void Inject_OutsideForgeBuild_DoesNotRequireManifest()
        {
            Assert.DoesNotThrow(() => new BuildManifestInjector().PrepareForBuild(null));
        }
    }
}
