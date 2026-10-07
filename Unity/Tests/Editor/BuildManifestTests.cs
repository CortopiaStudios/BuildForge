using BuildForge.Runtime;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class BuildManifestTests
    {
        [Test]
        public void CreateManifest_SetsBasicProperties()
        {
            var manifest = new BuildManifest
            {
                BuildProfileName = "TestProfile",
                BuildTarget = "StandaloneWindows64",
                BuildTargetGroup = "Standalone",
                ProductName = "TestGame",
                ProductVersion = "1.0.0",
                UnityVersion = "6000.3.0f1",
                BuildForgeVersion = "0.1.0",
                DevelopmentBuild = true,
                ManagedCodeVariant = "Debug"
            };

            Assert.AreEqual("TestProfile", manifest.BuildProfileName);
            Assert.AreEqual("StandaloneWindows64", manifest.BuildTarget);
            Assert.AreEqual("TestGame", manifest.ProductName);
            Assert.AreEqual("1.0.0", manifest.ProductVersion);
            Assert.IsTrue(manifest.DevelopmentBuild);
            Assert.AreEqual("Debug", manifest.ManagedCodeVariant);
        }

        [Test]
        public void ManifestSection_AddAndGet()
        {
            var section = new ManifestSection("TestSection");
            section.Add("key1", "value1");
            section.Add("key2", "value2");

            Assert.AreEqual("value1", section.Get("key1"));
            Assert.AreEqual("value2", section.Get("key2"));
            Assert.IsNull(section.Get("nonexistent"));
        }

        [Test]
        public void Manifest_AddAndGetSection()
        {
            var manifest = new BuildManifest();

            var section = new ManifestSection("MyPlugin");
            section.Add("setting", "enabled");
            manifest.AddSection(section);

            var retrieved = manifest.GetSection("MyPlugin");
            Assert.IsNotNull(retrieved);
            Assert.AreEqual("enabled", retrieved.Get("setting"));

            Assert.IsNull(manifest.GetSection("NonExistent"));
        }

        [Test]
        public void Manifest_SerializationRoundTrip()
        {
            var manifest = new BuildManifest
            {
                BuildProfileName = "RoundTrip",
                BuildVariant = "Internal",
                DevelopmentBuild = true,
                ManagedCodeVariant = "Release",
                BuildTarget = "Android",
                ProductName = "TestApp",
                ProductVersion = "2.0.0"
            };

            var section = new ManifestSection("Plugin");
            section.Add("feature", "on");
            manifest.AddSection(section);

            var json = manifest.ToJson();
            var deserialized = BuildManifest.FromJson(json);

            Assert.AreEqual("RoundTrip", deserialized.BuildProfileName);
            Assert.AreEqual("Internal", deserialized.BuildVariant);
            Assert.IsTrue(deserialized.DevelopmentBuild);
            Assert.AreEqual("Release", deserialized.ManagedCodeVariant);
            Assert.AreEqual("Android", deserialized.BuildTarget);
            Assert.AreEqual("TestApp", deserialized.ProductName);
            Assert.AreEqual(1, deserialized.PluginSections.Count);
            Assert.AreEqual("on", deserialized.PluginSections[0].Get("feature"));
        }
    }
}
