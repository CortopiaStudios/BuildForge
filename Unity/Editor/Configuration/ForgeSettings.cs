using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace BuildForge.Editor.Configuration
{
    /// <summary>
    /// Global Build Forge settings stored as a ScriptableObject singleton in the project.
    /// </summary>
    [FilePath("ProjectSettings/ForgeSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class ForgeSettings : ScriptableSingleton<ForgeSettings>
    {
        [Tooltip("When enabled, Build Forge intercepts BuildPipeline.BuildPlayer calls to enforce builds go through Build Forge.")]
        [SerializeField] bool interceptBuilds = true;

        [Tooltip("When enabled, Build Forge writes a build manifest JSON file to StreamingAssets.")]
        [SerializeField] bool writeBuildManifest = true;

        [Tooltip("When enabled, logs the full build manifest JSON to the console after writing.")]
        [SerializeField] bool logManifestContents;

        [Tooltip("When enabled, the {ProjectName} placeholder in output paths is sanitized for safe filenames.")]
        [SerializeField] bool mangleProductName = true;

        [Tooltip("When enabled, a successful build started from the editor is shown in the file browser (Explorer, Finder, or the Linux file manager), like Unity's own Build button. Never in CI or batch mode.")]
        [SerializeField] bool revealBuildInFileBrowser = true;

        [Tooltip("When enabled, Build Forge settings are written to disk right after they are edited in the Inspector, so the Build Profile's .asset file always matches what the Inspector shows.")]
        [SerializeField] bool saveProfilesOnEdit = true;

        [Tooltip("When enabled, Build Forge keeps a BUILD_PROFILE_<NAME> scripting define in each referenced Unity Build Profile's Scripting Defines list, so code can compile conditionally per profile.")]
        [SerializeField] bool maintainBuildProfileDefines = true;

        [Tooltip("When enabled, applying a profile for another platform also gives Play Mode, which runs on Standalone's XR settings, that platform's XR loaders, Initialize XR on Startup and OpenXR feature states. Revert to Baseline puts Standalone back.")]
        [SerializeField] bool playModeFollowsAppliedProfile = true;

        [Tooltip("Project-wide variant names. Each selects BUILD_VARIANT_<NAME> and any configured rules for development mode, extra defines, product marking and plugins. Once variants exist, the default build gets BUILD_VARIANT_DEFAULT.")]
        [SerializeField] List<string> buildVariants = new();

        [Tooltip("Optional rules for a named variant or Default. Unconfigured variants keep the profile's build settings.")]
        [SerializeField] List<BuildVariantRule> variantRules = new();

        internal IReadOnlyList<BuildVariantRule> VariantRules => variantRules;

        [Tooltip("When enabled, a variant build's product name (and so its file name and app display name) is suffixed with the variant, e.g. \"Game (Internal)\", so an internal build is hard to distribute by mistake. The default build is never suffixed.")]
        [SerializeField] bool markVariantBuilds = true;

        [Tooltip("Plugins disabled globally. Uses the full type name of the plugin class.")]
        [SerializeField] List<string> disabledPlugins = new();

        [Tooltip("Plugins explicitly enabled by the user. Used for plugins that default to disabled.")]
        [SerializeField] List<string> enabledPlugins = new();

        [Tooltip("Global plugin configuration objects keyed by plugin key, stored as managed references so they read as plain YAML.")]
        [SerializeField] List<PluginConfigSlot> globalPluginConfigs = new();

        public bool InterceptBuilds
        {
            get => interceptBuilds;
            set
            {
                interceptBuilds = value;
                SaveSettings();
            }
        }

        public bool WriteBuildManifest
        {
            get => writeBuildManifest;
            set
            {
                writeBuildManifest = value;
                SaveSettings();
            }
        }

        public bool LogManifestContents
        {
            get => logManifestContents;
            set
            {
                logManifestContents = value;
                SaveSettings();
            }
        }

        /// <summary>
        /// Returns the stored global configuration for <paramref name="key"/> when
        /// one exists and is of type <typeparamref name="T"/>; otherwise a fresh
        /// <c>new T()</c> that is not stored.
        /// </summary>
        public T GetGlobalPluginConfig<T>(string key) where T : class, new()
            => PluginConfigSlot.Get<T>(globalPluginConfigs, key);

        /// <summary>
        /// Stores (or replaces) the global configuration for <paramref name="key"/>
        /// and saves; null removes it. Same ownership rules as
        /// <see cref="ForgeProfile.SetPluginConfig{T}"/>.
        /// </summary>
        public void SetGlobalPluginConfig<T>(string key, T config) where T : class
        {
            PluginConfigSlot.Set(globalPluginConfigs, key, config);
            SaveSettings();
        }

        public bool MangleProductName
        {
            get => mangleProductName;
            set
            {
                mangleProductName = value;
                SaveSettings();
            }
        }

        /// <summary>
        /// Write Build Forge settings to disk as soon as they are edited in the
        /// Inspector (default). Unity otherwise keeps the change in memory until
        /// Save Project, a reload, or quit, so the .asset file lags behind the
        /// Inspector. Disable if the extra write per edit causes trouble.
        /// </summary>
        public bool SaveProfilesOnEdit
        {
            get => saveProfilesOnEdit;
            set
            {
                saveProfilesOnEdit = value;
                SaveSettings();
            }
        }

        /// <summary>
        /// Keep a <c>BUILD_PROFILE_&lt;NAME&gt;</c> scripting define in every
        /// referenced Unity Build Profile (default). See BuildProfileDefines.
        /// </summary>
        public bool MaintainBuildProfileDefines
        {
            get => maintainBuildProfileDefines;
            set
            {
                maintainBuildProfileDefines = value;
                SaveSettings();
            }
        }

        /// <summary>
        /// Applying a profile for a platform other than Standalone also writes
        /// that platform's XR state to Standalone, which Play Mode runs on
        /// (default). See IForgePlayModeMirror.
        /// </summary>
        public bool PlayModeFollowsAppliedProfile
        {
            get => playModeFollowsAppliedProfile;
            set
            {
                playModeFollowsAppliedProfile = value;
                SaveSettings();
            }
        }

        /// <summary>
        /// Show a successful editor build in the file browser afterwards, like
        /// Unity's own Build button (default). Skipped for CI builds.
        /// </summary>
        public bool RevealBuildInFileBrowser
        {
            get => revealBuildInFileBrowser;
            set
            {
                revealBuildInFileBrowser = value;
                SaveSettings();
            }
        }

        /// <summary>Project-wide build variant names; see BuildVariants.</summary>
        public IReadOnlyList<string> BuildVariants => buildVariants;

        /// <summary>Suffix variant builds' product name with the variant ("Game (Internal)"); the default build is never suffixed. Default on.</summary>
        public bool MarkVariantBuilds
        {
            get => markVariantBuilds;
            set
            {
                markVariantBuilds = value;
                SaveSettings();
            }
        }

        public static string MangleForFilename(string name)
        {
            name = name.Replace("&", "and");
            name = Regex.Replace(name, @"[:()'""!.()]", "");
            name = Regex.Replace(name, @"[^a-zA-Z0-9_\-]", "_");
            name = Regex.Replace(name, @"[_\-]{2,}", m => m.Value[0].ToString());
            name = name.Trim('_', '-');
            return name;
        }

        public bool IsPluginDisabledWithDefault(string pluginTypeName, bool safeToDefaultEnable)
            => PluginEnableState.IsDisabled(disabledPlugins, enabledPlugins, pluginTypeName, safeToDefaultEnable);

        public void SetPluginDisabled(string pluginTypeName, bool disabled)
        {
            PluginEnableState.Set(disabledPlugins, enabledPlugins, pluginTypeName, disabled);
            SaveSettings();
        }

        /// <summary>
        /// Persists settings to disk. Public wrapper around the protected Save method.
        /// </summary>
        public void SaveSettings()
        {
            Save(true);
        }
    }

    /// <summary>
    /// Pure resolution of the disabled/enabled plugin lists, split out of the
    /// ScriptableSingleton shell so it can be unit-tested on plain lists without
    /// touching the singleton or the host project's ProjectSettings asset.
    /// </summary>
    internal static class PluginEnableState
    {
        public static bool IsDisabled(
            List<string> disabled, List<string> enabled, string pluginTypeName, bool safeToDefaultEnable)
        {
            if (disabled.Contains(pluginTypeName))
                return true;
            if (enabled.Contains(pluginTypeName))
                return false;
            return !safeToDefaultEnable;
        }

        public static void Set(
            List<string> disabled, List<string> enabled, string pluginTypeName, bool isDisabled)
        {
            disabled.Remove(pluginTypeName);
            enabled.Remove(pluginTypeName);

            if (isDisabled)
                disabled.Add(pluginTypeName);
            else
                enabled.Add(pluginTypeName);
        }
    }
}
