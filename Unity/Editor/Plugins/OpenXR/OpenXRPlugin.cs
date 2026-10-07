using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Runtime;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace BuildForge.Editor.OpenXR
{
    [ForgePlugin]
    internal class OpenXRPlugin : IForgePlugin, IForgeManifestContributor, IForgeEditorApplicable
    {
        const string ConfigKey = "OpenXR";
        const string GlobalConfigKey = "OpenXR";
        const string SnapshotKey = "BuildForge.OpenXR.Snapshot";

        // Cached feature copies and editors for the inspector
        static Dictionary<string, OpenXRFeature> featureCopies = new();
        static Dictionary<string, UnityEditor.Editor> featureEditors = new();
        static ForgeProfile cachedProfile;

        public string DisplayName => "OpenXR";
        public string Description => "Override OpenXR settings and features per profile.";
        public int Order => 200;

        public IReadOnlyList<string> Validate(ForgeProfile forgeProfile)
        {
            if (forgeProfile.BuildProfile == null)
                return Array.Empty<string>();

            var buildTarget = Core.BuildProfileUtility.GetBuildTarget(forgeProfile.BuildProfile);
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);

            var warnings = new List<string>();
            var config = GetConfig(forgeProfile);

            var hasActiveLoader = XRManagement.XRLoaderSelectionPlugin.EffectiveLoaders(forgeProfile, buildTargetGroup)
                .Any(l => l != null && l.GetType().Name.Contains("OpenXR"));
            var providerWarning = ProviderWarning(config, hasActiveLoader, buildTargetGroup);
            if (providerWarning != null)
                warnings.Add(providerWarning);

            var openxrSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
            if (openxrSettings != null)
            {
                var includeInteractionProfiles = GetGlobalConfig().ShowInteractionProfileOverrides;
                warnings.AddRange(OverrideWarnings(config, openxrSettings.GetFeatures<OpenXRFeature>(), includeInteractionProfiles));
                warnings.AddRange(FeatureControlledSettingWarnings(config, openxrSettings.GetFeatures<OpenXRFeature>(),
                    includeInteractionProfiles));
                warnings.AddRange(FeatureGroupWarnings(config, openxrSettings, buildTargetGroup, includeInteractionProfiles));
                warnings.AddRange(MetaQuestPlatformWarnings(config, openxrSettings));
                var pendingDisable = MetaQuestUtilityWarning(buildTargetGroup, MetaQuestProfileActive, MetaQuestUtilityInitialized());
                if (pendingDisable != null)
                    warnings.Add(pendingDisable);
            }

            return warnings;
        }

        /// <summary>
        /// Overrides without OpenXR as a provider have no effect. A profile
        /// without overrides gets no warning: a profile of a target that doesn't
        /// use OpenXR needs none.
        /// </summary>
        internal static string ProviderWarning(OpenXRProfileConfig config, bool hasOpenXRLoader, BuildTargetGroup group)
        {
            if (hasOpenXRLoader || !config.HasOverrides)
                return null;
            return $"OpenXR is not enabled as a plug-in provider in XR Plug-in Management for {group}. " +
                   "Overrides configured here will have no effect until OpenXR is enabled.";
        }

        internal static List<string> OverrideWarnings(OpenXRProfileConfig config,
            IEnumerable<OpenXRFeature> features, bool includeInteractionProfiles)
        {
            var available = features.Where(f => f != null).GroupBy(f => f.GetType().FullName)
                .ToDictionary(group => group.Key, group => group.First());
            var warnings = new List<string>();
            foreach (var typeName in config.EnabledFeatures)
                if (!available.ContainsKey(typeName))
                    warnings.Add($"OpenXR feature '{typeName}' is configured for Enable but is not installed for this target.");
            if (!includeInteractionProfiles)
                foreach (var feature in available.Values.Where(OpenXRSettingsState.IsInteractionProfile))
                    if (config.EnabledFeatures.Contains(feature.GetType().FullName)
                        || config.DisabledFeatures.Contains(feature.GetType().FullName))
                        warnings.Add($"OpenXR interaction profile '{feature.GetType().FullName}' has an override that is ignored. " +
                            "Enable Interaction Profiles in Build Forge's global OpenXR settings to manage it per profile.");
            warnings.AddRange(HiddenFeatureWarnings(config, available.Values));
            return warnings;
        }

        /// <summary>
        /// An override on a hidden feature is almost always a mistake: its package
        /// turns it on and off with its other features, and turning it off can
        /// break the package. With Only Listed Features hidden features are left
        /// alone, so a listed one is reported as having no effect.
        /// </summary>
        internal static List<string> HiddenFeatureWarnings(OpenXRProfileConfig config, IEnumerable<OpenXRFeature> features)
        {
            var warnings = new List<string>();
            foreach (var feature in features.Where(f => f != null && OpenXRSettingsState.IsHidden(f)))
            {
                var typeName = feature.GetType().FullName;
                var name = FormatFeatureName(GetFeatureUiName(feature));
                if (config.OnlyListedFeatures)
                {
                    if (config.EnabledFeatures.Contains(typeName))
                        warnings.Add($"OpenXR feature '{name}' is hidden, so Only Listed Features leaves it to its package although " +
                                     "the profile lists it. Remove it from Enabled Features in the profile asset (the inspector " +
                                     "doesn't list hidden features).");
                }
                else if (config.EnabledFeatures.Contains(typeName) || config.DisabledFeatures.Contains(typeName))
                {
                    warnings.Add($"OpenXR feature '{name}' is hidden: XR Plug-in Management doesn't show it, and its package " +
                                 "turns it on and off itself, so this profile's " +
                                 $"{(config.EnabledFeatures.Contains(typeName) ? "Enable" : "Disable")} override can break the " +
                                 "package. Set it to No Override.");
                }
            }
            return warnings;
        }

        /// <summary>
        /// A settings override on a property that a feature enabled in this
        /// profile's build also declares does not reach the player: Unity's Meta
        /// Quest build step, for one, copies MetaQuestFeature's values into
        /// OpenXRSettings during BuildPlayer. A disabled feature's build steps do
        /// not run, so its properties leave the override alone. The inspector
        /// hides such rows, so these overrides come from hand-written YAML and can
        /// only be removed there.
        /// </summary>
        internal static List<string> FeatureControlledSettingWarnings(OpenXRProfileConfig config,
            IEnumerable<OpenXRFeature> features, bool includeInteractionProfiles)
        {
            var owners = FeaturePropertyOwners(FeaturesEnabledInBuild(config, features, includeInteractionProfiles));
            var warnings = new List<string>();
            foreach (var ovr in config.GetAllSettingsOverrides())
            {
                var owner = FeatureControllingSetting(ovr.propertyName, owners);
                if (owner != null)
                    warnings.Add($"OpenXR setting override '{ovr.propertyName}' may not reach the player: the " +
                        $"'{FormatFeatureName(GetFeatureUiName(owner))}' feature also declares it and can overwrite " +
                        "it during the build. Enable the feature in this profile and set it there, and remove the " +
                        "override from the profile asset (the inspector does not show it).");
            }
            return warnings;
        }

        /// <summary>
        /// The feature that declares an OpenXRSettings property, or null.
        /// MetaQuestFeature declares "spacewarpMotionVectorTextureFormat" while
        /// OpenXRSettings has the same field as "m_spacewarpMotionVectorTextureFormat".
        /// </summary>
        static OpenXRFeature FeatureControllingSetting(string settingName, Dictionary<string, OpenXRFeature> owners)
        {
            if (owners.TryGetValue(settingName, out var owner))
                return owner;
            if (settingName.StartsWith("m_") && owners.TryGetValue(settingName.Substring(2), out owner))
                return owner;
            return null;
        }

        /// <summary>
        /// The features a build of this profile runs with: the profile's overrides
        /// decide for the features Build Forge manages (as OpenXRSettingsState.Apply
        /// does), the settings' own state for the rest. Unity runs a feature's build
        /// steps only while it is enabled.
        /// </summary>
        internal static IEnumerable<OpenXRFeature> FeaturesEnabledInBuild(OpenXRProfileConfig config,
            IEnumerable<OpenXRFeature> features, bool includeInteractionProfiles)
        {
            foreach (var feature in features)
            {
                if (feature == null) continue;
                if (OpenXRSettingsState.ManagedOverride(config, feature, includeInteractionProfiles) ?? feature.enabled)
                    yield return feature;
            }
        }

        /// <summary>
        /// With Only Listed Features, the visible features this profile's builds
        /// turn off because the profile doesn't list them, by display name: what
        /// to review after a package update adds features. Empty in the other mode.
        /// </summary>
        internal static List<string> NotListedFeatures(OpenXRProfileConfig config, IEnumerable<OpenXRFeature> features,
            bool includeInteractionProfiles)
            => config.OnlyListedFeatures
                ? features.Where(f => f != null && !OpenXRSettingsState.IsHidden(f)
                                      && OpenXRSettingsState.ManagedOverride(config, f, includeInteractionProfiles) == false)
                    .Select(f => FormatFeatureName(GetFeatureUiName(f)))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();

        /// <summary>Each visible top-level feature property name, with the first feature that declares it.</summary>
        static Dictionary<string, OpenXRFeature> FeaturePropertyOwners(IEnumerable<OpenXRFeature> features)
        {
            var owners = new Dictionary<string, OpenXRFeature>();
            foreach (var feature in features)
            {
                if (feature == null) continue;
                var featureSO = new SerializedObject(feature);
                var prop = featureSO.GetIterator();
                if (!prop.Next(true)) continue;
                do
                {
                    if (prop.depth == 0 && !OpenXRSettingsState.SkipSettingsProperties.Contains(prop.name)
                        && !owners.ContainsKey(prop.name))
                        owners.Add(prop.name, feature);
                } while (prop.NextVisible(false));
            }
            return owners;
        }

        internal static string EffectiveEnabledFeatures(IEnumerable<OpenXRFeature> features)
            => string.Join(";", features.Where(f => f != null && f.enabled)
                .Select(f => f.GetType().FullName).OrderBy(n => n, StringComparer.Ordinal));

        /// <summary>
        /// Feature groups ticked in XR Plug-in Management force-enable their
        /// required features: Unity re-applies them on every domain reload and the
        /// OpenXRFeature.enabled setter refuses to disable them, so a per-profile
        /// Disable override on such a feature cannot take effect, and the build
        /// stops on it (see OnPreBuild). Build Forge manages features individually
        /// (see ARCHITECTURE.md), so any ticked group is worth a warning; a direct
        /// conflict is spelled out.
        /// </summary>
        static List<string> FeatureGroupWarnings(OpenXRProfileConfig config, OpenXRSettings openxrSettings, BuildTargetGroup group,
            bool includeInteractionProfiles)
        {
            var result = new List<string>();
            var enabledSets = EnabledFeatureSets(group);
            if (enabledSets.Count == 0)
                return result;

            var conflicts = new List<string>();
            foreach (var feature in openxrSettings.GetFeatures<OpenXRFeature>())
            {
                if (feature == null) continue;
                if (OpenXRSettingsState.ManagedOverride(config, feature, includeInteractionProfiles) != false) continue;
                foreach (var setName in RequiringGroups(GetFeatureId(feature), enabledSets))
                    conflicts.Add($"'{FormatFeatureName(GetFeatureUiName(feature))}' (required by '{setName}')");
            }

            result.Add(FeatureGroupMessage(enabledSets.Select(s => s.name).ToList(), group, conflicts, config.OnlyListedFeatures));
            return result;
        }

        static List<OpenXRFeatureSetManager.FeatureSet> EnabledFeatureSets(BuildTargetGroup group)
        {
            try
            {
                return OpenXRFeatureSetManager.FeatureSetsForBuildTarget(group)
                    .Where(s => s != null && s.isEnabled && s.featureIds != null)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<OpenXRFeatureSetManager.FeatureSet>();
            }
        }

        /// <summary>The names of the ticked groups that require the feature, so OpenXR refuses to disable it.</summary>
        internal static List<string> RequiringGroups(string featureId, IEnumerable<OpenXRFeatureSetManager.FeatureSet> featureSets)
            => featureSets
                .Where(s => s != null && s.isEnabled && s.requiredFeatureIds != null
                    && Array.IndexOf(s.requiredFeatureIds, featureId) >= 0)
                .Select(s => s.name)
                .ToList();

        internal static string FeatureGroupMessage(IReadOnlyList<string> groupNames, BuildTargetGroup group, IReadOnlyList<string> conflicts,
            bool onlyListedFeatures = false)
        {
            var message =
                $"Feature group(s) {string.Join(", ", groupNames.Select(n => $"'{n}'"))} are enabled for {group} in " +
                "XR Plug-in Management. Unity force-enables their required features on every domain reload and refuses " +
                "Disable overrides on them. Build Forge manages features individually: untick the groups and enable " +
                "the features per profile.";
            if (conflicts.Count > 0)
                message += onlyListedFeatures
                    ? $" Builds of this profile stop on turning off {string.Join(", ", conflicts)}, which it doesn't list; " +
                      "untick the group or list the feature."
                    : $" Builds of this profile stop on its Disable override for {string.Join(", ", conflicts)}; " +
                      "untick the group or remove the override.";
            return message;
        }

        internal static string RefusedDisableMessage(string featureName, IReadOnlyList<string> groups, BuildTargetGroup group,
            bool onlyListedFeatures = false)
            => $"OpenXR kept '{featureName}' enabled although this profile " +
               (onlyListedFeatures ? "doesn't list it" : "disables it") + ": the feature group(s) " +
               $"{string.Join(", ", groups.Select(n => $"'{n}'"))}, ticked for {group} in XR Plug-in Management, " +
               "require it. Untick the group, or " + (onlyListedFeatures ? "list the feature." : "remove the Disable override.");

        static readonly string[] MetaQuestAutoEnabledInteractionProfileIds =
        {
            "com.unity.openxr.feature.input.oculustouch",
            "com.unity.openxr.feature.input.metaquestpro",
            "com.unity.openxr.feature.input.metaquestplus",
        };

        /// <summary>
        /// While a Meta Quest platform profile is active (UNITY_META_QUEST is
        /// defined), the OpenXR package's BuildProfileUtility re-enables three
        /// Android interaction profiles on every domain reload. A profile that
        /// disables one of them fights that hook in the editor. Only relevant when
        /// interaction profile overrides are shown at all.
        /// </summary>
        static List<string> MetaQuestPlatformWarnings(OpenXRProfileConfig config, OpenXRSettings openxrSettings)
        {
            var result = new List<string>();
#if UNITY_META_QUEST
            if (!GetGlobalConfig().ShowInteractionProfileOverrides)
                return result;

            var disabled = new List<string>();
            foreach (var feature in openxrSettings.GetFeatures<OpenXRFeature>())
            {
                if (feature == null) continue;
                if (OpenXRSettingsState.ManagedOverride(config, feature, true) != false) continue;
                if (Array.IndexOf(MetaQuestAutoEnabledInteractionProfileIds, GetFeatureId(feature)) >= 0)
                    disabled.Add($"'{FormatFeatureName(GetFeatureUiName(feature))}'");
            }

            if (disabled.Count > 0)
                result.Add(
                    "A Meta Quest platform profile is active: Unity re-enables the Oculus Touch, Meta Quest Touch Pro " +
                    "and Touch Plus interaction profiles for Android on every domain reload, undoing " +
                    (config.OnlyListedFeatures
                        ? $"this profile's turning off {string.Join(", ", disabled)}, which it doesn't list, in the editor. List them."
                        : $"this profile's Disable override for {string.Join(", ", disabled)} in the editor. Leave them at No Override or Enable."));
#endif
            return result;
        }

        /// <summary>True while a Meta Quest platform Build Profile is active (Unity defines UNITY_META_QUEST then).</summary>
        static bool MetaQuestProfileActive =>
#if UNITY_META_QUEST
            true;
#else
            false;
#endif

        /// <summary>
        /// OpenXR 1.18's BuildProfileUtilityOpenXR enables the Meta controller
        /// profiles and Meta Quest Support for Android while a Meta Quest Build
        /// Profile is active and records that (isMetaQuestInitialized, saved in
        /// ProjectSettings/BuildProfileUtilityOpenXR.asset). On the first editor
        /// load without one it turns them off again, on a delayed call that batch
        /// builds never reach, so the change lands in an editor session and can
        /// be saved unnoticed (seen when a project's Quest profile became a plain
        /// Android profile). Returns the flag, or null when this OpenXR version
        /// has no such behavior.
        /// </summary>
        internal static bool? MetaQuestUtilityInitialized()
        {
            var type = Type.GetType("UnityEditor.XR.OpenXR.BuildProfileUtilityOpenXR, Unity.XR.OpenXR.Editor");
            var field = type?.GetField("isMetaQuestInitialized", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || type.GetMethod("DisableMetaQuestSettings", BindingFlags.Instance | BindingFlags.NonPublic) == null)
                return null;
            var instance = type.BaseType?.GetProperty("instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            return instance != null ? (bool)field.GetValue(instance) : (bool?)null;
        }

        internal static string MetaQuestUtilityWarning(BuildTargetGroup group, bool metaQuestProfileActive, bool? utilityInitialized)
        {
            if (group != BuildTargetGroup.Android || metaQuestProfileActive || utilityInitialized != true)
                return null;
            return "OpenXR enabled the Oculus Touch, Meta Quest Touch Pro and Touch Plus interaction profiles and Meta Quest Support " +
                   "for Android for a Meta Quest Build Profile, and turns them off again the next time the editor loads without one " +
                   "active (isMetaQuestInitialized: 1 in ProjectSettings/BuildProfileUtilityOpenXR.asset). Set it to 0 there and " +
                   "commit it to keep the OpenXR features as configured.";
        }

        /// <summary>
        /// The IDs of the features BuildProfileUtilityOpenXR switches for Android,
        /// or null on OpenXR versions without it.
        /// </summary>
        internal static string[] MetaQuestUtilityFeatureIds()
            => Type.GetType("UnityEditor.XR.OpenXR.BuildProfileUtilityOpenXR, Unity.XR.OpenXR.Editor")
                ?.GetField("metaFeatureIds", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as string[];

        /// <summary>
        /// The features a build turns on so that it matches an editor session:
        /// when the editor loads with a Meta Quest Build Profile active and
        /// isMetaQuestInitialized clear, BuildProfileUtilityOpenXR enables its
        /// features for Android, on a delayed call that batch builds never reach.
        /// A CI build would otherwise depend on the committed OpenXR settings,
        /// which lose them whenever an editor moves on from a Meta Quest profile.
        /// With the flag set, OpenXR did its part and any later change is the
        /// user's, so nothing is turned on.
        /// </summary>
        internal static List<OpenXRFeature> MetaQuestSetupFeatures(BuildTargetGroup group, bool metaQuestProfileActive,
            bool? utilityInitialized, IReadOnlyCollection<string> utilityFeatureIds, IEnumerable<OpenXRFeature> features)
        {
            var result = new List<OpenXRFeature>();
            if (group != BuildTargetGroup.Android || !metaQuestProfileActive || utilityInitialized != false || utilityFeatureIds == null)
                return result;
            foreach (var feature in features)
                if (feature != null && !feature.enabled && utilityFeatureIds.Contains(GetFeatureId(feature)))
                    result.Add(feature);
            return result;
        }

        internal static string MetaQuestSetupMessage(IEnumerable<string> featureNames)
            => $"Enabled {string.Join(", ", featureNames.Select(n => $"'{n}'"))} for Android for this build, as OpenXR does " +
               "when the editor loads with a Meta Quest Build Profile active and isMetaQuestInitialized: 0 in " +
               "ProjectSettings/BuildProfileUtilityOpenXR.asset, a step batch builds skip. The settings are restored afterwards.";

        static string GetFeatureId(OpenXRFeature feature)
        {
            var so = new SerializedObject(feature);
            var idProp = so.FindProperty("featureIdInternal");
            return idProp != null ? idProp.stringValue : "";
        }

        public bool IsApplicable(BuildProfile profile)
        {
            var buildTarget = Core.BuildProfileUtility.GetBuildTarget(profile);
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);

            var openxrSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
            return openxrSettings != null;
        }

        public string NotApplicableReason(BuildProfile profile) =>
            "No OpenXR settings exist for this profile's build target.";

        public void OnPreBuild(ForgeBuildContext context)
        {
            var config = GetConfig(context.ForgeProfile);
            var includeInteractionProfiles = GetGlobalConfig().ShowInteractionProfileOverrides;

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(context.BuildTargetGroup);
            if (settings == null)
            {
                Debug.LogWarning("[Build Forge/OpenXR] No OpenXR settings found for target group.");
                return;
            }
            var metaQuestSetup = MetaQuestSetupFeatures(context.BuildTargetGroup, MetaQuestProfileActive,
                MetaQuestUtilityInitialized(), MetaQuestUtilityFeatureIds(), settings.GetFeatures<OpenXRFeature>());

            // Full snapshot, not just the overridden values: Unity's Meta Quest
            // build hook rewrites top-level settings during the build, and the
            // restore has to undo that too, as well as the Meta Quest setup,
            // which includes interaction profiles.
            var snapshot = OpenXRSettingsState.Capture(context.BuildTargetGroup,
                includeInteractionProfiles || metaQuestSetup.Any(OpenXRSettingsState.IsInteractionProfile));
            context.SetProperty(SnapshotKey, snapshot.ToJson());
            if (metaQuestSetup.Count > 0)
            {
                foreach (var feature in metaQuestSetup)
                    feature.enabled = true;
                Debug.Log("[Build Forge/OpenXR] " + MetaQuestSetupMessage(metaQuestSetup.Select(f => FormatFeatureName(GetFeatureUiName(f)))));
            }
            OpenXRSettingsState.Apply(config, context.BuildTargetGroup, includeInteractionProfiles);
            // The build window shows these too; CI builds only have the log.
            foreach (var warning in FeatureControlledSettingWarnings(config, settings.GetFeatures<OpenXRFeature>(), includeInteractionProfiles))
                Debug.LogWarning("[Build Forge/OpenXR] " + warning);
            var pendingDisable = MetaQuestUtilityWarning(context.BuildTargetGroup, MetaQuestProfileActive, MetaQuestUtilityInitialized());
            if (pendingDisable != null)
                Debug.LogWarning("[Build Forge/OpenXR] " + pendingDisable);
            foreach (var feature in OpenXRSettingsState.GetManagedFeatures(settings, includeInteractionProfiles))
            {
                var typeName = feature.GetType().FullName;
                var state = OpenXRSettingsState.Override(config, feature);
                if (state == false && feature.enabled)
                {
                    var groups = RequiringGroups(GetFeatureId(feature), EnabledFeatureSets(context.BuildTargetGroup));
                    if (groups.Count > 0)
                        throw new InvalidOperationException(RefusedDisableMessage(
                            FormatFeatureName(GetFeatureUiName(feature)), groups, context.BuildTargetGroup, config.OnlyListedFeatures));
                }
                if (state != null && feature.enabled != state.Value)
                    throw new InvalidOperationException($"OpenXR feature '{typeName}' did not accept its configured override.");
            }
            if (config.OnlyListedFeatures)
            {
                var notListed = NotListedFeatures(config, settings.GetFeatures<OpenXRFeature>(), includeInteractionProfiles);
                Debug.Log("[Build Forge/OpenXR] Only Listed Features: " + (notListed.Count > 0
                    ? $"off in this build (not listed): {string.Join(", ", notListed)}."
                    : "every visible feature is listed."));
            }
        }

        public void OnPostBuild(ForgeBuildContext context)
        {
            var snapshot = OpenXREditorStateSnapshot.FromJson(context.GetProperty<string>(SnapshotKey));
            if (snapshot == null)
                return;

            OpenXRSettingsState.Restore(context.BuildTargetGroup, snapshot);
            Debug.Log("[Build Forge/OpenXR] OpenXR settings restored.");
        }

        #region Editor application

        public string CaptureEditorState(BuildTargetGroup group)
        {
            return OpenXRSettingsState.Capture(group, GetGlobalConfig().ShowInteractionProfileOverrides)?.ToJson();
        }

        public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group)
        {
            OpenXRSettingsState.Apply(GetConfig(forgeProfile), group, GetGlobalConfig().ShowInteractionProfileOverrides);
        }

        public void RestoreEditorState(BuildTargetGroup group, string json)
        {
            OpenXRSettingsState.Restore(group, OpenXREditorStateSnapshot.FromJson(json));
        }

        #endregion

        public void ContributeToManifest(BuildManifest manifest, ForgeBuildContext context)
        {
            var config = GetConfig(context.ForgeProfile);
            if (config == null)
                return;

            var section = new ManifestSection("OpenXR");

            if (config.OnlyListedFeatures)
                section.Add("onlyListedFeatures", "true");
            if (config.EnabledFeatures.Count > 0)
                section.Add("enabledFeatures", string.Join(";", config.EnabledFeatures));
            if (config.DisabledFeatures.Count > 0)
                section.Add("disabledFeatures", string.Join(";", config.DisabledFeatures));
            // Keep the existing configured fields for manifest consumers. Record
            // the applied state separately so a declaration cannot masquerade as
            // an enabled controller profile when global overrides exclude it.
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(context.BuildTargetGroup);
            if (settings != null)
                section.Add("effectiveEnabledFeatures", EffectiveEnabledFeatures(settings.GetFeatures<OpenXRFeature>()));

            foreach (var ovr in config.GetAllSettingsOverrides())
                section.Add(ovr.propertyName, ovr.value);
            AddFeaturePins(section, config);

            manifest.AddSection(section);
        }

        /// <summary>
        /// One entry per pinned feature property, keyed "FeatureType.path".
        /// Pins apply only to features this profile enables, so only those are listed.
        /// </summary>
        internal static void AddFeaturePins(ManifestSection section, OpenXRProfileConfig config)
        {
            foreach (var pin in config.GetAllFeatureOverrides())
            {
                if (!config.EnabledFeatures.Contains(pin.featureTypeName)) continue;
                foreach (var property in pin.properties)
                    section.Add($"{pin.featureTypeName}.{property.path}", property.value);
            }
        }

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI)
        {
            var config = GetConfig(forgeProfile);
            var settingOverrides = $"{config.GetAllSettingsOverrides().Count} setting override(s)";
            if (!config.OnlyListedFeatures)
                return $"enable {config.EnabledFeatures.Count} feature(s), disable {config.DisabledFeatures.Count}, {settingOverrides}";

            // The list to review after a package update: a feature it adds is off until the profile lists it.
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(
                Core.BuildProfileUtility.GetBuildTarget(forgeProfile.BuildProfile)));
            var notListed = settings != null
                ? NotListedFeatures(config, settings.GetFeatures<OpenXRFeature>(), GetGlobalConfig().ShowInteractionProfileOverrides)
                : new List<string>();
            return $"only the {config.EnabledFeatures.Count} listed feature(s); " +
                   (notListed.Count > 0 ? $"off in this build (not listed): {string.Join(", ", notListed)}; " : "") +
                   settingOverrides;
        }

        public void OnDrawProfileGUI(ForgeProfile forgeProfile)
        {
            var config = GetConfig(forgeProfile);
            bool changed = false;

            var buildTargetGroup = forgeProfile.BuildProfile != null
                ? BuildPipeline.GetBuildTargetGroup(
                    Core.BuildProfileUtility.GetBuildTarget(forgeProfile.BuildProfile))
                : BuildTargetGroup.Unknown;

            var openxrSettings = buildTargetGroup != BuildTargetGroup.Unknown
                ? OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup)
                : null;

            // The same warnings the build window shows: missing loader, ticked
            // feature groups, Meta Quest platform interaction-profile conflicts.
            if (forgeProfile.BuildProfile != null)
            {
                foreach (var warning in Validate(forgeProfile))
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            // Top-level settings overrides
            if (openxrSettings != null)
            {
                var featurePropertyNames = CollectFeaturePropertyNames(openxrSettings, config,
                    GetGlobalConfig().ShowInteractionProfileOverrides);
                EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
                DrawSettingsOverrides(openxrSettings, config, ref changed, featurePropertyNames);
                EditorGUILayout.Space(4);
            }

            // Feature overrides
            EditorGUILayout.LabelField("Feature Overrides", EditorStyles.boldLabel);

            if (openxrSettings != null)
            {
                var allFeatures = openxrSettings.GetFeatures<OpenXRFeature>().Where(f => f != null).ToList();
                var includeInteractionProfiles = GetGlobalConfig().ShowInteractionProfileOverrides;

                var onlyListed = EditorGUILayout.Toggle(new GUIContent("Only Listed Features",
                        "Tick the features this profile's builds have: every other visible feature is turned off, in builds and " +
                        "Apply, so a feature a package update adds stays off until you tick it. Hidden features are left to " +
                        "their packages. Switching keeps what builds get now."),
                    config.OnlyListedFeatures);
                if (onlyListed != config.OnlyListedFeatures)
                {
                    SetOnlyListedFeatures(config, OpenXRSettingsState.GetManagedFeatures(openxrSettings, includeInteractionProfiles).ToList(),
                        onlyListed);
                    changed = true;
                }

                var features = allFeatures.Where(f => !OpenXRSettingsState.IsInteractionProfile(f))
                    .OrderBy(f => GetFeatureUiName(f)).ToList();

                var interactionProfiles = allFeatures.Where(f => OpenXRSettingsState.IsInteractionProfile(f))
                    .OrderBy(f => GetFeatureUiName(f)).ToList();

                // The state indicator and row margins consume 22 points of
                // Unity's prefix area; keep dropdowns on the normal field column.
                var featureLabelWidth = Mathf.Max(0, EditorGUIUtility.labelWidth - 22);

                // Clear cached editors if the profile changed
                if (cachedProfile != forgeProfile)
                {
                    ClearCachedEditors();
                    cachedProfile = forgeProfile;
                }

                if (features.Count > 0)
                {
                    EditorGUILayout.LabelField("Features", EditorStyles.miniLabel);
                    if (config.OnlyListedFeatures)
                    {
                        DrawListedFeatures(features.Where(f => !OpenXRSettingsState.IsHidden(f)).ToList(), config, forgeProfile,
                            true, ref changed);
                        DrawHiddenFeatures(features.Where(OpenXRSettingsState.IsHidden).ToList());
                    }
                    else
                    {
                        DrawFeatureOverrides(features, config, forgeProfile, featureLabelWidth, ref changed);
                    }
                }

                if (interactionProfiles.Count > 0 && includeInteractionProfiles)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Interaction Profiles", EditorStyles.miniLabel);
                    if (config.OnlyListedFeatures)
                        DrawListedFeatures(interactionProfiles, config, forgeProfile, false, ref changed);
                    else
                        DrawFeatureToggles(interactionProfiles, config, featureLabelWidth, ref changed);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Assign a Unity Build Profile to configure OpenXR features for its target platform.",
                    MessageType.Info);
            }

            if (changed)
                SaveConfig(forgeProfile, config);
        }

        public void OnDrawSettingsGUI()
        {
            var globalConfig = GetGlobalConfig();
            var newShow = EditorGUILayout.Toggle(
                new GUIContent("Interaction Profiles",
                    "Show per-profile interaction profile overrides. Most projects should " +
                    "manage interaction profiles globally in XR Plug-in Management instead."),
                globalConfig.ShowInteractionProfileOverrides);
            if (newShow != globalConfig.ShowInteractionProfileOverrides)
            {
                globalConfig.ShowInteractionProfileOverrides = newShow;
                SaveGlobalConfig(globalConfig);
            }
        }

        #region Settings Overrides

        // Rows hidden because a feature this profile's build runs with controls the value.
        static HashSet<string> CollectFeaturePropertyNames(OpenXRSettings openxrSettings, OpenXRProfileConfig config,
            bool includeInteractionProfiles)
            => new HashSet<string>(FeaturePropertyOwners(
                FeaturesEnabledInBuild(config, openxrSettings.GetFeatures<OpenXRFeature>(), includeInteractionProfiles)).Keys);

        static void DrawSettingsOverrides(OpenXRSettings openxrSettings,
            OpenXRProfileConfig config, ref bool changed, HashSet<string> featurePropertyNames)
        {
            var so = new SerializedObject(openxrSettings);
            var iterator = so.GetIterator();

            if (!iterator.Next(true)) return;

            do
            {
                if (iterator.depth != 0) continue;
                if (OpenXRSettingsState.SkipSettingsProperties.Contains(iterator.name)) continue;
                // MetaQuestFeature declares "spacewarpMotionVectorTextureFormat" while
                // OpenXRSettings has the same field as "m_spacewarpMotionVectorTextureFormat"
                var nameWithoutPrefix = iterator.name.StartsWith("m_")
                    ? iterator.name.Substring(2)
                    : iterator.name;
                if (featurePropertyNames.Contains(iterator.name) ||
                    featurePropertyNames.Contains(nameWithoutPrefix)) continue;

                if (iterator.propertyType == SerializedPropertyType.Generic ||
                    iterator.propertyType == SerializedPropertyType.ManagedReference)
                    continue;

                var propName = iterator.name;
                var currentOverride = config.GetSettingOverride(propName);
                var hasOverride = currentOverride != null;

                var displayName = iterator.displayName;

                EditorGUILayout.BeginHorizontal();

                if (iterator.propertyType == SerializedPropertyType.Enum)
                {
                    var enumNames = iterator.enumDisplayNames;
                    var enumIdx = iterator.enumValueIndex;
                    var currentName = enumIdx >= 0 && enumIdx < enumNames.Length
                        ? enumNames[enumIdx] : iterator.intValue.ToString();
                    var defaultLabel = $"No Override ({currentName})";

                    var enumProp = so.FindProperty(propName);
                    var savedIdx = enumProp.enumValueIndex;
                    var intValues = new int[enumNames.Length];
                    for (int j = 0; j < enumNames.Length; j++)
                    {
                        enumProp.enumValueIndex = j;
                        intValues[j] = enumProp.intValue;
                    }
                    enumProp.enumValueIndex = savedIdx;

                    var options = new string[enumNames.Length + 1];
                    options[0] = defaultLabel;
                    Array.Copy(enumNames, 0, options, 1, enumNames.Length);

                    var selectedIndex = 0;
                    if (hasOverride && int.TryParse(currentOverride, out var storedInt))
                    {
                        for (int j = 0; j < intValues.Length; j++)
                        {
                            if (intValues[j] == storedInt) { selectedIndex = j + 1; break; }
                        }
                    }

                    var newIndex = EditorGUILayout.Popup(displayName, selectedIndex, options);
                    if (newIndex != selectedIndex)
                    {
                        config.SetSettingOverride(propName, newIndex == 0 ? null : intValues[newIndex - 1].ToString());
                        changed = true;
                    }
                }
                else if (iterator.propertyType == SerializedPropertyType.Boolean)
                {
                    var defaultStr = iterator.boolValue ? "on" : "off";
                    var options = new[] { $"No Override ({defaultStr})", "On", "Off" };

                    var selectedIndex = 0;
                    if (hasOverride)
                        selectedIndex = currentOverride == "1" ? 1 : 2;

                    var newIndex = EditorGUILayout.Popup(displayName, selectedIndex, options);
                    if (newIndex != selectedIndex)
                    {
                        config.SetSettingOverride(propName, newIndex switch
                        {
                            1 => "1",
                            2 => "0",
                            _ => null
                        });
                        changed = true;
                    }
                }
                else if (iterator.propertyType == SerializedPropertyType.Integer)
                {
                    EditorGUILayout.PrefixLabel(displayName);
                    if (!hasOverride)
                    {
                        if (GUILayout.Button($"No Override ({OpenXRSettingsState.GetPropertyValueAsString(iterator)})", EditorStyles.popup))
                        {
                            config.SetSettingOverride(propName, OpenXRSettingsState.GetPropertyValueAsString(iterator));
                            changed = true;
                        }
                    }
                    else
                    {
                        int.TryParse(currentOverride, out var intVal);
                        var newVal = EditorGUILayout.IntField(intVal);
                        if (newVal.ToString() != currentOverride)
                        {
                            config.SetSettingOverride(propName, newVal.ToString());
                            changed = true;
                        }
                        if (GUILayout.Button("X", GUILayout.Width(20)))
                        {
                            config.SetSettingOverride(propName, null);
                            changed = true;
                        }
                    }
                }
                else if (iterator.propertyType == SerializedPropertyType.Float)
                {
                    EditorGUILayout.PrefixLabel(displayName);
                    if (!hasOverride)
                    {
                        if (GUILayout.Button($"No Override ({OpenXRSettingsState.GetPropertyValueAsString(iterator)})", EditorStyles.popup))
                        {
                            config.SetSettingOverride(propName, OpenXRSettingsState.GetPropertyValueAsString(iterator));
                            changed = true;
                        }
                    }
                    else
                    {
                        float.TryParse(currentOverride, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatVal);
                        var newVal = EditorGUILayout.FloatField(floatVal);
                        if (newVal.ToString("R", CultureInfo.InvariantCulture) != currentOverride)
                        {
                            config.SetSettingOverride(propName, newVal.ToString("R", CultureInfo.InvariantCulture));
                            changed = true;
                        }
                        if (GUILayout.Button("X", GUILayout.Width(20)))
                        {
                            config.SetSettingOverride(propName, null);
                            changed = true;
                        }
                    }
                }

                EditorGUILayout.EndHorizontal();
            } while (iterator.NextVisible(false));
        }

        #endregion

        #region Feature Overrides

        static void DrawFeatureOverrides(List<OpenXRFeature> features,
            OpenXRProfileConfig config, ForgeProfile forgeProfile, float labelWidth, ref bool changed)
        {
            foreach (var feature in features)
            {
                var typeName = feature.GetType().FullName;
                var isEnabled = config.EnabledFeatures.Contains(typeName);
                var isDisabled = config.DisabledFeatures.Contains(typeName);

                var currentState = isEnabled ? 1 : isDisabled ? 2 : 0;
                var defaultOn = feature.enabled;

                var displayName = FormatFeatureName(GetFeatureUiName(feature));

                EditorGUILayout.BeginHorizontal();

                using (new EditorGUI.DisabledScope(true))
                {
                    var effectiveState = currentState switch
                    {
                        1 => true,
                        2 => false,
                        _ => defaultOn
                    };
                    EditorGUILayout.Toggle(effectiveState, GUILayout.Width(16));
                }

                GUILayout.Label(
                    new GUIContent(displayName, feature.name), EditorStyles.wordWrappedLabel,
                    GUILayout.MinWidth(0), GUILayout.MaxWidth(labelWidth));

                var newState = EditorGUILayout.Popup(currentState,
                    new[] { "No Override", "Enable", "Disable" }, GUILayout.Width(110));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                if (newState != currentState)
                {
                    config.EnabledFeatures.Remove(typeName);
                    config.DisabledFeatures.Remove(typeName);
                    switch (newState)
                    {
                        case 1: config.EnabledFeatures.Add(typeName); break;
                        case 2: config.DisabledFeatures.Add(typeName); break;
                    }

                    // Clear snapshot and cached editor when not explicitly enabled
                    if (newState != 1)
                        ClearFeaturePins(config, typeName);

                    changed = true;
                }

                // Show native feature editor only for explicitly enabled features
                if (currentState == 1)
                {
                    DrawNativeFeatureEditor(feature, typeName, config, forgeProfile);
                }
            }
        }

        /// <summary>Drops a feature's pins and its cached editor copy, for a feature the profile no longer enables.</summary>
        static void ClearFeaturePins(OpenXRProfileConfig config, string typeName)
        {
            config.SetFeatureOverrides(typeName, null);
            if (featureCopies.TryGetValue(typeName, out var oldCopy))
            {
                if (featureEditors.TryGetValue(typeName, out var oldEditor))
                {
                    UnityEngine.Object.DestroyImmediate(oldEditor);
                    featureEditors.Remove(typeName);
                }
                UnityEngine.Object.DestroyImmediate(oldCopy);
                featureCopies.Remove(typeName);
            }
        }

        /// <summary>
        /// Switches Only Listed Features without changing what builds get: turned
        /// on, the visible features on in this profile's builds now are listed and
        /// Disabled Features is cleared; hidden features leave the lists, being
        /// left to their packages from now on. Turned off, every visible feature
        /// the profile doesn't list gets a Disable override.
        /// </summary>
        internal static void SetOnlyListedFeatures(OpenXRProfileConfig config, IReadOnlyList<OpenXRFeature> managedFeatures, bool on)
        {
            if (config.OnlyListedFeatures == on)
                return;
            if (on)
            {
                var listed = new List<string>();
                foreach (var feature in managedFeatures)
                {
                    var typeName = feature.GetType().FullName;
                    if (OpenXRSettingsState.IsHidden(feature))
                    {
                        config.EnabledFeatures.Remove(typeName);
                        ClearFeaturePins(config, typeName);
                    }
                    else if (OpenXRSettingsState.Override(config, feature) ?? feature.enabled)
                    {
                        listed.Add(typeName);
                    }
                }
                // Features not installed here keep their entries.
                config.EnabledFeatures.RemoveAll(n => managedFeatures.Any(f => f.GetType().FullName == n));
                config.EnabledFeatures.AddRange(listed);
                config.DisabledFeatures.Clear();
                config.OnlyListedFeatures = true;
            }
            else
            {
                foreach (var feature in managedFeatures)
                {
                    var typeName = feature.GetType().FullName;
                    if (!OpenXRSettingsState.IsHidden(feature) && !config.EnabledFeatures.Contains(typeName)
                        && !config.DisabledFeatures.Contains(typeName))
                        config.DisabledFeatures.Add(typeName);
                }
                config.OnlyListedFeatures = false;
            }
        }

        /// <summary>
        /// Only Listed Features: a checkbox per feature, ticked when the profile
        /// lists it. A listed feature shows its settings, like an Enable override.
        /// </summary>
        static void DrawListedFeatures(List<OpenXRFeature> features, OpenXRProfileConfig config, ForgeProfile forgeProfile,
            bool withSettings, ref bool changed)
        {
            foreach (var feature in features)
            {
                var typeName = feature.GetType().FullName;
                var listed = config.EnabledFeatures.Contains(typeName);

                EditorGUILayout.BeginHorizontal();
                var nowListed = EditorGUILayout.Toggle(listed, GUILayout.Width(16));
                GUILayout.Label(new GUIContent(FormatFeatureName(GetFeatureUiName(feature)), feature.name),
                    EditorStyles.wordWrappedLabel, GUILayout.MinWidth(0));
                EditorGUILayout.EndHorizontal();

                if (nowListed != listed)
                {
                    if (nowListed)
                    {
                        config.EnabledFeatures.Add(typeName);
                    }
                    else
                    {
                        config.EnabledFeatures.Remove(typeName);
                        ClearFeaturePins(config, typeName);
                    }
                    changed = true;
                }

                if (withSettings && listed)
                    DrawNativeFeatureEditor(feature, typeName, config, forgeProfile);
            }
        }

        const string HiddenFeaturesExpandedKey = "BuildForge.OpenXR.HiddenFeaturesExpanded";

        /// <summary>Only Listed Features: the hidden features with their current state, read-only, in a folded group.</summary>
        static void DrawHiddenFeatures(List<OpenXRFeature> hidden)
        {
            if (hidden.Count == 0)
                return;
            var expanded = SessionState.GetBool(HiddenFeaturesExpandedKey, false);
            var newExpanded = EditorGUILayout.Foldout(expanded, new GUIContent(
                $"Hidden features, left to their packages ({hidden.Count})",
                "Vendor packages register these features for their own use and turn them on and off themselves. " +
                "Builds and Apply leave them as they are; this shows their current state."), true);
            if (newExpanded != expanded)
                SessionState.SetBool(HiddenFeaturesExpandedKey, newExpanded);
            if (!newExpanded)
                return;
            using (new EditorGUI.DisabledScope(true))
            using (new EditorGUI.IndentLevelScope())
            {
                foreach (var feature in hidden)
                    EditorGUILayout.ToggleLeft(new GUIContent(FormatFeatureName(GetFeatureUiName(feature)), feature.name), feature.enabled);
            }
        }

        // Interaction profiles only get enable/disable toggles, no settings editor
        static void DrawFeatureToggles(List<OpenXRFeature> features,
            OpenXRProfileConfig config, float labelWidth, ref bool changed)
        {
            foreach (var feature in features)
            {
                var typeName = feature.GetType().FullName;
                var isEnabled = config.EnabledFeatures.Contains(typeName);
                var isDisabled = config.DisabledFeatures.Contains(typeName);

                var currentState = isEnabled ? 1 : isDisabled ? 2 : 0;
                var defaultOn = feature.enabled;

                var displayName = FormatFeatureName(GetFeatureUiName(feature));

                EditorGUILayout.BeginHorizontal();

                using (new EditorGUI.DisabledScope(true))
                {
                    var effectiveState = currentState switch
                    {
                        1 => true,
                        2 => false,
                        _ => defaultOn
                    };
                    EditorGUILayout.Toggle(effectiveState, GUILayout.Width(16));
                }

                GUILayout.Label(
                    new GUIContent(displayName, feature.name), EditorStyles.wordWrappedLabel,
                    GUILayout.MinWidth(0), GUILayout.MaxWidth(labelWidth));

                var newState = EditorGUILayout.Popup(currentState,
                    new[] { "No Override", "Enable", "Disable" }, GUILayout.Width(110));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                if (newState != currentState)
                {
                    config.EnabledFeatures.Remove(typeName);
                    config.DisabledFeatures.Remove(typeName);
                    switch (newState)
                    {
                        case 1: config.EnabledFeatures.Add(typeName); break;
                        case 2: config.DisabledFeatures.Add(typeName); break;
                    }
                    changed = true;
                }
            }
        }

        static void DrawNativeFeatureEditor(OpenXRFeature realFeature, string typeName,
            OpenXRProfileConfig config, ForgeProfile forgeProfile)
        {
            // Get or create an in-memory copy of the feature
            if (!featureCopies.TryGetValue(typeName, out var copy) || copy == null)
            {
                // The copy starts as the real feature (whatever profile is applied
                // right now) with this profile's pins on top. The first edit pins
                // every visible property at those values — full pins, not a diff,
                // so applying never depends on the editor's state at edit time.
                copy = UnityEngine.Object.Instantiate(realFeature);
                copy.name = realFeature.name; // Instantiate appends "(Clone)"
                copy.hideFlags = HideFlags.DontSave;

                var overrides = config.GetFeatureOverrides(typeName);
                if (overrides != null)
                    SerializedPropertySnapshot.Apply(new SerializedObject(copy), overrides);

                featureCopies[typeName] = copy;
            }

            // Get or create the editor for the copy
            if (!featureEditors.TryGetValue(typeName, out var editor) || editor == null)
            {
                editor = UnityEditor.Editor.CreateEditor(copy);
                featureEditors[typeName] = editor;
            }

            // Capture real OpenXR settings state before drawing — the native editor's
            // ApplySettingsOverride pushes copy values to real settings on every repaint
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(
                Core.BuildProfileUtility.GetBuildTarget(forgeProfile.BuildProfile));
            var openxrSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
            string settingsSnapshot = null;
            if (openxrSettings != null)
                settingsSnapshot = EditorJsonUtility.ToJson(openxrSettings);

            var previousIndent = EditorGUI.indentLevel;
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            try
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Start at the feature name, past its state indicator,
                    // then apply Unity's normal child indentation inside it.
                    GUILayout.Space(24);
                    using (new EditorGUILayout.VerticalScope())
                    using (var change = new EditorGUI.ChangeCheckScope())
                    {
                        EditorGUI.indentLevel++;
                        editor.OnInspectorGUI();
                        if (change.changed)
                        {
                            // Pin every visible property of the copy; identity/metadata
                            // properties are excluded by the skip list.
                            config.SetFeatureOverrides(typeName,
                                SerializedPropertySnapshot.Capture(new SerializedObject(copy), OpenXRSettingsState.SkipFeatureProperties));
                            forgeProfile.SetPluginConfig(ConfigKey, config);
                        }
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndent;
                EditorGUIUtility.labelWidth = previousLabelWidth;
                // Restore real settings to undo ApplySettingsOverride side effects.
                if (openxrSettings != null && settingsSnapshot != null)
                    EditorJsonUtility.FromJsonOverwrite(settingsSnapshot, openxrSettings);
            }
        }

        static void ClearCachedEditors()
        {
            foreach (var editor in featureEditors.Values)
            {
                if (editor != null)
                    UnityEngine.Object.DestroyImmediate(editor);
            }
            featureEditors.Clear();

            foreach (var copy in featureCopies.Values)
            {
                if (copy != null)
                    UnityEngine.Object.DestroyImmediate(copy);
            }
            featureCopies.Clear();
        }

        static string GetFeatureUiName(OpenXRFeature feature)
        {
            var so = new SerializedObject(feature);
            var uiNameProp = so.FindProperty("nameUi");
            if (uiNameProp != null && !string.IsNullOrEmpty(uiNameProp.stringValue))
                return uiNameProp.stringValue;
            return feature.name;
        }

        // Feature names often end in their platform's build target group name, such as "Android".
        static readonly HashSet<string> PlatformSuffixes = new(Enum.GetNames(typeof(BuildTargetGroup)));

        static string FormatFeatureName(string name)
        {
            var lastSpace = name.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                var lastWord = name.Substring(lastSpace + 1);
                if (PlatformSuffixes.Contains(lastWord))
                    name = name.Substring(0, lastSpace);
            }

            name = Regex.Replace(name, @"([a-z])([A-Z])", "$1 $2");
            name = Regex.Replace(name, @"([A-Z]+)([A-Z][a-z])", "$1 $2");

            return name.Trim();
        }

        #endregion

        #region Serialization Helpers

        static OpenXRProfileConfig GetConfig(ForgeProfile forgeProfile)
        {
            return forgeProfile.GetPluginConfig<OpenXRProfileConfig>(ConfigKey);
        }

        static void SaveConfig(ForgeProfile forgeProfile, OpenXRProfileConfig config)
        {
            forgeProfile.SetPluginConfig(ConfigKey, config);
        }

        static OpenXRGlobalConfig GetGlobalConfig()
        {
            return ForgeSettings.instance.GetGlobalPluginConfig<OpenXRGlobalConfig>(GlobalConfigKey);
        }

        static void SaveGlobalConfig(OpenXRGlobalConfig config)
        {
            ForgeSettings.instance.SetGlobalPluginConfig(GlobalConfigKey, config);
        }

        #endregion
    }
}
