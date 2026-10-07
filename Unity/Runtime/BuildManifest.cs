using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildForge.Runtime
{
    /// <summary>
    /// Represents a build manifest containing metadata about a build.
    /// Written during the build process and readable at runtime.
    /// </summary>
    [Serializable]
    public class BuildManifest
    {
        [SerializeField] string buildProfileName;
        [SerializeField] string buildTimestamp;
        [SerializeField] string buildTarget;
        [SerializeField] string buildTargetGroup;
        [SerializeField] string productName;
        [SerializeField] string productVersion;
        [SerializeField] string buildNumber;
        [SerializeField] string unityVersion;
        [SerializeField] string buildForgeVersion;
        [SerializeField] bool developmentBuild;
        [SerializeField] string managedCodeVariant;
        [SerializeField] string buildVariant;
        [SerializeField] List<ManifestSection> pluginSections = new();

        /// <summary>The name of the Unity Build Profile the build was made from.</summary>
        public string BuildProfileName
        {
            get => buildProfileName;
            set => buildProfileName = value;
        }

        public string BuildTimestamp
        {
            get => buildTimestamp;
            set => buildTimestamp = value;
        }

        public string BuildTarget
        {
            get => buildTarget;
            set => buildTarget = value;
        }

        public string BuildTargetGroup
        {
            get => buildTargetGroup;
            set => buildTargetGroup = value;
        }

        public string ProductName
        {
            get => productName;
            set => productName = value;
        }

        public string ProductVersion
        {
            get => productVersion;
            set => productVersion = value;
        }

        public string BuildNumber
        {
            get => buildNumber;
            set => buildNumber = value;
        }

        public string UnityVersion
        {
            get => unityVersion;
            set => unityVersion = value;
        }

        public string BuildForgeVersion
        {
            get => buildForgeVersion;
            set => buildForgeVersion = value;
        }

        /// <summary>The Build Forge build variant the player was built with: "Internal", …, or "Default" once the project defines variants; empty when it defines none.</summary>
        public string BuildVariant
        {
            get => buildVariant;
            set => buildVariant = value;
        }

        /// <summary>The Build Profile's Development Build option at build time.</summary>
        public bool DevelopmentBuild
        {
            get => developmentBuild;
            set => developmentBuild = value;
        }

        /// <summary>
        /// The Managed Code Variant player setting at build time ("Debug",
        /// "Checked", "Instrumented" or "Release"). Empty on editors before
        /// Unity 6.6, where the variant was still implied by Development Build.
        /// </summary>
        public string ManagedCodeVariant
        {
            get => managedCodeVariant;
            set => managedCodeVariant = value;
        }

        public IReadOnlyList<ManifestSection> PluginSections => pluginSections;

        public void AddSection(ManifestSection section)
        {
            pluginSections.Add(section);
        }

        public ManifestSection GetSection(string sectionName)
        {
            return pluginSections.Find(s => s.Name == sectionName);
        }

        public string ToJson(bool prettyPrint = true)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }

        public static BuildManifest FromJson(string json)
        {
            return JsonUtility.FromJson<BuildManifest>(json);
        }
    }

    [Serializable]
    public class ManifestSection
    {
        [SerializeField] string name;
        [SerializeField] List<ManifestEntry> entries = new();

        public string Name
        {
            get => name;
            set => name = value;
        }

        public IReadOnlyList<ManifestEntry> Entries => entries;

        public ManifestSection(string name)
        {
            this.name = name;
        }

        public void Add(string key, string value)
        {
            entries.Add(new ManifestEntry(key, value));
        }

        public string Get(string key)
        {
            var entry = entries.Find(e => e.Key == key);
            return entry?.Value;
        }
    }

    [Serializable]
    public class ManifestEntry
    {
        [SerializeField] string key;
        [SerializeField] string value;

        public string Key => key;
        public string Value => value;

        public ManifestEntry(string key, string value)
        {
            this.key = key;
            this.value = value;
        }
    }
}
