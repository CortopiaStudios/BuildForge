using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using BuildForge.Editor.Plugins;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class XRVendorFilterTests
    {
        // The vendor entries of a Steam Frame APK built with Meta's Core and
        // Platform SDKs installed, around the entries the build needs.
        const string FrameManifest = @"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" xmlns:horizonos=""http://schemas.horizonos/sdk"" package=""com.example.game"">
  <application>
    <activity android:name=""com.unity3d.player.UnityPlayerGameActivity"">
      <intent-filter>
        <action android:name=""android.intent.action.MAIN"" />
        <category android:name=""android.intent.category.LAUNCHER"" />
        <category android:name=""com.oculus.intent.category.VR"" />
        <category android:name=""org.khronos.openxr.intent.category.IMMERSIVE_HMD"" />
      </intent-filter>
      <meta-data android:name=""com.oculus.vr.focusaware"" android:value=""true"" />
    </activity>
    <meta-data android:name=""com.oculus.supportedDevices"" android:value=""quest2|questpro|quest3|quest3s"" />
    <meta-data android:name=""com.meta.store.defaultDeviceTargets"" android:value=""quest3"" />
  </application>
  <horizonos:uses-horizonos-supplement name=""horizonos-supplement-hzplatformclientcore"" minSupplementVersion=""1"" />
  <uses-permission android:name=""org.khronos.openxr.permission.OPENXR"" />
  <uses-permission android:name=""com.picovr.permission.EYE_TRACKING"" />
</manifest>
";

        static XRVendors.StripRules MetaRules => XRVendors.RulesFor(new[] { "com.meta.xr.sdk.core", "com.meta.xr.sdk.platform" }, null);

        string root;

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "BuildForgeVendorFilter-" + System.Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void RulesFor_KnownVendorsGiveTheirEntries_ExtrasAreAdded()
        {
            var rules = XRVendors.RulesFor(
                new[] { "com.meta.xr.sdk.core", "com.meta.xr.sdk.platform", "com.unity.xr.openxr.picoxr", "com.example.sdk" },
                new[] { " com.example. ", "" });
            CollectionAssert.AreEqual(new[] { "com.oculus.", "com.meta.", "com.pico.", "com.picovr.", "com.example." }, rules.Prefixes);
            CollectionAssert.AreEqual(new[] { XRVendors.HorizonOSNamespace }, rules.Namespaces);

            Assert.IsTrue(XRVendors.RulesFor(new[] { "com.unity.xr.openxr" }, null).IsEmpty, "Not a vendor SDK.");
            Assert.AreEqual("Meta", XRVendors.VendorOf("com.unity.xr.oculus").Name);
            Assert.AreEqual("Pico", XRVendors.VendorOf("com.unity.xr.picoxr").Name);
        }

        [Test]
        public void RulesFor_Steamworks_IsTheSteamVendorWithoutManifestEntries()
        {
            Assert.AreEqual("Steam", XRVendors.VendorOf("com.rlabrecque.steamworks.net").Name);
            Assert.IsTrue(XRVendors.RulesFor(new[] { "com.rlabrecque.steamworks.net" }, null).IsEmpty);
        }

        [Test]
        public void Group_ListsVendorsByVendor_FoldsUnityXRAndTheRest()
        {
            var groups = XRVendors.Group(new[]
            {
                Package("com.virtuix.omniconnectsdk", "Omni Connect SDK"),
                Package("com.unity.xr.openxr", "OpenXR Plugin"),
                Package("com.rlabrecque.steamworks.net", "Steamworks.NET"),
                Package("com.unity.xr.management", "XR Plugin Management"),
                Package("com.unity.xr.openxr.picoxr", "PICO OpenXR Plugin"),
                Package("com.pico.example", "Another Pico SDK"),
                Package("com.meta.xr.sdk.platform", "Meta XR Platform SDK"),
            });

            CollectionAssert.AreEqual(new[] { "Meta XR Platform SDK", "Another Pico SDK", "PICO OpenXR Plugin", "Steamworks.NET" },
                groups.Vendors.Select(p => p.DisplayName), "By vendor, then by name.");
            CollectionAssert.AreEqual(new[] { "OpenXR Plugin", "XR Plugin Management" }, groups.UnityXR.Select(p => p.DisplayName));
            CollectionAssert.AreEqual(new[] { "Omni Connect SDK" }, groups.Other.Select(p => p.DisplayName));
        }

        static XRVendors.AndroidLibraryPackage Package(string name, string displayName) => new() { Name = name, DisplayName = displayName };

        [Test]
        public void Strip_RemovesTheVendorsEntries_KeepsWhatTheBuildNeeds()
        {
            var document = new XmlDocument();
            document.LoadXml(FrameManifest);

            var removed = VendorManifestStripper.Strip(document, MetaRules);

            CollectionAssert.AreEquivalent(new[]
            {
                "category com.oculus.intent.category.VR",
                "meta-data com.oculus.vr.focusaware",
                "meta-data com.oculus.supportedDevices",
                "meta-data com.meta.store.defaultDeviceTargets",
                "uses-horizonos-supplement horizonos-supplement-hzplatformclientcore",
            }, removed);
            var xml = document.OuterXml;
            StringAssert.DoesNotContain("xmlns:horizonos", xml, "The namespace declaration goes with its last element.");
            StringAssert.Contains("android.intent.category.LAUNCHER", xml);
            StringAssert.Contains("org.khronos.openxr.intent.category.IMMERSIVE_HMD", xml);
            StringAssert.Contains("org.khronos.openxr.permission.OPENXR", xml);
            StringAssert.Contains("com.picovr.permission.EYE_TRACKING", xml, "Pico was not excluded.");
        }

        [Test]
        public void StripProject_ChangesTheSourcesOnly_FindLeftoversReportsWhatRemains()
        {
            var library = Path.Combine(root, "unityLibrary");
            var sourceManifest = Write(Path.Combine(library, "src", "main", "AndroidManifest.xml"), FrameManifest);
            var staleOutput = Write(Path.Combine(library, "build", "intermediates", "merged_manifests", "AndroidManifest.xml"), FrameManifest);
            Write(Path.Combine(library, "src", "main", "jniLibs", "arm64-v8a", "libovrplatformloader.so"), "");
            Write(Path.Combine(library, "src", "main", "jniLibs", "arm64-v8a", "libopenxr_loader.so"), "");
            var aar = Path.Combine(library, "libs", "vendor.aar");
            Directory.CreateDirectory(Path.GetDirectoryName(aar));
            using (var archive = new ZipArchive(File.Create(aar), ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("AndroidManifest.xml").Open()))
                writer.Write(@"<manifest xmlns:android=""http://schemas.android.com/apk/res/android""><application>" +
                             @"<meta-data android:name=""com.oculus.platform.app"" android:value=""1"" /></application></manifest>");

            Assert.AreEqual(Path.GetFullPath(root), VendorManifestStripper.ProjectRoot(library));

            var removed = VendorManifestStripper.StripProject(root, MetaRules);
            Assert.AreEqual(5, removed.Count);
            StringAssert.DoesNotContain("com.oculus.", File.ReadAllText(sourceManifest));
            Assert.AreEqual(FrameManifest, File.ReadAllText(staleOutput), "Gradle's outputs of an earlier build stay as they are.");

            var leftovers = VendorManifestStripper.FindLeftovers(root, MetaRules, new[] { "libovrplatformloader.so" });
            CollectionAssert.AreEquivalent(new[]
            {
                $"{Path.Combine("unityLibrary", "libs", "vendor.aar")} (merged by Gradle): meta-data com.oculus.platform.app",
                $"{Path.Combine("unityLibrary", "src", "main", "jniLibs", "arm64-v8a", "libovrplatformloader.so")}: excluded library",
            }, leftovers);
        }

        [Test]
        public void CreateExclusion_ANameAnIncludedLibraryShares_IsNoLeftover()
        {
            var vendor = new XRVendors.AndroidLibraryPackage { Name = "com.unity.xr.picoxr", DisplayName = "PICO" };
            vendor.LibraryPaths.Add("Packages/com.unity.xr.picoxr/Plugins/libopenxr_loader.so");
            vendor.LibraryPaths.Add("Packages/com.unity.xr.picoxr/Plugins/libpxrplatformloader.so");
            var installed = new Dictionary<string, XRVendors.AndroidLibraryPackage> { [vendor.Name] = vendor };
            var all = vendor.LibraryPaths.Append("Packages/com.unity.xr.openxr/Runtime/android/libopenxr_loader.so");
            var config = new XRVendorFilterConfig { excludedPackages = { "com.unity.xr.picoxr", "com.meta.xr.sdk.platform" } };

            var exclusion = XRVendorFilterPlugin.CreateExclusion(config, installed, all);

            CollectionAssert.AreEquivalent(vendor.LibraryPaths, exclusion.LibraryPaths);
            CollectionAssert.AreEquivalent(new[] { "libpxrplatformloader.so" }, exclusion.LibraryFileNames,
                "OpenXR's own loader has the same name and is included.");
            CollectionAssert.AreEqual(new[] { "com.pico.", "com.picovr.", "com.oculus.", "com.meta." }, exclusion.Rules.Prefixes,
                "A known vendor's rules apply although its package ships no Android libraries here.");
            Assert.IsTrue(XRVendorFilterPlugin.IncludeInBuild(vendor.LibraryPaths[0]), "No build is excluding anything.");
        }

        [TestCase("Packages/com.meta.xr.sdk.core/Plugins/Android/OVRPlugin.aar", true)]
        [TestCase("Packages/com.meta.xr.sdk.platform/Plugins/Android64/libovrplatformloader.so", true)]
        [TestCase("Assets/Plugins/Android/vendor.jar", true)]
        [TestCase("Assets/Plugins/Android/Bridge.java", true)]
        [TestCase("Assets/Plugins/Android/Vendor.androidlib", true)]
        [TestCase("Packages/com.meta.xr.sdk.core/Scripts/Oculus.VR.dll", false)]
        [TestCase("Assets/Plugins/Android/AndroidManifest.xml", false)]
        public void IsAndroidLibrary_CoversWhatUnityCopiesIntoTheGradleProject(string path, bool expected)
            => Assert.AreEqual(expected, XRVendors.IsAndroidLibrary(path));

        [Test]
        public void LeftoverMessage_NamesEachItemAndTheWaysOut()
        {
            var message = XRVendorFilterPlugin.LeftoverMessage(new[] { "a: excluded library", "b: meta-data com.oculus.x" });
            StringAssert.Contains("2 excluded vendor items are still in the Gradle project", message);
            StringAssert.Contains("a: excluded library\nb: meta-data com.oculus.x", message);
            StringAssert.Contains("turn off Check for Leftovers", message);
        }

        static string Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }
    }
}
