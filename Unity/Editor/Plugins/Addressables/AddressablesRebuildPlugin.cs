using System.Collections.Generic;
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

        // Also without Addressables settings: turned on, it then stops the build (see
        // MissingSettingsMessage) instead of being skipped. Off, its switch filters it out.
        public bool IsApplicable(BuildProfile profile) => true;

        /// <summary>
        /// Turned on without Addressables settings, nothing is rebuilt, while Unity's player
        /// build still copies the content built before from Library into the player.
        /// </summary>
        internal const string MissingSettingsMessage =
            "Addressables Rebuild is on for this profile, but the Addressables settings are missing, so no content " +
            "would be rebuilt and content built before would ship. Create the settings in Window > Asset Management > " +
            "Addressables > Groups, or turn Addressables Rebuild off for this profile. Builds of this profile stop until then.";

        public IReadOnlyList<string> Validate(ForgeProfile forgeProfile, string variant)
            => AddressableAssetSettingsDefaultObject.Settings == null
                ? new[] { MissingSettingsMessage }
                : System.Array.Empty<string>();

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
                throw new System.InvalidOperationException($"[Build Forge/Addressables] {MissingSettingsMessage}");

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

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI, string variant)
        {
            return GetConfig(forgeProfile).CleanBeforeRebuild
                ? "clean and rebuild Addressables content before the build"
                : "rebuild Addressables content before the build";
        }

        public void OnDrawProfileGUI(ForgeProfile forgeProfile)
        {
            if (AddressableAssetSettingsDefaultObject.Settings == null)
            {
                // Greyed out while the switch is off; turned on, builds of the profile stop.
                EditorGUILayout.HelpBox(MissingSettingsMessage,
                    GetConfig(forgeProfile).RebuildBeforeBuild ? MessageType.Error : MessageType.Info);
                return;
            }

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
