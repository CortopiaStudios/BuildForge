using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Samples.ExampleIntegration
{
    /// <summary>
    /// Example Build Forge plugin. While a build of one of the selected variants
    /// runs, it appends a suffix to the Android application identifier, so that
    /// build installs next to the store build instead of replacing it:
    /// com.example.mygame becomes com.example.mygame.internal. The default build
    /// is never changed.
    ///
    /// Build Forge marks a variant build's product name ("My Game (Internal)") but
    /// keeps the application identifier; this plugin adds the other half. It only
    /// acts in builds, so it does not implement IForgeEditorApplicable: Apply never
    /// changes the identifier the editor uses.
    /// </summary>
    [ForgePlugin]
    public sealed class ApplicationIdSuffixPlugin : IForgePlugin, IForgeManifestContributor
    {
        /// <summary>The key the settings are stored under, prefixed so it cannot collide with another plugin's.</summary>
        public const string ConfigKey = "ExampleIntegration.ApplicationIdSuffix";

        // The identifier before this build changed it, kept in the build context:
        // the context lives for one build, while Build Forge reuses one plugin
        // instance for every build until the next domain reload.
        const string OriginalIdentifierKey = ConfigKey + ".Original";

        const string LogPrefix = "[Application ID Suffix]";

        // An Android application identifier: at least two dot-separated segments,
        // each starting with a letter and continuing with letters, digits or
        // underscores.
        static readonly Regex AndroidIdentifier = new Regex(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$");

        // What can follow a valid identifier and keep it valid: characters that
        // extend its last segment, then any number of new segments.
        static readonly Regex ValidSuffix = new Regex(@"^[A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)*$");

        public string DisplayName => "Application ID Suffix";

        public string Description =>
            "Append a suffix to the Android application identifier for selected variants, so their builds install next to the store build.";

        // After Android Keystore Env (600) and before the Addressables plugins
        // (700, 800). The plugin depends on none of the bundled plugins, so any
        // free value between their multiples of 100 would do: the identifier only
        // has to be set before the player build, which follows every pre-build step.
        public int Order => 650;

        public bool IsApplicable(BuildProfile profile) => profile != null && IsAndroid(profile);

        public string NotApplicableReason(BuildProfile profile) => "Only applicable to Android Build Profiles.";

        // Non-null: Build Forge draws the Enabled checkbox, greys the settings while
        // it is off, and skips the build steps for profiles where it is off.
        public bool? IsEnabled(ForgeProfile forgeProfile) => Config(forgeProfile).enabled;

        public void SetEnabled(ForgeProfile forgeProfile, bool enabled)
        {
            var config = Config(forgeProfile);
            config.enabled = enabled;
            forgeProfile.SetPluginConfig(ConfigKey, config);
        }

        public IReadOnlyList<string> Validate(ForgeProfile forgeProfile) => Validate(forgeProfile, null);

        // Shown in the build window. Warnings do not stop builds; OnPreBuild throws
        // for the problems that must.
        public IReadOnlyList<string> Validate(ForgeProfile forgeProfile, string variant)
        {
            var config = Config(forgeProfile);
            var warnings = new List<string>();
            var suffixError = SuffixError(config.suffix);
            if (suffixError != null)
                warnings.Add(suffixError);
            if (config.variants.Count == 0)
                warnings.Add("Application ID Suffix is enabled, but no variant is selected, so no build gets the suffix.");
            var unknown = UnknownVariants(config);
            if (unknown.Count > 0)
                warnings.Add($"Application ID Suffix lists {string.Join(", ", unknown.Select(v => $"'{v}'"))}, which " +
                             "is not a configured variant (renamed or removed?). Update the plugin's variants.");
            return warnings;
        }

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI) => DescribeBuild(forgeProfile, isCI, null);

        // One line in the build window's "Plugins for this build" list.
        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI, string variant)
        {
            var config = Config(forgeProfile);
            if (AppliesTo(config, variant))
                return $"append '{config.suffix}' to the application identifier";
            return string.IsNullOrEmpty(variant)
                ? "application identifier unchanged (default build)"
                : $"application identifier unchanged for {variant}";
        }

        public void OnPreBuild(ForgeBuildContext context)
        {
            var config = Config(context.ForgeProfile);
            if (context.BuildTarget != BuildTarget.Android || !AppliesTo(config, context.Variant))
            {
                Debug.Log($"{LogPrefix} Application identifier unchanged for this build.");
                return;
            }

            var suffixError = SuffixError(config.suffix);
            if (suffixError != null)
                throw new InvalidOperationException($"{LogPrefix} {suffixError}");

            // During a build the profile's Unity Build Profile is active, so the
            // static PlayerSettings API reads and writes the identifier this build
            // uses: the Build Profile's own Player Settings when it has them,
            // otherwise Project Settings.
            var original = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            var suffixed = original + config.suffix;
            if (!AndroidIdentifier.IsMatch(suffixed))
                throw new InvalidOperationException($"{LogPrefix} '{suffixed}' is not a valid Android application identifier. " +
                                                    "Check the Package Name in Player Settings and the plugin's suffix.");

            // Record before changing: Build Forge also calls OnPostBuild when this
            // method throws, and OnPostBuild restores only what it finds here.
            context.SetProperty(OriginalIdentifierKey, original);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, suffixed);
            Debug.Log($"{LogPrefix} Application identifier '{suffixed}' for this build (was '{original}').");
        }

        public void OnPostBuild(ForgeBuildContext context)
        {
            var original = context.GetProperty<string>(OriginalIdentifierKey);
            if (original == null)
                return;

            // Build Forge saves the restored Player Settings after every plugin's
            // post-build step, so the project is unchanged on disk afterwards.
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, original);
            Debug.Log($"{LogPrefix} Restored application identifier '{original}'.");
        }

        // Called after every plugin's pre-build step, for the plugins that run in
        // the build. The player reads the section with
        // manifest.GetSection("ApplicationIdSuffix")?.Get("applicationIdentifier").
        public void ContributeToManifest(BuildManifest manifest, ForgeBuildContext context)
        {
            var original = context.GetProperty<string>(OriginalIdentifierKey);
            if (original == null)
                return;

            var section = new ManifestSection("ApplicationIdSuffix");
            section.Add("applicationIdentifier", PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));
            section.Add("originalApplicationIdentifier", original);
            manifest.AddSection(section);
        }

        // Drawn in the plugin's foldout in the Build Forge section. Build Forge draws
        // the Enabled checkbox above it, and saves the Build Profile after
        // SetPluginConfig.
        public void OnDrawProfileGUI(ForgeProfile forgeProfile)
        {
            var config = Config(forgeProfile);
            var changed = false;

            var suffix = EditorGUILayout.TextField(new GUIContent("Suffix",
                    "Appended to the application identifier: '.internal' turns com.example.mygame into com.example.mygame.internal."),
                config.suffix);
            if (suffix != config.suffix)
            {
                config.suffix = suffix;
                changed = true;
            }

            EditorGUILayout.LabelField("Variants", EditorStyles.boldLabel);
            var configured = ForgeSettings.instance.BuildVariants;
            if (configured.Count == 0)
                EditorGUILayout.HelpBox("No variants are configured. Add one in Project Settings > Build Forge > Build Variants.",
                    MessageType.Info);
            foreach (var variant in configured)
            {
                var selected = config.variants.Contains(variant);
                if (EditorGUILayout.Toggle(variant, selected) == selected)
                    continue;
                if (selected)
                    config.variants.Remove(variant);
                else
                    config.variants.Add(variant);
                changed = true;
            }

            // Names left behind by a renamed or removed variant.
            foreach (var stale in UnknownVariants(config))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(stale, "not a configured variant");
                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        config.variants.Remove(stale);
                        changed = true;
                    }
                }
            }

            var suffixError = SuffixError(config.suffix);
            if (suffixError != null)
                EditorGUILayout.HelpBox(suffixError, MessageType.Warning);

            // The static PlayerSettings API answers for the active Build Profile
            // only, so the preview is shown only while this profile is active.
            if (forgeProfile.BuildProfile != null && forgeProfile.BuildProfile == BuildProfile.GetActiveBuildProfile())
                EditorGUILayout.LabelField("Variant builds use",
                    PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) + config.suffix, EditorStyles.miniLabel);
            else
                EditorGUILayout.LabelField(" ", "Activate this profile to preview the identifier.", EditorStyles.miniLabel);

            if (changed)
                forgeProfile.SetPluginConfig(ConfigKey, config);
        }

        static ApplicationIdSuffixConfig Config(ForgeProfile forgeProfile) =>
            forgeProfile.GetPluginConfig<ApplicationIdSuffixConfig>(ConfigKey);

        static bool AppliesTo(ApplicationIdSuffixConfig config, string variant) =>
            !string.IsNullOrEmpty(variant) && config.variants.Contains(variant);

        static List<string> UnknownVariants(ApplicationIdSuffixConfig config)
        {
            var configured = ForgeSettings.instance.BuildVariants;
            return config.variants.Where(v => !configured.Contains(v)).ToList();
        }

        static string SuffixError(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
                return "The suffix is empty, so the application identifier would not change.";
            if (!ValidSuffix.IsMatch(suffix))
                return $"Suffix '{suffix}' would make an invalid Android application identifier. Use letters, digits and " +
                       "underscores; each new dot-separated segment must start with a letter, as in '.internal'.";
            return null;
        }

        // Unity 6000.3 has no public property for a Build Profile's platform, and
        // IsApplicable is also asked about profiles that are not active. Build
        // Forge itself reads the serialized m_BuildTarget field the same way. If the
        // field cannot be read, the plugin counts as applicable and OnPreBuild
        // checks the build's actual target.
        static bool IsAndroid(BuildProfile profile)
        {
            using (var serialized = new SerializedObject(profile))
            {
                var target = serialized.FindProperty("m_BuildTarget");
                return target == null || target.intValue == (int)BuildTarget.Android;
            }
        }
    }
}
