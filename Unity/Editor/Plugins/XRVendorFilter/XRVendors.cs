using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace BuildForge.Editor.Plugins
{
    /// <summary>
    /// What the XR Vendor Filter knows about vendors: which packages are whose,
    /// which manifest entries their Gradle steps write, and which installed
    /// packages ship Android libraries.
    /// </summary>
    internal static class XRVendors
    {
        internal sealed class Vendor
        {
            public string Name;
            public Func<string, bool> OwnsPackage;
            /// <summary>android:name prefixes of the entries the vendor writes.</summary>
            public string[] ManifestPrefixes;
            /// <summary>XML namespaces of the vendor's own manifest elements.</summary>
            public string[] ManifestNamespaces;
        }

        internal const string HorizonOSNamespace = "http://schemas.horizonos/sdk";

        // Meta's Core SDK patches every Android build's manifest (the Oculus VR
        // category always, supported devices and focus awareness for any Quest
        // target), and its Platform SDK adds the Horizon OS supplement.
        static readonly Vendor[] Known =
        {
            new()
            {
                Name = "Meta",
                OwnsPackage = p => p.StartsWith("com.meta.xr.", StringComparison.Ordinal) || p == "com.unity.xr.oculus",
                ManifestPrefixes = new[] { "com.oculus.", "com.meta." },
                ManifestNamespaces = new[] { HorizonOSNamespace },
            },
            new()
            {
                Name = "Pico",
                OwnsPackage = p => p == "com.unity.xr.picoxr" || p == "com.unity.xr.openxr.picoxr"
                    || p.StartsWith("com.pico.", StringComparison.Ordinal),
                ManifestPrefixes = new[] { "com.pico.", "com.picovr." },
                ManifestNamespaces = Array.Empty<string>(),
            },
            // Steamworks.NET ships the Steam API's Android library and has no build steps.
            new()
            {
                Name = "Steam",
                OwnsPackage = p => p == "com.rlabrecque.steamworks.net",
                ManifestPrefixes = Array.Empty<string>(),
                ManifestNamespaces = Array.Empty<string>(),
            },
        };

        /// <summary>The known vendor of a package, or null.</summary>
        internal static Vendor VendorOf(string packageName)
            => packageName == null ? null : Known.FirstOrDefault(v => v.OwnsPackage(packageName));

        /// <summary>Unity's XR packages, which every OpenXR headset needs.</summary>
        internal static bool IsUnityXR(string packageName)
            => packageName == "com.unity.xr.openxr" || packageName == "com.unity.xr.management";

        internal sealed class StripRules
        {
            public readonly List<string> Prefixes = new();
            public readonly List<string> Namespaces = new();
            public bool IsEmpty => Prefixes.Count == 0 && Namespaces.Count == 0;
        }

        /// <summary>The manifest entries to remove for the excluded packages, plus the extra prefixes.</summary>
        internal static StripRules RulesFor(IEnumerable<string> excludedPackages, IEnumerable<string> extraPrefixes)
        {
            var rules = new StripRules();
            foreach (var vendor in excludedPackages.Select(VendorOf).Where(v => v != null).Distinct())
            {
                rules.Prefixes.AddRange(vendor.ManifestPrefixes.Where(p => !rules.Prefixes.Contains(p)));
                rules.Namespaces.AddRange(vendor.ManifestNamespaces.Where(n => !rules.Namespaces.Contains(n)));
            }
            foreach (var extra in extraPrefixes ?? Enumerable.Empty<string>())
            {
                var prefix = extra?.Trim();
                if (!string.IsNullOrEmpty(prefix) && !rules.Prefixes.Contains(prefix))
                    rules.Prefixes.Add(prefix);
            }
            return rules;
        }

        static readonly HashSet<string> AndroidLibraryExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".aar", ".jar", ".so", ".java", ".kt", ".androidlib",
        };

        /// <summary>
        /// A plugin that Unity copies into the Gradle project: native and Java
        /// libraries and sources. Managed assemblies are left alone, since game
        /// code may reference them.
        /// </summary>
        internal static bool IsAndroidLibrary(string assetPath) => AndroidLibraryExtensions.Contains(Path.GetExtension(assetPath));

        internal sealed class AndroidLibraryPackage
        {
            public string Name;
            public string DisplayName;
            public readonly List<string> LibraryPaths = new();
        }

        /// <summary>Installed packages with Android libraries, by package name.</summary>
        internal static Dictionary<string, AndroidLibraryPackage> DetectAndroidLibraryPackages()
        {
            var packages = new Dictionary<string, AndroidLibraryPackage>();
            foreach (var importer in PluginImporter.GetAllImporters())
            {
                if (importer == null || !IsAndroidLibrary(importer.assetPath)
                    || !importer.GetCompatibleWithPlatform(BuildTarget.Android))
                    continue;
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(importer.assetPath);
                if (info == null)
                    continue;
                if (!packages.TryGetValue(info.name, out var package))
                    packages[info.name] = package = new AndroidLibraryPackage
                    {
                        Name = info.name,
                        DisplayName = string.IsNullOrEmpty(info.displayName) ? info.name : info.displayName,
                    };
                package.LibraryPaths.Add(importer.assetPath);
            }
            return packages;
        }

        internal sealed class PackageGroups
        {
            public readonly List<AndroidLibraryPackage> Vendors = new();
            public readonly List<AndroidLibraryPackage> UnityXR = new();
            public readonly List<AndroidLibraryPackage> Other = new();
        }

        /// <summary>
        /// The packages as the inspector lists them: known vendors' packages by
        /// vendor, then Unity's XR packages and the rest, each by display name.
        /// </summary>
        internal static PackageGroups Group(IEnumerable<AndroidLibraryPackage> packages)
        {
            var groups = new PackageGroups();
            foreach (var package in packages
                         .OrderBy(p => VendorOf(p.Name) is { } vendor ? Array.IndexOf(Known, vendor) : Known.Length)
                         .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                if (VendorOf(package.Name) != null)
                    groups.Vendors.Add(package);
                else if (IsUnityXR(package.Name))
                    groups.UnityXR.Add(package);
                else
                    groups.Other.Add(package);
            }
            return groups;
        }
    }
}
