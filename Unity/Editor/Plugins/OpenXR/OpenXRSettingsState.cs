using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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

        /// <summary>
        /// Top-level properties that are structure, not settings, and hidden values
        /// OpenXR derives from other settings and recomputes itself: the custom
        /// loader from the enabled features, the old multiview flag from its mode,
        /// and that mode's migration marker. Kept out of snapshots, which would
        /// otherwise report drift whenever OpenXR recomputes them.
        /// </summary>
        public static readonly HashSet<string> SkipSettingsProperties = new()
        {
            "m_ObjectHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier", "m_Enabled", "m_EditorHideFlags",
            "m_Features",
            "customLoaderName", "m_optimizeMultiviewRenderRegions", "m_hasMigratedMultiviewRenderRegionSetting",
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

        public static bool IsInteractionProfile(OpenXRFeature feature) => feature is OpenXRInteractionFeature;

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

        /// <summary>
        /// Null when the group has no OpenXR settings. Throws when Restore couldn't
        /// write a feature's on/off state back (see <see cref="RequireEnabledField"/>),
        /// before anything is written with the snapshot.
        /// </summary>
        public static OpenXREditorStateSnapshot Capture(BuildTargetGroup group, bool includeInteractionProfiles)
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
                return null;

            var snapshot = new OpenXREditorStateSnapshot();

            // Hidden settings too: a profile can override one, and Unity's Meta Quest build
            // step writes the multiview render regions mode, which the inspector hides.
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
                } while (iterator.Next(false));
            }

            foreach (var feature in GetManagedFeatures(settings, includeInteractionProfiles))
            {
                RequireEnabledField(feature);
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

            using (new RefusalPlatform(group))
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
        /// Turns on each managed feature of <paramref name="target"/> whose type
        /// is on in <paramref name="source"/>, interaction profiles included. With
        /// <paramref name="match"/>, also turns off each one that is off there, so
        /// the target matches the source; without, it turns nothing off, and the
        /// target keeps the features it has on itself, such as hand tracking for a
        /// tool that runs in Play Mode. A feature only one platform has keeps its
        /// state. Returns the state it gave each feature it wrote, which OpenXR can
        /// refuse (see <see cref="SetEnabled"/>).
        /// </summary>
        public static Dictionary<OpenXRFeature, bool> CopyFeatureStates(BuildTargetGroup source, BuildTargetGroup target, bool match)
        {
            var written = new Dictionary<OpenXRFeature, bool>();
            var from = OpenXRSettings.GetSettingsForBuildTargetGroup(source);
            var to = OpenXRSettings.GetSettingsForBuildTargetGroup(target);
            if (from == null || to == null)
                return written;

            var sourceStates = new Dictionary<string, bool>();
            foreach (var feature in GetManagedFeatures(from, true))
            {
                var typeName = feature.GetType().FullName;
                if (!sourceStates.ContainsKey(typeName))
                    sourceStates.Add(typeName, feature.enabled);
            }
            using (new RefusalPlatform(target))
                foreach (var feature in GetManagedFeatures(to, true))
                    if (sourceStates.TryGetValue(feature.GetType().FullName, out var enabled) && (enabled || match))
                    {
                        SetEnabled(feature, enabled);
                        written[feature] = enabled;
                    }
            Flush(to);
            return written;
        }

        /// <summary>
        /// OpenXRFeature.enabled refuses to turn off a feature that a ticked group requires,
        /// for the platform in OpenXRFeatureSetManager.activeBuildTarget, which only the
        /// XR Plug-in Management page sets while it draws. While Build Forge writes a
        /// platform's features, that is the platform being written; the value is put back after.
        /// </summary>
        sealed class RefusalPlatform : IDisposable
        {
            readonly BuildTargetGroup previous = OpenXRFeatureSetManager.activeBuildTarget;

            public RefusalPlatform(BuildTargetGroup group) => OpenXRFeatureSetManager.activeBuildTarget = group;

            public void Dispose() => OpenXRFeatureSetManager.activeBuildTarget = previous;
        }

        // ---------------------------------------------------------------- restore

        /// <summary>
        /// Writes a captured snapshot back for the group and saves. It checks before
        /// writing anything and throws, leaving the settings as they are, when the
        /// snapshot or a feature's recorded JSON can't be read, the group has no OpenXR
        /// settings, or a recorded value can't be written; so a restore that fails
        /// writes nothing, and a stopped build leaves the editor as it was. A setting
        /// or feature this OpenXR no longer has, as after a package was removed or
        /// updated, is skipped with a warning: there is nothing to restore it to, and
        /// nothing to build with it.
        /// A feature's on/off state is written as data, like the rest of its snapshot,
        /// not through OpenXRFeature.enabled: that setter runs the feature's callback,
        /// which can write other features and other platforms' settings, and can be
        /// refused by a feature group, so restores keep to their own snapshot.
        /// </summary>
        public static void Restore(BuildTargetGroup group, OpenXREditorStateSnapshot snapshot)
        {
            if (snapshot == null)
                throw new InvalidOperationException($"The recorded OpenXR settings for {group} can't be read.");
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
                throw new InvalidOperationException($"There are no OpenXR settings for {group} to restore.");

            var gone = new List<string>();
            var check = new SerializedObject(settings);
            foreach (var entry in snapshot.settings)
            {
                var prop = check.FindProperty(entry.propertyName);
                if (prop == null) { gone.Add($"setting {entry.propertyName}"); continue; }
                if (!SetPropertyFromString(prop, entry.value))
                    throw new InvalidOperationException(
                        $"OpenXR's {entry.propertyName} for {group} can't take its recorded value '{entry.value}'.");
            }

            var byType = new Dictionary<string, OpenXRFeature>();
            foreach (var feature in settings.GetFeatures<OpenXRFeature>())
            {
                if (feature == null) continue;
                byType[feature.GetType().FullName] = feature;
            }
            var features = new List<(OpenXRFeature feature, OpenXRFeatureState state)>();
            foreach (var state in snapshot.features)
            {
                if (!byType.TryGetValue(state.featureTypeName, out var feature)) { gone.Add($"feature {state.featureTypeName}"); continue; }
                // A snapshot recorded before an OpenXR update may meet a feature without the field.
                RequireEnabledField(feature);
                if (!string.IsNullOrEmpty(state.json))
                {
                    try { JsonUtility.FromJson<JsonObjectProbe>(state.json); }
                    catch (ArgumentException e)
                    {
                        throw new InvalidOperationException(
                            $"The recorded settings of OpenXR feature {feature.name} for {group} can't be read: {e.Message}", e);
                    }
                }
                features.Add((feature, state));
            }

            var so = new SerializedObject(settings);
            foreach (var entry in snapshot.settings)
            {
                var prop = so.FindProperty(entry.propertyName);
                if (prop != null)
                    SetPropertyFromString(prop, entry.value);
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (var (feature, state) in features)
            {
                OverwriteFeaturePreservingIdentity(feature, state.json);
                var featureObject = new SerializedObject(feature);
                featureObject.FindProperty(EnabledField).boolValue = state.enabled;
                featureObject.ApplyModifiedPropertiesWithoutUndo();
            }

            Flush(settings);

            if (gone.Count > 0)
                Debug.LogWarning($"{LogPrefix} Restored the OpenXR settings for {group} except what this OpenXR no longer " +
                                 $"has: {string.Join(", ", gone)}.");
        }

        /// <summary>OpenXRFeature's serialized on/off field, which restores write as data (see <see cref="Restore"/>).</summary>
        internal const string EnabledField = "m_enabled";

        /// <summary>
        /// Throws when the feature has no <see cref="EnabledField"/>, which Restore writes
        /// by name. A private field, renamed once before, so a later OpenXR may not have it.
        /// </summary>
        static void RequireEnabledField(OpenXRFeature feature)
        {
            if (new SerializedObject(feature).FindProperty(EnabledField) == null)
                throw new InvalidOperationException(
                    $"Build Forge can't restore whether OpenXR feature {feature.name} is on: OpenXRFeature has no " +
                    $"'{EnabledField}' field in this OpenXR version. Update Build Forge for it.");
        }

        /// <summary>No fields: parsing into it only checks that a recorded feature JSON is a JSON object, without touching the feature.</summary>
        [Serializable]
        sealed class JsonObjectProbe { }

        // ------------------------------------------------------------------ merge

        /// <summary>
        /// The snapshot that undoes an Apply but keeps what changed after it (see
        /// IForgeEditorApplicable.MergeEditorState). Each setting, and each
        /// feature's enabled state and properties, takes the baseline's value
        /// while the current one is still the one the Apply left, and keeps the
        /// current one otherwise. A feature's properties count as one value, compared
        /// without the on/off state its JSON also holds, which counts on its own. What
        /// the current snapshot doesn't hold, such as interaction profiles left out
        /// of it, takes the baseline's value. Null when a snapshot can't be read.
        /// </summary>
        public static string Merge(string baselineJson, string expectedJson, string currentJson)
        {
            var baseline = OpenXREditorStateSnapshot.FromJson(baselineJson);
            var expected = OpenXREditorStateSnapshot.FromJson(expectedJson);
            var current = OpenXREditorStateSnapshot.FromJson(currentJson);
            if (baseline == null || expected == null || current == null)
                return null;

            var merged = new OpenXREditorStateSnapshot { version = current.version };

            var baselineSettings = ByKey(baseline.settings, s => s.propertyName);
            var expectedSettings = ByKey(expected.settings, s => s.propertyName);
            var currentSettings = ByKey(current.settings, s => s.propertyName);
            foreach (var setting in current.settings)
            {
                var asApplied = expectedSettings.TryGetValue(setting.propertyName, out var applied) && applied.value == setting.value;
                merged.settings.Add(asApplied && baselineSettings.TryGetValue(setting.propertyName, out var before) ? before : setting);
            }
            foreach (var setting in baseline.settings)
                if (!currentSettings.ContainsKey(setting.propertyName))
                    merged.settings.Add(setting);

            var baselineFeatures = ByKey(baseline.features, f => f.featureTypeName);
            var expectedFeatures = ByKey(expected.features, f => f.featureTypeName);
            var currentFeatures = ByKey(current.features, f => f.featureTypeName);
            foreach (var feature in current.features)
            {
                if (!expectedFeatures.TryGetValue(feature.featureTypeName, out var applied)
                    || !baselineFeatures.TryGetValue(feature.featureTypeName, out var before))
                {
                    merged.features.Add(feature);
                    continue;
                }
                merged.features.Add(new OpenXRFeatureState
                {
                    featureTypeName = feature.featureTypeName,
                    enabled = feature.enabled == applied.enabled ? before.enabled : feature.enabled,
                    json = WithoutEnabled(feature.json) == WithoutEnabled(applied.json) ? before.json : feature.json,
                });
            }
            foreach (var feature in baseline.features)
                if (!currentFeatures.ContainsKey(feature.featureTypeName))
                    merged.features.Add(feature);

            return merged.ToJson();
        }

        static readonly Regex EnabledValue = new Regex("\"" + EnabledField + @"""\s*:\s*(true|false)", RegexOptions.CultureInvariant);

        /// <summary>
        /// A feature's JSON without its on/off value, which the snapshot's enabled field
        /// holds and Merge compares on its own. Only the first: Unity writes a base
        /// class's fields first, so that is OpenXRFeature's own.
        /// </summary>
        internal static string WithoutEnabled(string json)
            => json == null ? null : EnabledValue.Replace(json, "\"" + EnabledField + "\":_", 1);

        static Dictionary<string, T> ByKey<T>(List<T> items, Func<T, string> key)
        {
            var result = new Dictionary<string, T>();
            foreach (var item in items)
                if (item != null && key(item) != null)
                    result[key(item)] = item;
            return result;
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

        /// <summary>Writes a value into a simple property; false, leaving it as it was, for a value it can't take.</summary>
        public static bool SetPropertyFromString(SerializedProperty prop, string value)
        {
            value ??= "";
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return false;
                    prop.intValue = i;
                    return true;
                case SerializedPropertyType.Boolean:
                    if (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) { prop.boolValue = true; return true; }
                    if (value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) { prop.boolValue = false; return true; }
                    return false;
                case SerializedPropertyType.Float:
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return false;
                    prop.floatValue = f;
                    return true;
                case SerializedPropertyType.String:
                    prop.stringValue = value;
                    return true;
                case SerializedPropertyType.Enum:
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var e)) return false;
                    prop.intValue = e;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// An override or pin of a profile that doesn't match OpenXR any more, with why;
        /// <see cref="Remove"/> takes it out of the profile.
        /// </summary>
        internal sealed class StaleEntry
        {
            public string Description;
            public Action<OpenXRProfileConfig> Remove;
            /// <summary>The setting, for an entry of the settings overrides; null otherwise.</summary>
            public string SettingName;
        }

        /// <summary>
        /// The profile's overrides and pins that can't be applied to the group's settings:
        /// a setting or feature OpenXR doesn't have, as after an update renamed it, or a value
        /// it can't take. Builds and Apply stop on them rather than go out without them. Only
        /// pins Apply would write are checked: those of the features the profile turns on.
        /// </summary>
        public static List<StaleEntry> StaleEntries(OpenXRProfileConfig config, OpenXRSettings settings, bool includeInteractionProfiles)
        {
            var stale = new List<StaleEntry>();
            var so = new SerializedObject(settings);
            foreach (var ovr in config.GetAllSettingsOverrides().ToList())
            {
                var name = ovr.propertyName;
                var prop = string.IsNullOrEmpty(name) ? null : so.FindProperty(name);
                var problem = prop == null ? "OpenXR has no such setting"
                    : !IsSimpleProperty(prop.propertyType) ? "not a setting Build Forge can set"
                    : !SetPropertyFromString(prop, ovr.value) ? $"'{ovr.value}' is not a valid {prop.propertyType} value"
                    : null;
                if (problem != null)
                    stale.Add(new StaleEntry { Description = $"Setting '{name}': {problem}", Remove = c => c.SetSettingOverride(name, null), SettingName = name });
            }

            var installed = new HashSet<string>(settings.GetFeatures<OpenXRFeature>().Where(f => f != null).Select(f => f.GetType().FullName));
            foreach (var typeName in config.EnabledFeatures.Where(t => !installed.Contains(t)).ToList())
                stale.Add(new StaleEntry
                {
                    Description = $"Feature '{typeName}' ({(config.OnlyListedFeatures ? "listed" : "Enable")}): not installed for this platform",
                    Remove = c => c.EnabledFeatures.Remove(typeName),
                });
            // Only Listed Features doesn't use Disabled Features.
            if (!config.OnlyListedFeatures)
                foreach (var typeName in config.DisabledFeatures.Where(t => !installed.Contains(t)).ToList())
                    stale.Add(new StaleEntry
                    {
                        Description = $"Feature '{typeName}' (Disable): not installed for this platform",
                        Remove = c => c.DisabledFeatures.Remove(typeName),
                    });
            foreach (var typeName in config.GetAllFeatureOverrides().Select(o => o.featureTypeName).Where(t => !installed.Contains(t)).ToList())
                stale.Add(new StaleEntry
                {
                    Description = $"Settings of feature '{typeName}': not installed for this platform",
                    Remove = c => c.SetFeatureOverrides(typeName, null),
                });

            foreach (var feature in GetManagedFeatures(settings, includeInteractionProfiles))
            {
                if (Override(config, feature) != true) continue;
                var typeName = feature.GetType().FullName;
                var pins = config.GetFeatureOverrides(typeName);
                if (pins == null) continue;
                var fso = new SerializedObject(feature);
                foreach (var entry in pins.ToList())
                {
                    var problem = SerializedPropertySnapshot.Problem(fso, entry);
                    if (problem == null) continue;
                    var path = entry.path;
                    stale.Add(new StaleEntry
                    {
                        Description = $"Setting '{path}' of feature '{feature.name}': {problem}",
                        Remove = c => c.SetFeatureOverrides(typeName, c.GetFeatureOverrides(typeName)?.Where(e => e.path != path).ToList()),
                    });
                }
            }
            return stale;
        }
    }
}
