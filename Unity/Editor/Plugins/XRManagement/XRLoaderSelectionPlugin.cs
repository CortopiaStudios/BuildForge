using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Runtime;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;

[assembly: InternalsVisibleTo("BuildForge.Editor.OpenXR")]
[assembly: InternalsVisibleTo("BuildForge.Tests.XRManagement")]

namespace BuildForge.Editor.XRManagement
{
    [Serializable]
    internal sealed class XRLoaderSelectionConfig
    {
        public bool enabled;
        public List<XRLoader> loaders = new();
    }

    [ForgePlugin]
    internal sealed class XRLoaderSelectionPlugin : IForgePlugin, IForgeEditorApplicable, IForgeManifestContributor
    {
        internal const string Key = "XRLoaders";
        const string SnapshotKey = "BuildForge.XRLoaders.Snapshot";
        [Serializable] sealed class Snapshot { public List<string> loaderGuids = new(); }

        public string DisplayName => "XR Loaders";
        public string Description => "Select the ordered XR provider list for this profile; an empty list disables XR.";
        public int Order => 100;
        static XRManagerSettings Manager(BuildTargetGroup group)
            => XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group)?.Manager;
        static XRLoaderSelectionConfig Config(ForgeProfile profile) => profile.GetPluginConfig<XRLoaderSelectionConfig>(Key);

        // A configured profile with missing settings must fail validation/apply, not silently skip the plugin.
        public bool IsApplicable(BuildProfile profile) => profile != null;
        public bool? IsEnabled(ForgeProfile profile) => Config(profile).enabled;
        public void SetEnabled(ForgeProfile profile, bool enabled)
        {
            var config = Config(profile); config.enabled = enabled; profile.SetPluginConfig(Key, config);
        }
        static BuildTargetGroup Group(BuildProfile profile)
            => BuildPipeline.GetBuildTargetGroup(BuildProfileUtility.GetBuildTarget(profile));

        internal static IReadOnlyList<XRLoader> EffectiveLoaders(ForgeProfile profile, BuildTargetGroup group)
        {
            var config = Config(profile);
            var disabled = ForgeSettings.instance.IsPluginDisabledWithDefault(typeof(XRLoaderSelectionPlugin).FullName, true);
            return config.enabled && !disabled ? config.loaders : Manager(group)?.activeLoaders ?? Array.Empty<XRLoader>();
        }

        public IReadOnlyList<string> Validate(ForgeProfile profile)
        {
            var config = Config(profile);
            if (!config.enabled) return Array.Empty<string>();
            if (Manager(Group(profile.BuildProfile)) == null) return new[] { "XR Management settings are missing for this platform." };
            if (config.loaders.Any(l => l == null)) return new[] { "An XR loader reference is missing; install its provider or repair the selection." };
            if (config.loaders.Distinct().Count() != config.loaders.Count) return new[] { "The XR loader selection contains a duplicate." };
            if (config.loaders.Any(l => string.IsNullOrEmpty(AssetDatabase.GetAssetPath(l))))
                return new[] { "All selected XR loaders must be saved assets." };
            return Array.Empty<string>();
        }

        public string CaptureEditorState(BuildTargetGroup group)
        {
            var manager = Manager(group);
            if (manager == null) return null;
            var snapshot = new Snapshot();
            foreach (var loader in manager.activeLoaders)
            {
                var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(loader));
                if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Cannot snapshot an unsaved or missing XR loader.");
                snapshot.loaderGuids.Add(guid);
            }
            return JsonUtility.ToJson(snapshot);
        }

        public void ApplyToEditor(ForgeProfile profile, BuildTargetGroup group)
        {
            // Turned off for this profile: leave the current loaders alone. Apply
            // and builds skip a disabled plugin, but resuming after a build
            // re-applies every recorded entry of the applied profile.
            var config = Config(profile);
            if (!config.enabled)
                return;
            var errors = Validate(profile);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            SetLoaders(group, config.loaders);
        }

        public void RestoreEditorState(BuildTargetGroup group, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var snapshot = JsonUtility.FromJson<Snapshot>(json);
            var loaders = snapshot.loaderGuids.Select(g => AssetDatabase.LoadAssetAtPath<XRLoader>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            if (loaders.Any(l => l == null)) throw new InvalidOperationException("An XR loader required to restore the baseline is missing.");
            SetLoaders(group, loaders);
        }

        static void SetLoaders(BuildTargetGroup group, List<XRLoader> loaders)
        {
            var manager = Manager(group);
            if (manager == null || !manager.TrySetLoaders(loaders))
                throw new InvalidOperationException($"Could not set XR loaders for {group}.");
            if (!manager.activeLoaders.SequenceEqual(loaders))
                throw new InvalidOperationException($"XR Management did not retain the requested loader order for {group}.");
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();
        }

        public void OnPreBuild(ForgeBuildContext context)
        {
            context.SetProperty(SnapshotKey, CaptureEditorState(context.BuildTargetGroup));
            ApplyToEditor(context.ForgeProfile, context.BuildTargetGroup);
        }
        public void OnPostBuild(ForgeBuildContext context)
            => RestoreEditorState(context.BuildTargetGroup, context.GetProperty<string>(SnapshotKey));
        public void ContributeToManifest(BuildManifest manifest, ForgeBuildContext context)
        {
            var section = new ManifestSection(Key);
            section.Add("loaders", string.Join(";", EffectiveLoaders(context.ForgeProfile, context.BuildTargetGroup).Select(l => l.GetType().FullName)));
            manifest.AddSection(section);
        }
        public string DescribeBuild(ForgeProfile profile, bool isCI)
            => Config(profile).loaders.Count == 0 ? "disable XR for this profile" : "use " + string.Join(", ", Config(profile).loaders.Select(l => l == null ? "(missing)" : l.name));

        public void OnDrawProfileGUI(ForgeProfile profile)
        {
            var config = Config(profile);
            EditorGUILayout.HelpBox("Only the selected loaders are enabled, in this order. An enabled empty list disables XR.", MessageType.None);
            var changed = false;
            for (var i = 0; i < config.loaders.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var loader = (XRLoader)EditorGUILayout.ObjectField($"Loader {i + 1}", config.loaders[i], typeof(XRLoader), false);
                if (loader != config.loaders[i]) { config.loaders[i] = loader; changed = true; }
                if (i > 0 && GUILayout.Button("Up", GUILayout.Width(32)))
                { (config.loaders[i - 1], config.loaders[i]) = (config.loaders[i], config.loaders[i - 1]); changed = true; }
                if (GUILayout.Button("Remove", GUILayout.Width(60))) { config.loaders.RemoveAt(i--); changed = true; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Add Loader")) { config.loaders.Add(null); changed = true; }
            if (changed) profile.SetPluginConfig(Key, config);
        }
    }
}
