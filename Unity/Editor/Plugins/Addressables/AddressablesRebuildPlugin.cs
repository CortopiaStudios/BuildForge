using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.Addressables
{
    /// <summary>
    /// Build Forge plugin that rebuilds Addressables content before the player build.
    /// Ensures the Addressables catalog is up to date — a common pain point where
    /// stale content gets shipped because someone forgot to rebuild.
    /// </summary>
    [ForgePlugin]
    internal class AddressablesRebuildPlugin : IForgePlugin
    {
        internal const string ConfigKey = "AddressablesRebuild";

        internal const string NotInitializedMessage =
            "Addressables is installed but not initialized — go to " +
            "Window > Asset Management > Addressables > Groups and create settings.";

        public string DisplayName => "Addressables Rebuild";
        public string Description => "Rebuild Addressables content before the player build.";
        public int Order => 800;

        public bool IsApplicable(BuildProfile profile)
        {
            return AddressableAssetSettingsDefaultObject.Settings != null;
        }

        public string NotApplicableReason(BuildProfile profile) => NotInitializedMessage;

        // The serialized field keeps its historical name (rebuildBeforeBuild);
        // it is this plugin's enable switch.
        public bool? IsEnabled(ForgeProfile forgeProfile) => GetConfig(forgeProfile).RebuildBeforeBuild;

        public void SetEnabled(ForgeProfile forgeProfile, bool enabled)
        {
            var config = GetConfig(forgeProfile);
            config.RebuildBeforeBuild = enabled;
            SaveConfig(forgeProfile, config);
        }

        public void OnPreBuild(ForgeBuildContext context)
        {
            var config = GetConfig(context.ForgeProfile);
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogWarning("[Build Forge/Addressables] No Addressable Asset Settings found. Skipping rebuild.");
                return;
            }

            if (config.CleanBeforeRebuild)
            {
                Debug.Log("[Build Forge/Addressables] Cleaning Addressables build cache...");
                AddressableAssetSettings.CleanPlayerContent(
                    AddressableAssetSettingsDefaultObject.Settings.ActivePlayerDataBuilder);
            }

            Debug.Log("[Build Forge/Addressables] Rebuilding Addressables content...");
            AddressableAssetSettings.BuildPlayerContent(out var result);

            if (!string.IsNullOrEmpty(result.Error))
            {
                throw new System.Exception(
                    $"[Build Forge/Addressables] Addressables build failed: {result.Error}");
            }
            else
            {
                Debug.Log($"[Build Forge/Addressables] Addressables build complete. " +
                          $"{result.LocationCount} locations in {result.OutputPath}");
            }
        }

        public void OnPostBuild(ForgeBuildContext context) { }

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI)
        {
            return GetConfig(forgeProfile).CleanBeforeRebuild
                ? "clean and rebuild Addressables content before the build"
                : "rebuild Addressables content before the build";
        }

        public void OnDrawProfileGUI(ForgeProfile forgeProfile)
        {
            // Silent: the profile inspector already shows NotApplicableReason.
            if (AddressableAssetSettingsDefaultObject.Settings == null)
                return;

            var config = GetConfig(forgeProfile);
            bool changed = false;

            var newClean = EditorGUILayout.Toggle("Clean Before Rebuild", config.CleanBeforeRebuild);
            if (newClean != config.CleanBeforeRebuild)
            {
                config.CleanBeforeRebuild = newClean;
                changed = true;
            }

            if (changed)
                SaveConfig(forgeProfile, config);
        }

        static AddressablesRebuildConfig GetConfig(ForgeProfile profile)
        {
            return profile.GetPluginConfig<AddressablesRebuildConfig>(ConfigKey);
        }

        static void SaveConfig(ForgeProfile profile, AddressablesRebuildConfig config)
        {
            profile.SetPluginConfig(ConfigKey, config);
        }
    }
}
