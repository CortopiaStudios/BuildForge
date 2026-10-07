using System.IO;
using BuildForge.Editor.Plugins;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class AndroidManifestPluginTests
    {
        string root;

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "BuildForgeManifest-" + System.Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void SwapAndRestore_PutBackTheProjectsFile()
        {
            var path = Path.Combine(root, "Plugins", "Android", "AndroidManifest.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "project");

            var state = AndroidManifestPlugin.Swap(path, System.Text.Encoding.UTF8.GetBytes("profile"));
            Assert.AreEqual("profile", File.ReadAllText(path));
            File.WriteAllText(path + ".meta", "imported during the build");

            AndroidManifestPlugin.Restore(path, state);
            Assert.AreEqual("project", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + ".meta"), "The project had no .meta for it.");
            CollectionAssert.IsEmpty(state.createdDirectories);
        }

        [Test]
        public void SwapAndRestore_WithoutAProjectFile_LeaveNothingBehind()
        {
            var path = Path.Combine(root, "Plugins", "Android", "AndroidManifest.xml");
            Directory.CreateDirectory(root);

            var state = AndroidManifestPlugin.Swap(path, System.Text.Encoding.UTF8.GetBytes("profile"));
            Assert.AreEqual("profile", File.ReadAllText(path));
            File.WriteAllText(Path.Combine(root, "Plugins") + ".meta", "imported during the build");

            AndroidManifestPlugin.Restore(path, state);
            Assert.IsFalse(File.Exists(path));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "Plugins")), "The folders it created go too.");
            Assert.IsFalse(File.Exists(Path.Combine(root, "Plugins") + ".meta"));
            Assert.IsTrue(Directory.Exists(root));
        }

        [Test]
        public void ManifestError_OnlyAcceptsAnAndroidManifest()
        {
            Assert.IsNull(AndroidManifestPlugin.ManifestError(
                @"<manifest xmlns:android=""http://schemas.android.com/apk/res/android""><application /></manifest>"));
            StringAssert.Contains("root element is not <manifest>", AndroidManifestPlugin.ManifestError("<resources />"));
            StringAssert.Contains("is not valid XML", AndroidManifestPlugin.ManifestError("<manifest>"));
        }

        [Test]
        public void PlayerSettingsHaveTheCustomMainManifestSwitch()
            => Assert.IsNotNull(AndroidManifestPlugin.Property());
    }
}
