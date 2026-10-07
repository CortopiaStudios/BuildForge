using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildForge.Editor.Plugins
{
    /// <summary>
    /// Keeps other XR vendors' SDKs out of an Android profile's builds. The
    /// libraries of the excluded packages are never copied into the Gradle
    /// project (Unity's include-in-build delegate, set after the vendors' own
    /// pre-build steps). The manifest entries the vendors' Gradle steps write
    /// into every Android build cannot be prevented, so they are removed after
    /// those steps. A final check fails the build on anything left over.
    /// </summary>
    [ForgePlugin]
    internal class XRVendorFilterPlugin : IForgePlugin, IForgeGradleProcessor, IForgeManifestContributor
    {
        internal const string ConfigKey = "XRVendorFilter";
        internal const string LogPrefix = "[Build Forge/XR Vendor Filter]";

        public string DisplayName => "XR Vendor Filter";
        public string Description =>
            "Keep other XR vendors' SDKs out of this Android profile's builds: their libraries, and the manifest entries their Gradle steps write.";
        // Last among the bundled plugins, so its leftover check sees what every
        // other plugin's Gradle step left.
        public int Order => 1000;

        public bool IsApplicable(BuildProfile profile) => BuildProfileUtility.GetBuildTarget(profile) == BuildTarget.Android;

        public string NotApplicableReason(BuildProfile profile) => "Only applicable to Android build profiles.";

        public bool? IsEnabled(ForgeProfile forgeProfile) => Config(forgeProfile).enabled;

        public void SetEnabled(ForgeProfile forgeProfile, bool enabled)
        {
            var config = Config(forgeProfile);
            config.enabled = enabled;
            forgeProfile.SetPluginConfig(ConfigKey, config);
        }

        internal static XRVendorFilterConfig Config(ForgeProfile profile) => profile.GetPluginConfig<XRVendorFilterConfig>(ConfigKey);

        /// <summary>What the build in progress keeps out.</summary>
        internal sealed class Exclusion
        {
            public List<string> Packages;
            public HashSet<string> LibraryPaths;
            /// <summary>File and folder names of the excluded libraries that no included library shares.</summary>
            public HashSet<string> LibraryFileNames;
            public XRVendors.StripRules Rules;
            public bool CheckLeftovers;
        }

        /// <summary>The exclusion of the build in progress; null between builds.</summary>
        internal static Exclusion Active { get; private set; }

        /// <summary>
        /// Unity's include-in-build delegate for the excluded libraries: out
        /// while the build that excludes them runs, Unity's own decision
        /// otherwise. Vendors set their own delegates in every build's
        /// pre-build step, so leaving this one in place affects nothing else.
        /// </summary>
        internal static bool IncludeInBuild(string path) => Active == null || !Active.LibraryPaths.Contains(path);

        internal static Exclusion CreateExclusion(XRVendorFilterConfig config,
            IReadOnlyDictionary<string, XRVendors.AndroidLibraryPackage> installed, IEnumerable<string> allLibraryPaths)
        {
            var excluded = new HashSet<string>(config.excludedPackages
                .Where(installed.ContainsKey).SelectMany(p => installed[p].LibraryPaths));
            // A name an included library shares (two packages' libopenxr_loader.so)
            // is no sign of a leftover.
            var included = new HashSet<string>(allLibraryPaths.Where(p => !excluded.Contains(p)).Select(Path.GetFileName),
                StringComparer.OrdinalIgnoreCase);
            return new Exclusion
            {
                Packages = config.excludedPackages.ToList(),
                LibraryPaths = excluded,
                LibraryFileNames = new HashSet<string>(excluded.Select(Path.GetFileName).Where(n => !included.Contains(n)),
                    StringComparer.OrdinalIgnoreCase),
                Rules = XRVendors.RulesFor(config.excludedPackages, config.extraManifestPrefixes),
                CheckLeftovers = config.checkLeftovers,
            };
        }

        public void OnPreBuild(ForgeBuildContext context)
        {
            var config = Config(context.ForgeProfile);
            var installed = XRVendors.DetectAndroidLibraryPackages();
            var missing = config.excludedPackages.Where(p => !installed.ContainsKey(p)).ToList();
            if (missing.Count > 0)
                Debug.LogWarning($"{LogPrefix} {Names(missing)} {(missing.Count == 1 ? "is" : "are")} not installed or " +
                                 $"{(missing.Count == 1 ? "ships" : "ship")} no Android libraries; only {(missing.Count == 1 ? "its" : "their")} " +
                                 "manifest rules apply.");
            var allLibraryPaths = PluginImporter.GetAllImporters()
                .Where(i => i != null && XRVendors.IsAndroidLibrary(i.assetPath) && i.GetCompatibleWithPlatform(BuildTarget.Android))
                .Select(i => i.assetPath);
            Active = CreateExclusion(config, installed, allLibraryPaths);
            Debug.Log($"{LogPrefix} Keeping {Active.LibraryPaths.Count} Android {(Active.LibraryPaths.Count == 1 ? "library" : "libraries")} " +
                      $"of {Names(Active.Packages)} out of this build" +
                      (Active.Rules.IsEmpty ? "." : $", and removing {DescribeRules(Active.Rules)} after the vendors' Gradle steps."));
        }

        public void OnPostBuild(ForgeBuildContext context) => Active = null;

        public void OnPostGenerateGradleAndroidProject(ForgeBuildContext context, string path)
        {
            var active = Active;
            if (active == null)
                return;
            var root = VendorManifestStripper.ProjectRoot(path);
            if (!active.Rules.IsEmpty)
            {
                var removed = VendorManifestStripper.StripProject(root, active.Rules);
                Debug.Log(removed.Count == 0
                    ? $"{LogPrefix} No vendor manifest entries to remove."
                    : $"{LogPrefix} Removed {removed.Count} vendor manifest {(removed.Count == 1 ? "entry" : "entries")}:\n" +
                      string.Join("\n", removed));
            }
            if (!active.CheckLeftovers)
                return;
            var leftovers = VendorManifestStripper.FindLeftovers(root, active.Rules, active.LibraryFileNames);
            if (leftovers.Count > 0)
                throw new BuildFailedException(LeftoverMessage(leftovers));
            Debug.Log($"{LogPrefix} No excluded libraries or vendor manifest entries are left in the Gradle project.");
        }

        internal static string LeftoverMessage(IReadOnlyCollection<string> leftovers)
            => $"{LogPrefix} {leftovers.Count} excluded vendor {(leftovers.Count == 1 ? "item is" : "items are")} still in the " +
               "Gradle project:\n" + string.Join("\n", leftovers) + "\nExclude the package that provides " +
               $"{(leftovers.Count == 1 ? "it" : "them")}, add a manifest prefix, or turn off Check for Leftovers.";

        public void ContributeToManifest(BuildManifest manifest, ForgeBuildContext context)
        {
            var active = Active;
            if (active == null)
                return;
            var section = new ManifestSection("XRVendorFilter");
            section.Add("excludedPackages", string.Join(";", active.Packages));
            section.Add("removedManifestPrefixes", string.Join(";", active.Rules.Prefixes));
            manifest.AddSection(section);
        }

        public IReadOnlyList<string> Validate(ForgeProfile forgeProfile)
        {
            var config = Config(forgeProfile);
            var warnings = new List<string>();
            if (!config.enabled)
                return warnings;
            if (config.excludedPackages.Count == 0 && config.extraManifestPrefixes.All(string.IsNullOrWhiteSpace))
                warnings.Add("XR Vendor Filter is enabled, but excludes no package.");
            var missing = config.excludedPackages.Where(p => !CachedPackages().ContainsKey(p)).ToList();
            if (missing.Count > 0)
                warnings.Add($"XR Vendor Filter excludes {Names(missing)}, which {(missing.Count == 1 ? "is" : "are")} not installed " +
                             $"or {(missing.Count == 1 ? "ships" : "ship")} no Android libraries.");
            if (config.excludedPackages.Contains("com.unity.xr.openxr"))
                warnings.Add("XR Vendor Filter excludes com.unity.xr.openxr, whose OpenXR loader every OpenXR headset needs.");
            return warnings;
        }

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI)
        {
            var config = Config(forgeProfile);
            if (config.excludedPackages.Count == 0)
                return "excludes nothing";
            var rules = XRVendors.RulesFor(config.excludedPackages, config.extraManifestPrefixes);
            return $"keeps {Names(config.excludedPackages)} out" + (rules.IsEmpty ? "" : $"; removes {DescribeRules(rules)}");
        }

        internal static string DescribeRules(XRVendors.StripRules rules)
            => string.Join(", ", rules.Prefixes.Select(p => p + "*")
                   .Concat(rules.Namespaces.Select(n => n == XRVendors.HorizonOSNamespace ? "Horizon OS" : n)))
               + " manifest entries";

        static string Names(IReadOnlyList<string> names)
            => names.Count <= 1 ? string.Join("", names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];

        // Detection walks every plugin importer, too slow for each repaint.
        static Dictionary<string, XRVendors.AndroidLibraryPackage> cachedPackages;

        [InitializeOnLoadMethod]
        static void InvalidateOnProjectChange() => EditorApplication.projectChanged += () => cachedPackages = null;

        static Dictionary<string, XRVendors.AndroidLibraryPackage> CachedPackages()
            => cachedPackages ??= XRVendors.DetectAndroidLibraryPackages();

        const string OtherPackagesExpandedKey = "BuildForge.XRVendorFilter.OtherPackagesExpanded";

        /// <summary>The package's Excluded toggle; true when it changed the config.</summary>
        static bool DrawPackageToggle(XRVendorFilterConfig config, XRVendors.AndroidLibraryPackage package)
        {
            var vendor = XRVendors.VendorOf(package.Name);
            var rules = XRVendors.RulesFor(new[] { package.Name }, null);
            var tooltip = $"{package.Name}: {package.LibraryPaths.Count} Android " +
                          $"{(package.LibraryPaths.Count == 1 ? "library" : "libraries")}" +
                          (vendor == null || rules.IsEmpty ? "" : $". {vendor.Name}'s manifest entries are removed when excluded: " +
                              DescribeRules(rules) + ".");
            var label = vendor == null ? package.DisplayName : $"{package.DisplayName} ({vendor.Name})";
            var excluded = config.excludedPackages.Contains(package.Name);
            if (EditorGUILayout.ToggleLeft(new GUIContent(label, tooltip), excluded) == excluded)
                return false;
            if (excluded)
                config.excludedPackages.Remove(package.Name);
            else
                config.excludedPackages.Add(package.Name);
            return true;
        }

        public void OnDrawProfileGUI(ForgeProfile forgeProfile)
        {
            var config = Config(forgeProfile);
            var changed = false;
            var installed = CachedPackages();

            EditorGUILayout.LabelField("Excluded Packages", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Their Android libraries stay out of this profile's builds. For known vendors, the " +
                                       "manifest entries their Gradle steps write are removed as well.", EditorStyles.wordWrappedMiniLabel);
            if (installed.Count == 0)
                EditorGUILayout.HelpBox("No installed package ships Android libraries.", MessageType.Info);

            var groups = XRVendors.Group(installed.Values);
            foreach (var package in groups.Vendors)
                changed |= DrawPackageToggle(config, package);

            var folded = groups.UnityXR.Concat(groups.Other).ToList();
            if (folded.Count > 0)
            {
                var excludedFolded = folded.Count(p => config.excludedPackages.Contains(p.Name));
                var expanded = SessionState.GetBool(OtherPackagesExpandedKey, false);
                var title = $"Other packages with Android libraries ({folded.Count}" +
                            (excludedFolded > 0 ? $", {excludedFolded} excluded)" : ")");
                var newExpanded = EditorGUILayout.Foldout(expanded, title, true);
                if (newExpanded != expanded)
                    SessionState.SetBool(OtherPackagesExpandedKey, newExpanded);
                if (newExpanded)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        if (groups.UnityXR.Count > 0)
                        {
                            EditorGUILayout.LabelField("Unity XR: every OpenXR headset needs these", EditorStyles.miniBoldLabel);
                            foreach (var package in groups.UnityXR)
                                changed |= DrawPackageToggle(config, package);
                        }
                        if (groups.Other.Count > 0)
                        {
                            if (groups.UnityXR.Count > 0)
                                EditorGUILayout.LabelField("Other", EditorStyles.miniBoldLabel);
                            foreach (var package in groups.Other)
                                changed |= DrawPackageToggle(config, package);
                        }
                    }
                }
            }

            foreach (var stale in config.excludedPackages.Where(p => !installed.ContainsKey(p)).ToList())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(stale, "not installed, or no Android libraries");
                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        config.excludedPackages.Remove(stale);
                        changed = true;
                    }
                }
            }

            EditorGUILayout.Space(4);
            var extras = string.Join(", ", config.extraManifestPrefixes);
            var newExtras = EditorGUILayout.DelayedTextField(new GUIContent("Extra Manifest Prefixes",
                "Comma-separated android:name prefixes to remove as well, for packages without built-in rules " +
                "(for example com.example.)."), extras);
            if (newExtras != extras)
            {
                config.extraManifestPrefixes = newExtras.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                changed = true;
            }

            var check = EditorGUILayout.Toggle(new GUIContent("Check for Leftovers",
                "Fail the build when an excluded library or a removed manifest entry is still in the Gradle project, " +
                "including the manifests inside the remaining libraries, which Gradle merges later."), config.checkLeftovers);
            if (check != config.checkLeftovers)
            {
                config.checkLeftovers = check;
                changed = true;
            }

            if (GUILayout.Button("Refresh Package List", GUILayout.Width(150)))
                cachedPackages = null;

            if (changed)
                forgeProfile.SetPluginConfig(ConfigKey, config);
        }
    }

    /// <summary>
    /// Sets Unity's include-in-build delegate on the excluded libraries, after
    /// the vendors' own pre-build steps, which set theirs on some of the same
    /// libraries (Meta's on OVRPlugin, at order 3).
    /// </summary>
    internal sealed class XRVendorFilterPreprocess : IPreprocessBuildWithReport
    {
        public int callbackOrder => ForgeGradleCallback.CallbackOrder;

        public void OnPreprocessBuild(BuildReport report)
        {
            var active = XRVendorFilterPlugin.Active;
            if (active == null)
                return;
            foreach (var importer in PluginImporter.GetAllImporters())
                if (importer != null && active.LibraryPaths.Contains(importer.assetPath))
                    importer.SetIncludeInBuildDelegate(XRVendorFilterPlugin.IncludeInBuild);
        }
    }
}
