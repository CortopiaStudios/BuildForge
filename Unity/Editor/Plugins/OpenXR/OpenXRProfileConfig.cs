using System;
using System.Collections.Generic;
using BuildForge.Editor.Core;
using UnityEngine;

namespace BuildForge.Editor.OpenXR
{
    /// <summary>
    /// Per-profile OpenXR configuration: which features to enable/disable,
    /// top-level OpenXRSettings overrides, and per-feature property pins.
    /// Stored as a managed reference in the profile asset, so it reads as
    /// plain YAML.
    /// </summary>
    [Serializable]
    internal class OpenXRProfileConfig
    {
        [Tooltip("Only the features in Enabled Features are on in this profile's builds and Apply: every other visible " +
                 "feature is turned off, and Disabled Features is not used. Hidden features are left to their packages.")]
        [SerializeField] bool onlyListedFeatures;

        [Tooltip("OpenXR features to enable, identified by feature type name.")]
        [SerializeField] List<string> enabledFeatures = new();

        [Tooltip("OpenXR features to explicitly disable, identified by feature type name.")]
        [SerializeField] List<string> disabledFeatures = new();

        [Tooltip("OpenXR settings overrides. Key is the property name, value is the serialized value.")]
        [SerializeField] List<SettingsOverride> settingsOverrides = new();

        [Tooltip("Per-feature property pins: every visible property of a feature edited in this profile, one entry each.")]
        [SerializeField] List<FeatureOverride> featureOverrides = new();

        /// <summary>
        /// The allow-list mode: the features in <see cref="EnabledFeatures"/> are on,
        /// every other visible feature is off, and hidden features are left alone.
        /// </summary>
        public bool OnlyListedFeatures { get => onlyListedFeatures; set => onlyListedFeatures = value; }
        public List<string> EnabledFeatures => enabledFeatures;
        public List<string> DisabledFeatures => disabledFeatures;

        /// <summary>True when the profile overrides anything: a feature's state, a setting or a feature property.</summary>
        public bool HasOverrides => onlyListedFeatures || enabledFeatures.Count > 0 || disabledFeatures.Count > 0
                                    || settingsOverrides.Count > 0 || featureOverrides.Count > 0;

        /// <summary>The pinned properties for a feature, or null when the feature has none.</summary>
        public IReadOnlyList<PropertyValueEntry> GetFeatureOverrides(string featureTypeName)
        {
            var entry = featureOverrides.Find(e => e.featureTypeName == featureTypeName);
            return entry?.properties;
        }

        /// <summary>Replaces the pinned properties for a feature; null or empty removes them.</summary>
        public void SetFeatureOverrides(string featureTypeName, List<PropertyValueEntry> properties)
        {
            var entry = featureOverrides.Find(e => e.featureTypeName == featureTypeName);
            if (properties == null || properties.Count == 0)
            {
                if (entry != null)
                    featureOverrides.Remove(entry);
                return;
            }

            if (entry != null)
            {
                entry.properties = properties;
            }
            else
            {
                featureOverrides.Add(new FeatureOverride
                {
                    featureTypeName = featureTypeName,
                    properties = properties
                });
            }
        }

        /// <summary>
        /// Gets the override value for a setting, or null if not overridden.
        /// </summary>
        public string GetSettingOverride(string propertyName)
        {
            var entry = settingsOverrides.Find(e => e.propertyName == propertyName);
            return entry?.value;
        }

        /// <summary>
        /// Sets an override value for a setting. Pass null to remove the override.
        /// </summary>
        public void SetSettingOverride(string propertyName, string value)
        {
            var entry = settingsOverrides.Find(e => e.propertyName == propertyName);
            if (value == null)
            {
                if (entry != null)
                    settingsOverrides.Remove(entry);
                return;
            }

            if (entry != null)
            {
                entry.value = value;
            }
            else
            {
                settingsOverrides.Add(new SettingsOverride
                {
                    propertyName = propertyName,
                    value = value
                });
            }
        }

        public bool HasSettingOverride(string propertyName)
        {
            return settingsOverrides.Exists(e => e.propertyName == propertyName);
        }

        public IReadOnlyList<SettingsOverride> GetAllSettingsOverrides()
        {
            return settingsOverrides;
        }

        public IReadOnlyList<FeatureOverride> GetAllFeatureOverrides()
        {
            return featureOverrides;
        }
    }

    [Serializable]
    internal class SettingsOverride
    {
        public string propertyName;
        public string value;
    }

    /// <summary>All pinned properties of one feature (see SerializedPropertySnapshot).</summary>
    [Serializable]
    internal class FeatureOverride
    {
        public string featureTypeName;
        public List<PropertyValueEntry> properties = new();
    }
}
