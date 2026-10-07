using System;
using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace BuildForge.Editor.XRManagement
{
    /// <summary>
    /// Play Mode runs on Standalone's XR Plug-in Management settings. Copies the
    /// applied profile's platform's loader list and Initialize XR on Startup into
    /// Standalone, without the loaders whose provider doesn't support Standalone.
    /// </summary>
    internal sealed class PlayModeXRLoaders : IForgePlayModeMirror
    {
        const string LogPrefix = "[Build Forge/Play Mode]";

        [Serializable]
        sealed class Snapshot
        {
            public bool initManagerOnStart;
            public List<string> loaderGuids = new();
        }

        public string DisplayName => "Play Mode XR loaders";

        static XRGeneralSettings Settings(BuildTargetGroup group)
            => XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);

        public bool CanMirror(BuildTargetGroup source)
            => Settings(source)?.Manager != null && Settings(BuildTargetGroup.Standalone)?.Manager != null;

        public string CaptureEditorState(BuildTargetGroup group)
        {
            var settings = Settings(group);
            if (settings?.Manager == null)
                return null;
            var snapshot = new Snapshot { initManagerOnStart = settings.InitManagerOnStart };
            foreach (var loader in settings.Manager.activeLoaders)
            {
                var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(loader));
                if (string.IsNullOrEmpty(guid))
                    throw new InvalidOperationException("Cannot snapshot an unsaved or missing XR loader.");
                snapshot.loaderGuids.Add(guid);
            }
            return JsonUtility.ToJson(snapshot);
        }

        public void ApplyToEditor(ForgeProfile profile, BuildTargetGroup group)
            => Mirror(BuildPipeline.GetBuildTargetGroup(BuildProfileUtility.GetBuildTarget(profile.BuildProfile)), group);

        internal static void Mirror(BuildTargetGroup sourceGroup, BuildTargetGroup group)
        {
            var source = Settings(sourceGroup);
            var target = Settings(group);
            if (source?.Manager == null || target?.Manager == null)
                return;

            var loaders = new List<XRLoader>();
            foreach (var loader in source.Manager.activeLoaders)
            {
                if (Supports(loader, group))
                    loaders.Add(loader);
                else
                    Debug.Log($"{LogPrefix} {loader.name} doesn't support {group}, so Play Mode runs without it.");
            }
            Set(target, source.InitManagerOnStart, loaders);
            Debug.Log($"{LogPrefix} Play Mode uses {sourceGroup}'s XR loaders: " +
                      (loaders.Count == 0 ? "none" : string.Join(", ", loaders.Select(l => l.name))) +
                      $", Initialize XR on Startup {(source.InitManagerOnStart ? "on" : "off")}.");
        }

        public void RestoreEditorState(BuildTargetGroup group, string json)
        {
            if (string.IsNullOrEmpty(json))
                return;
            var target = Settings(group);
            if (target?.Manager == null)
                throw new InvalidOperationException($"XR Management settings for {group} are missing.");
            var snapshot = JsonUtility.FromJson<Snapshot>(json);
            var loaders = snapshot.loaderGuids
                .Select(g => AssetDatabase.LoadAssetAtPath<XRLoader>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            if (loaders.Any(l => l == null))
                throw new InvalidOperationException("An XR loader required to restore the baseline is missing.");
            Set(target, snapshot.initManagerOnStart, loaders);
        }

        static void Set(XRGeneralSettings settings, bool initManagerOnStart, List<XRLoader> loaders)
        {
            settings.InitManagerOnStart = initManagerOnStart;
            if (!settings.Manager.TrySetLoaders(loaders))
                throw new InvalidOperationException("XR Management refused the Play Mode loader list.");
            if (!settings.Manager.activeLoaders.SequenceEqual(loaders))
                throw new InvalidOperationException("XR Management did not retain the Play Mode loader order.");
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(settings.Manager);
            AssetDatabase.SaveAssets();
        }

        // XR Management's metadata names the platforms each provider's loader
        // supports. A loader without metadata, such as a project's own, is kept.
        static bool Supports(XRLoader loader, BuildTargetGroup group)
        {
            var typeName = loader.GetType().FullName;
            var metadata = XRPackageMetadataStore.GetAllPackageMetadata()
                .Where(p => p?.metadata?.loaderMetadata != null)
                .SelectMany(p => p.metadata.loaderMetadata)
                .FirstOrDefault(m => m.loaderType == typeName);
            return metadata?.supportedBuildTargets == null || metadata.supportedBuildTargets.Contains(group);
        }
    }
}
