using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace BuildForge.Editor.OpenXR
{
    /// <summary>
    /// The single capture / apply / restore core for OpenXR settings, shared by
    /// the build path (OnPreBuild/OnPostBuild) and the editor-apply path
    /// (IForgeEditorApplicable). Everything is keyed by an explicit
    /// BuildTargetGroup and flushed to disk, never by the active profile.
    /// </summary>
    internal static class OpenXRSettingsState
    {
        const string LogPrefix = "[Build Forge/OpenXR]";

        /// <summary>Top-level properties that are structure, not settings.</summary>
        public static readonly HashSet<string> SkipSettingsProperties = new()
        {
            "m_ObjectHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier",
            "m_Features",
        };

        /// <summary>
        /// OpenXRFeature base-class metadata and Unity object plumbing — identity,
        /// never per-profile settings — excluded from feature property pins. This
        /// is the complete serialized field list of OpenXRFeature; m_enabled is
        /// handled by the Enable/Disable override instead.
        /// </summary>
        public static readonly HashSet<string> SkipFeatureProperties = new()
        {
            "m_ObjectHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier", "m_EditorHideFlags",
            "m_enabled", "nameUi", "version", "featureIdInternal", "openxrExtensionStrings",
            "company", "priority", "targetOpenXRApiVersion", "required", "customRuntimeLoaderName",
        };

        static readonly Type InteractionProfileType =
            typeof(OpenXRFeature).Assembly.GetType("UnityEngine.XR.OpenXR.Features.OpenXRInteractionFeature");

        public static bool IsInteractionProfile(OpenXRFeature feature)
            => InteractionProfileType != null && InteractionProfileType.IsAssignableFrom(feature.GetType());

        /// <summary>
        /// A feature XR Plug-in Management doesn't show: a vendor package registers
        /// it for its own use and turns it on and off itself, such as Meta's
        /// lifecycle feature, which follows the other Meta features.
        /// </summary>
        public static bool IsHidden(OpenXRFeature feature)
            => feature.GetType().GetCustomAttribute<OpenXRFeatureAttribute>()?.Hidden ?? false;

        /// <summary>
        /// The state a profile gives a feature it manages: true for Enable, false
        /// for Disable, null to leave the feature as the settings have it. With
        /// Only Listed Features a listed feature is on and every other visible
        /// feature off, whatever Disabled Features says; hidden features are left
        /// to their packages even when listed.
        /// </summary>
        public static bool? Override(OpenXRProfileConfig config, OpenXRFeature feature)
        {
            var typeName = feature.GetType().FullName;
            if (config.OnlyListedFeatures)
                return IsHidden(feature) ? (bool?)null : config.EnabledFeatures.Contains(typeName);
            if (config.EnabledFeatures.Contains(typeName))
                return true;
            if (config.DisabledFeatures.Contains(typeName))
                return false;
            return null;
        }

        /// <summary><see cref="Override"/> for a feature Build Forge manages (see <see cref="GetManagedFeatures"/>), null for one it doesn't.</summary>
        public static bool? ManagedOverride(OpenXRProfileConfig config, OpenXRFeature feature, bool includeInteractionProfiles)
            => includeInteractionProfiles || !IsInteractionProfile(feature) ? Override(config, feature) : null;

        /// <summary>
        /// The features Build Forge manages for a settings object: non-null, and
        /// excluding interaction profiles unless per-profile interaction profile
        /// overrides are enabled (see OpenXRGlobalConfig). Keeping interaction
        /// profiles out of scope by default also keeps Unity's Meta Quest
        /// auto-enable of controller profiles out of drift detection.
        /// </summary>
        public static IEnumerable<OpenXRFeature> GetManagedFeatures(OpenXRSettings settings, bool includeInteractionProfiles)
        {
            foreach (var feature in settings.GetFeatures<OpenXRFeature>())
            {
                if (feature == null)
                    continue;
                if (!includeInteractionProfiles && IsInteractionProfile(feature))
                    continue;
                yield return feature;
            }
        }

        // ---------------------------------------------------------------- capture

        /// <summary>Null when the group has no OpenXR settings.</summary>
        public static OpenXREditorStateSnapshot Capture(BuildTargetGroup group, bool includeInteractionProfiles)
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
                return null;

            var snapshot = new OpenXREditorStateSnapshot();

            var so = new SerializedObject(settings);
            var iterator = so.GetIterator();
            if (iterator.Next(true))
            {
                do
                {
                    if (iterator.depth != 0 || iterator.isArray) continue;
                    if (SkipSettingsProperties.Contains(iterator.name)) continue;
                    if (!IsSimpleProperty(iterator.propertyType)) continue;

                    snapshot.settings.Add(new SettingsOverride
                    {
                        propertyName = iterator.name,
                        value = GetPropertyValueAsString(iterator),
                    });
                } while (iterator.NextVisible(false));
            }

            foreach (var feature in GetManagedFeatures(settings, includeInteractionProfiles))
            {
                snapshot.features.Add(new OpenXRFeatureState
                {
                    featureTypeName = feature.GetType().FullName,
                    enabled = feature.enabled,
                    // EditorJsonUtility writes asset references as {fileID, guid,
                    // type}, so the JSON is stable across editor sessions as is.
                    json = EditorJsonUtility.ToJson(feature),
                });
            }

            return snapshot;
        }

        // ------------------------------------------------------------------ apply

        /// <summary>Writes a profile's overrides for the group and saves.</summary>
        public static void Apply(OpenXRProfileConfig config, BuildTargetGroup group, bool includeInteractionProfiles)
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
            {
                Debug.LogWarning($"{LogPrefix} No OpenXR settings found for {group}.");
                return;
            }

            foreach (var feature in GetManagedFeatures(settings, includeInteractionProfiles))
            {
                var state = Override(config, feature);
                if (state != null)
                    SetEnabled(feature, state.Value);
            }

            var so = new SerializedObject(settings);
            foreach (var ovr in config.GetAllSettingsOverrides())
            {
                var prop = so.FindProperty(ovr.propertyName);
                if (prop == null) continue;
                SetPropertyFromString(prop, ovr.value);
                Debug.Log($"{LogPrefix} Applied override: {ovr.propertyName} = {ovr.value}");
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            // Feature property pins only for explicitly enabled features.
            foreach (var feature in GetManagedFeatures(settings, includeInteractionProfiles))
            {
                var typeName = feature.GetType().FullName;
                if (Override(config, feature) != true) continue;
                var overrides = config.GetFeatureOverrides(typeName);
                if (overrides == null) continue;

                SerializedPropertySnapshot.Apply(new SerializedObject(feature), overrides);
                SetEnabled(feature, true);
                Debug.Log($"{LogPrefix} Applied {overrides.Count} pinned setting(s): {feature.name}");
            }

            Flush(settings);
        }

        /// <summary>
        /// Gives each managed feature of <paramref name="target"/> the enabled
        /// state of the same feature type in <paramref name="source"/>,
        /// interaction profiles included. A feature only one platform has keeps
        /// its state.
        /// </summary>
        public static void MirrorFeatureStates(BuildTargetGroup source, BuildTargetGroup target)
        {
            var from = OpenXRSettings.GetSettingsForBuildTargetGroup(source);
            var to = OpenXRSettings.GetSettingsForBuildTargetGroup(target);
            if (from == null || to == null)
                return;

            var sourceStates = new Dictionary<string, bool>();
            foreach (var feature in GetManagedFeatures(from, true))
            {
                var typeName = feature.GetType().FullName;
                if (!sourceStates.ContainsKey(typeName))
                    sourceStates.Add(typeName, feature.enabled);
            }
            foreach (var feature in GetManagedFeatures(to, true))
                if (sourceStates.TryGetValue(feature.GetType().FullName, out var enabled))
                    SetEnabled(feature, enabled);
            Flush(to);
        }

        // ---------------------------------------------------------------- restore

        /// <summary>Writes a captured snapshot back for the group and saves.</summary>
        public static void Restore(BuildTargetGroup group, OpenXREditorStateSnapshot snapshot)
        {
            if (snapshot == null)
                return;
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
                return;

            var so = new SerializedObject(settings);
            foreach (var entry in snapshot.settings)
            {
                var prop = so.FindProperty(entry.propertyName);
                if (prop == null) continue;
                SetPropertyFromString(prop, entry.value);
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            var byType = new Dictionary<string, OpenXRFeature>();
            foreach (var feature in settings.GetFeatures<OpenXRFeature>())
            {
                if (feature == null) continue;
                byType[feature.GetType().FullName] = feature;
            }

            foreach (var state in snapshot.features)
            {
                if (!byType.TryGetValue(state.featureTypeName, out var feature))
                    continue;
                OverwriteFeaturePreservingIdentity(feature, state.json);
                // The JSON already carries m_enabled; go through the setter last so
                // any feature-set bookkeeping sees the final value.
                SetEnabled(feature, state.enabled);
            }

            Flush(settings);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// Overwrites a feature from JSON while keeping the asset's own name and
        /// hideFlags: the JSON may come from an editor copy ("X(Clone)", DontSave),
        /// and DontSave would make SaveAssets skip the feature.
        /// </summary>
        public static void OverwriteFeaturePreservingIdentity(OpenXRFeature feature, string json)
        {
            var originalName = feature.name;
            var originalHideFlags = feature.hideFlags;
            EditorJsonUtility.FromJsonOverwrite(json, feature);
            feature.name = originalName;
            feature.hideFlags = originalHideFlags;
        }

        /// <summary>
        /// The enabled setter silently refuses when a feature is required by an
        /// enabled feature group, and logs an error while OpenXR is running; say
        /// so instead of leaving a mismatch to be discovered as drift.
        /// </summary>
        static void SetEnabled(OpenXRFeature feature, bool enabled)
        {
            if (feature.enabled == enabled)
                return;
            feature.enabled = enabled;
            if (feature.enabled != enabled)
                Debug.LogWarning($"{LogPrefix} {feature.name} could not be {(enabled ? "enabled" : "disabled")} " +
                    "(required by an enabled feature group, or OpenXR is running).");
            else
                Debug.Log($"{LogPrefix} {(enabled ? "Enabled" : "Disabled")} feature: {feature.name}");
        }

        static void Flush(OpenXRSettings settings)
        {
            foreach (var feature in settings.GetFeatures<OpenXRFeature>())
                if (feature != null) EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        static bool IsSimpleProperty(SerializedPropertyType type)
        {
            switch (type)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.String:
                case SerializedPropertyType.Enum:
                    return true;
                default:
                    return false;
            }
        }

        public static string GetPropertyValueAsString(SerializedProperty prop)
        {
            return prop.propertyType switch
            {
                SerializedPropertyType.Integer => prop.intValue.ToString(),
                SerializedPropertyType.Boolean => prop.boolValue ? "1" : "0",
                SerializedPropertyType.Float => prop.floatValue.ToString("R", CultureInfo.InvariantCulture),
                SerializedPropertyType.String => prop.stringValue,
                SerializedPropertyType.Enum => prop.intValue.ToString(),
                _ => ""
            };
        }

        public static void SetPropertyFromString(SerializedProperty prop, string value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (int.TryParse(value, out var i)) prop.intValue = i;
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = value == "1" || value.ToLower() == "true";
                    break;
                case SerializedPropertyType.Float:
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) prop.floatValue = f;
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value;
                    break;
                case SerializedPropertyType.Enum:
                    if (int.TryParse(value, out var e)) prop.intValue = e;
                    break;
            }
        }
    }
}
