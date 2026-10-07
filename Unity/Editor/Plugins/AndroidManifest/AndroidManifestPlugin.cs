using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Runtime;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.Plugins
{
    /// <summary>
    /// Gives an Android profile its own main manifest. Unity reads the custom
    /// main manifest from one fixed path for every Android build, so the
    /// profile's file is put there for the build, with Custom Main Manifest
    /// on, and the project's file and setting are restored afterwards. The
    /// vendors' Gradle steps then patch the profile's manifest as usual.
    /// </summary>
    [ForgePlugin]
    internal class AndroidManifestPlugin : IForgePlugin, IForgeManifestContributor
    {
        internal const string ConfigKey = "AndroidManifest";
        internal const string MainManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        const string StateKey = "BuildForge.AndroidManifest.State";
        const string LogPrefix = "[Build Forge/Android Manifest]";

        public string DisplayName => "Android Manifest";
        public string Description => "Use this profile's own AndroidManifest.xml as the main manifest of its builds.";
        public int Order => 900;

        public bool IsApplicable(BuildProfile profile) => BuildProfileUtility.GetBuildTarget(profile) == BuildTarget.Android;

        public string NotApplicableReason(BuildProfile profile) => "Only applicable to Android build profiles.";

        static AndroidManifestConfig Config(ForgeProfile profile) => profile.GetPluginConfig<AndroidManifestConfig>(ConfigKey);

        /// <summary>What the build changed at the manifest path, so it can be undone.</summary>
        internal sealed class State
        {
            public bool existed;
            public byte[] bytes;
            public bool metaExisted;
            /// <summary>Folders created for the file, deepest first.</summary>
            public List<string> createdDirectories = new();
            public bool useCustomMainManifest;
        }

        public void OnPreBuild(ForgeBuildContext context)
        {
            var manifest = Config(context.ForgeProfile).mainManifest;
            if (manifest == null)
                return;
            var source = AssetDatabase.GetAssetPath(manifest);
            var error = ManifestError(manifest.text);
            if (error != null)
                throw new InvalidOperationException($"{LogPrefix} {source} {error}");
            var useCustom = UseCustomMainManifest;
            var state = Swap(MainManifestPath, manifest.bytes);
            state.useCustomMainManifest = useCustom;
            context.SetProperty(StateKey, state);
            UseCustomMainManifest = true;
            Debug.Log($"{LogPrefix} Main manifest for this build: {source}.");
        }

        public void OnPostBuild(ForgeBuildContext context)
        {
            var state = context.GetProperty<State>(StateKey);
            if (state == null)
                return;
            Restore(MainManifestPath, state);
            UseCustomMainManifest = state.useCustomMainManifest;
            Debug.Log($"{LogPrefix} Restored the project's main manifest.");
        }

        /// <summary>Puts <paramref name="bytes"/> at <paramref name="path"/> and records what was there.</summary>
        internal static State Swap(string path, byte[] bytes)
        {
            var state = new State { existed = File.Exists(path), metaExisted = File.Exists(path + ".meta") };
            if (state.existed)
                state.bytes = File.ReadAllBytes(path);
            for (var directory = Path.GetDirectoryName(path);
                 !string.IsNullOrEmpty(directory) && !Directory.Exists(directory);
                 directory = Path.GetDirectoryName(directory))
                state.createdDirectories.Add(directory);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, bytes);
            return state;
        }

        /// <summary>
        /// Puts back what <see cref="Swap"/> found. An import during the build
        /// may have created .meta files for the file and the folders; those go too.
        /// </summary>
        internal static void Restore(string path, State state)
        {
            if (state.existed)
                File.WriteAllBytes(path, state.bytes);
            else if (File.Exists(path))
                File.Delete(path);
            if (!state.metaExisted && File.Exists(path + ".meta"))
                File.Delete(path + ".meta");
            foreach (var directory in state.createdDirectories)
            {
                if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
                    Directory.Delete(directory);
                if (!Directory.Exists(directory) && File.Exists(directory + ".meta"))
                    File.Delete(directory + ".meta");
            }
        }

        /// <summary>Null for an Android manifest, otherwise what is wrong with it.</summary>
        internal static string ManifestError(string xml)
        {
            try
            {
                var document = new XmlDocument();
                document.LoadXml(xml);
                return document.DocumentElement?.Name == "manifest"
                    ? null
                    : "is not an Android manifest: its root element is not <manifest>.";
            }
            catch (XmlException e)
            {
                return $"is not valid XML: {e.Message}";
            }
        }

        /// <summary>Custom Main Manifest in the Player Settings the build reads.</summary>
        static bool UseCustomMainManifest
        {
            get => Property().boolValue;
            set
            {
                var property = Property();
                if (property.boolValue == value)
                    return;
                property.boolValue = value;
                property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        internal static SerializedProperty Property()
        {
            // The object the static PlayerSettings API writes: the active Build
            // Profile's own Player Settings, which falls back to the project's.
            var target = BuildProfile.GetActiveBuildProfile()?.GetComponent<PlayerSettings>()
                ?? Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            return new SerializedObject(target).FindProperty("useCustomMainManifest")
                ?? throw new InvalidOperationException($"{LogPrefix} PlayerSettings has no useCustomMainManifest property.");
        }

        public IReadOnlyList<string> Validate(ForgeProfile forgeProfile)
        {
            var manifest = Config(forgeProfile).mainManifest;
            if (manifest == null)
                return Array.Empty<string>();
            var warnings = new List<string>();
            var path = AssetDatabase.GetAssetPath(manifest);
            if (path == MainManifestPath)
                warnings.Add($"Android Manifest names {MainManifestPath}, the project's own main manifest. Pick a file of this profile's.");
            var error = ManifestError(manifest.text);
            if (error != null)
                warnings.Add($"Android Manifest: {path} {error}");
            return warnings;
        }

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI)
        {
            var manifest = Config(forgeProfile).mainManifest;
            return manifest == null ? "the project's main manifest" : $"main manifest {AssetDatabase.GetAssetPath(manifest)}";
        }

        public void ContributeToManifest(BuildManifest manifest, ForgeBuildContext context)
        {
            var mainManifest = Config(context.ForgeProfile).mainManifest;
            if (mainManifest == null)
                return;
            var section = new ManifestSection("AndroidManifest");
            section.Add("mainManifest", AssetDatabase.GetAssetPath(mainManifest));
            manifest.AddSection(section);
        }

        public void OnDrawProfileGUI(ForgeProfile forgeProfile)
        {
            var config = Config(forgeProfile);
            var manifest = (TextAsset)EditorGUILayout.ObjectField(new GUIContent("Main Manifest",
                    "This profile's AndroidManifest.xml. Builds put it at " + MainManifestPath + " with Custom Main Manifest on, " +
                    "and restore the project's file and setting afterwards. None builds with the project's."),
                config.mainManifest, typeof(TextAsset), false);
            if (manifest != config.mainManifest)
            {
                config.mainManifest = manifest;
                forgeProfile.SetPluginConfig(ConfigKey, config);
            }
        }
    }
}
