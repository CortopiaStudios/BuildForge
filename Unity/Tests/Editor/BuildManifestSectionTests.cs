using BuildForge.Runtime;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class BuildManifestSectionTests
    {
        [Test]
        public void AddSection_GetSection_RoundTrips()
        {
            var manifest = new BuildManifest();
            var section = new ManifestSection("Git");
            section.Add("branch", "main");
            section.Add("commit", "abc123");
            manifest.AddSection(section);

            var retrieved = manifest.GetSection("Git");
            Assert.IsNotNull(retrieved);
            Assert.AreEqual("main", retrieved.Get("branch"));
            Assert.AreEqual("abc123", retrieved.Get("commit"));
        }

        [Test]
        public void GetSection_Missing_ReturnsNull()
        {
            var manifest = new BuildManifest();
            Assert.IsNull(manifest.GetSection("NonExistent"));
        }

        [Test]
        public void SectionGet_MissingKey_ReturnsNull()
        {
            var section = new ManifestSection("Test");
            section.Add("key1", "value1");
            Assert.IsNull(section.Get("key2"));
        }

        [Test]
        public void MultipleSections_IndependentlyRetrievable()
        {
            var manifest = new BuildManifest();

            var git = new ManifestSection("Git");
            git.Add("branch", "main");
            manifest.AddSection(git);

            var build = new ManifestSection("BuildNumber");
            build.Add("buildNumber", "42");
            manifest.AddSection(build);

            Assert.AreEqual("main", manifest.GetSection("Git").Get("branch"));
            Assert.AreEqual("42", manifest.GetSection("BuildNumber").Get("buildNumber"));
        }

        [Test]
        public void Section_Entries_Readable()
        {
            var section = new ManifestSection("Test");
            section.Add("a", "1");
            section.Add("b", "2");

            Assert.AreEqual(2, section.Entries.Count);
            Assert.AreEqual("a", section.Entries[0].Key);
            Assert.AreEqual("1", section.Entries[0].Value);
            Assert.AreEqual("b", section.Entries[1].Key);
            Assert.AreEqual("2", section.Entries[1].Value);
        }

        [Test]
        public void Manifest_JsonRoundTrip_PreservesSections()
        {
            var manifest = new BuildManifest
            {
                BuildProfileName = "Release",
                ProductVersion = "1.0.0"
            };

            var section = new ManifestSection("Git");
            section.Add("branch", "main");
            manifest.AddSection(section);

            var json = manifest.ToJson();
            var restored = BuildManifest.FromJson(json);

            Assert.AreEqual("Release", restored.BuildProfileName);
            Assert.AreEqual("1.0.0", restored.ProductVersion);
            Assert.AreEqual(1, restored.PluginSections.Count);
            Assert.AreEqual("main", restored.GetSection("Git").Get("branch"));
        }
    }
}
