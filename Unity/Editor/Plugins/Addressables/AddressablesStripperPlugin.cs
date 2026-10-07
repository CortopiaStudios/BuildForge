using System;
using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Runtime;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.Addressables
{
    /// <summary>
    /// Build Forge plugin that strips Addressable groups and labels from builds.
    /// Typical use case: removing spoiler content from public demos.
    ///
    /// At build time, excluded groups are disabled and entries carrying an
    /// excluded label are moved into a temporary group that is not included in
    /// the build. Removing only the label would leave the entry in its group,
    /// so it would still be packed and loadable by address or GUID. Both are
    /// restored in OnPostBuild.
    /// </summary>
    [ForgePlugin]
    internal class AddressablesStripperPlugin : IForgePlugin, IForgeManifestContributor
    {
        internal const string ConfigKey = "AddressablesStripper";
        const string CapturedGroupStatesKey = "BuildForge.Addressables.CapturedGroupStates";
        const string MovedEntriesKey = "BuildForge.Addressables.MovedEntries";
        const string TempGroupName = "BuildForge Excluded (temporary, safe to delete)";

        /// <summary>Where a label-excluded entry came from, so it can be moved back.</summary>
        class MovedEntry
        {
            public string EntryGuid;
            public string OriginalGroupGuid;
            public bool ReadOnly;
        }

        class MovedEntries
        {
            public string TempGroupGuid;
            public List<MovedEntry> Entries = new List<MovedEntry>();
        }

        public string DisplayName => "Addressables Stripper";
        public string Description => "Exclude Addressables groups and labels from the build.";
        public int Order => 700;

        public bool IsApplicable(BuildProfile profile)
        {
            return AddressableAssetSettingsDefaultObject.Settings != null;
        }

        public string NotApplicableReason(BuildProfile profile) =>
            AddressablesRebuildPlugin.NotInitializedMessage;

        /// <summary>
        /// Exclusions only take effect if Addressables content is built between
        /// OnPreBuild and OnPostBuild — the strip is transient, so bundles built
        /// beforehand always contain the excluded content. A rebuild is guaranteed
        /// by either the Addressables Rebuild plugin (Order 800, runs after this
        /// plugin) or Unity's "Build Addressables on Player Build" set to Build
        /// With Player. The per-user editor preference (PreferencesValue) is
        /// deliberately not honored: content correctness must not depend on
        /// machine-local state.
        /// </summary>
        static bool ContentRebuildGuaranteed(ForgeProfile forgeProfile)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null && settings.BuildAddressablesWithPlayerBuild ==
                AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer)
                return true;

            var rebuildDisabled = ForgeSettings.instance.IsPluginDisabledWithDefault(
                typeof(AddressablesRebuildPlugin).FullName,
                ForgePluginRegistry.IsSafeToDefaultEnable(typeof(AddressablesRebuildPlugin)));
            if (rebuildDisabled)
                return false;

            var rebuildConfig = forgeProfile.GetPluginConfig<AddressablesRebuildConfig>(AddressablesRebuildPlugin.ConfigKey);
            return rebuildConfig.RebuildBeforeBuild;
        }

        const string MisconfigurationMessage =
            "Addressables exclusions are configured, but no content rebuild will run during the build. " +
            "The strip only applies while the build is running, so previously built bundles — still containing " +
            "the excluded content — would ship. Enable the Addressables Rebuild plugin for this profile, or set " +
            "'Build Addressables on Player Build' to 'Build With Player' in the Addressables settings asset.";

        public IReadOnlyList<string> Validate(ForgeProfile forgeProfile)
            => Validate(forgeProfile, null);

        public IReadOnlyList<string> Validate(ForgeProfile forgeProfile, string variant)
        {
            var messages = new List<string>();
            var stored = GetConfig(forgeProfile);
            var unknownVariants = UnknownVariantsError(stored, ForgeSettings.instance.BuildVariants);
            if (unknownVariants != null)
                messages.Add(unknownVariants);

            var config = stored.ForVariant(variant);
            if ((config.ExcludedGroups.Count > 0 || config.ExcludedLabels.Count > 0)
                && !ContentRebuildGuaranteed(forgeProfile))
            {
                messages.Add(MisconfigurationMessage);
            }

            return messages;
        }

        /// <summary>
        /// Why the per-variant exclusions cannot be trusted, or null. Checked for
        /// every build, not only the renamed variant's: which variant an entry was
        /// meant for is unknown, and the renamed variant has no entry of its own.
        /// </summary>
        internal static string UnknownVariantsError(AddressablesStripperConfig config, IReadOnlyList<string> variants)
        {
            var unknown = config.UnknownVariantExclusions(variants);
            if (unknown.Count == 0)
                return null;
            var names = string.Join(", ", unknown.Select(v => $"'{v.variant}'"));
            var what = unknown.Count == 1 ? "is not a configured variant" : "are not configured variants";
            return $"Additional exclusions are set for {names}, which {what} (renamed or removed). Move them to a " +
                   "variant or remove them in this profile's Addressables Stripper settings. Builds of this profile " +
                   "stop until then, because a renamed variant would otherwise ship the content they exclude.";
        }

        public void OnPreBuild(ForgeBuildContext context)
        {
            // Before the early return below: the renamed variant has no exclusions.
            var stored = GetConfig(context.ForgeProfile);
            var unknownVariants = UnknownVariantsError(stored, ForgeSettings.instance.BuildVariants);
            if (unknownVariants != null)
                throw new InvalidOperationException($"[Build Forge/Addressables] {unknownVariants}");

            var config = stored.ForVariant(context.Variant);
            if (config.ExcludedGroups.Count == 0 && config.ExcludedLabels.Count == 0)
                return;

            if (!ContentRebuildGuaranteed(context.ForgeProfile))
                throw new InvalidOperationException(
                    $"[Build Forge/Addressables] {MisconfigurationMessage}");

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            // Capture and disable excluded groups
            var capturedGroupStates = new Dictionary<string, bool>();
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                if (config.ExcludedGroups.Contains(group.Name))
                {
                    capturedGroupStates[group.Name] = true;
                    // Remove all entries from the group temporarily by disabling it
                    // We do this by removing it from the build via the include flag
                    var schema = group.GetSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();
                    if (schema != null)
                    {
                        capturedGroupStates[$"{group.Name}__includeInBuild"] = schema.IncludeInBuild;
                        schema.IncludeInBuild = false;
                        Debug.Log($"[Build Forge/Addressables] Excluded group: {group.Name}");
                    }
                }
            }
            context.SetProperty(CapturedGroupStatesKey, capturedGroupStates);

            // Move entries carrying an excluded label out of the build. Entries
            // in excluded (already disabled) or read-only groups are left alone.
            if (config.ExcludedLabels.Count > 0)
            {
                var toMove = new List<AddressableAssetEntry>();
                foreach (var group in settings.groups)
                {
                    if (group == null || group.ReadOnly || config.ExcludedGroups.Contains(group.Name)) continue;
                    foreach (var entry in group.entries)
                    {
                        if (entry.labels.Any(l => config.ExcludedLabels.Contains(l)))
                            toMove.Add(entry);
                    }
                }

                if (toMove.Count > 0)
                {
                    var tempGroup = settings.CreateGroup(TempGroupName, false, false, false, null,
                        typeof(UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema));
                    tempGroup.GetSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>()
                        .IncludeInBuild = false;

                    var moved = new MovedEntries { TempGroupGuid = tempGroup.Guid };
                    foreach (var entry in toMove)
                    {
                        moved.Entries.Add(new MovedEntry
                        {
                            EntryGuid = entry.guid,
                            OriginalGroupGuid = entry.parentGroup.Guid,
                            ReadOnly = entry.ReadOnly,
                        });
                        settings.MoveEntry(entry, tempGroup, entry.ReadOnly, false);
                    }
                    context.SetProperty(MovedEntriesKey, moved);

                    Debug.Log($"[Build Forge/Addressables] Excluded {toMove.Count} entries by label " +
                        $"({string.Join(", ", config.ExcludedLabels)}).");
                }
            }
        }

        public void OnPostBuild(ForgeBuildContext context)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            // Restore group states
            var capturedGroupStates = context.GetProperty<Dictionary<string, bool>>(CapturedGroupStatesKey);
            if (capturedGroupStates != null)
            {
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;
                    var key = $"{group.Name}__includeInBuild";
                    if (capturedGroupStates.TryGetValue(key, out var wasIncluded))
                    {
                        var schema = group.GetSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();
                        if (schema != null)
                        {
                            schema.IncludeInBuild = wasIncluded;
                        }
                    }
                }
            }

            // Move label-excluded entries back and drop the temporary group
            var moved = context.GetProperty<MovedEntries>(MovedEntriesKey);
            if (moved != null)
            {
                var tempGroup = settings.FindGroup(g => g != null && g.Guid == moved.TempGroupGuid);
                var stranded = 0;
                foreach (var record in moved.Entries)
                {
                    var entry = settings.FindAssetEntry(record.EntryGuid);
                    var original = settings.FindGroup(g => g != null && g.Guid == record.OriginalGroupGuid);
                    if (entry == null || original == null)
                    {
                        stranded++;
                        continue;
                    }
                    settings.MoveEntry(entry, original, record.ReadOnly, false);
                }

                if (tempGroup != null)
                {
                    if (stranded == 0 && tempGroup.entries.Count == 0)
                        settings.RemoveGroup(tempGroup);
                    else
                        Debug.LogWarning($"[Build Forge/Addressables] {stranded} label-excluded entries could not be " +
                            $"moved back to their original group; they were left in '{tempGroup.Name}'.");
                }
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            }

            Debug.Log("[Build Forge/Addressables] Addressable settings restored.");
        }

        public void ContributeToManifest(BuildManifest manifest, ForgeBuildContext context)
        {
            var config = GetConfig(context.ForgeProfile).ForVariant(context.Variant);
            if (config.ExcludedGroups.Count == 0 && config.ExcludedLabels.Count == 0)
                return;

            var section = new ManifestSection("Addressables");

            if (config.ExcludedGroups.Count > 0)
                section.Add("excludedGroups", string.Join(";", config.ExcludedGroups));
            if (config.ExcludedLabels.Count > 0)
                section.Add("excludedLabels", string.Join(";", config.ExcludedLabels));

            manifest.AddSection(section);
        }

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI)
            => DescribeBuild(forgeProfile, isCI, null);

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI, string variant)
        {
            var config = GetConfig(forgeProfile).ForVariant(variant);
            return $"exclude {config.ExcludedGroups.Count} group(s) and {config.ExcludedLabels.Count} label(s) from the build";
        }

        public void OnDrawProfileGUI(ForgeProfile forgeProfile)
        {
            var config = GetConfig(forgeProfile);
            bool changed = false;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            // Silent: the profile inspector already shows NotApplicableReason.
            if (settings == null)
                return;

            EditorGUILayout.HelpBox(
                "Excluded groups and entries with an excluded label are stripped from the build.\n" +
                "Use this to remove content like spoilers from demo builds.\n" +
                "An excluded asset that a shipped asset references is still pulled in as an " +
                "implicit dependency — Addressables packs dependencies regardless of group.",
                MessageType.None);

            if ((config.ExcludedGroups.Count > 0 || config.ExcludedLabels.Count > 0)
                && !ContentRebuildGuaranteed(forgeProfile))
            {
                EditorGUILayout.HelpBox(MisconfigurationMessage, MessageType.Warning);
            }

            // Group exclusions
            EditorGUILayout.LabelField("Exclude Groups", EditorStyles.boldLabel);
            foreach (var group in settings.groups)
            {
                if (group == null || group.ReadOnly) continue;

                var isExcluded = config.ExcludedGroups.Contains(group.Name);
                var newExcluded = EditorGUILayout.Toggle(group.Name, isExcluded);

                if (newExcluded != isExcluded)
                {
                    if (newExcluded)
                        config.ExcludedGroups.Add(group.Name);
                    else
                        config.ExcludedGroups.Remove(group.Name);
                    changed = true;
                }
            }

            EditorGUILayout.Space(4);

            // Label exclusions
            EditorGUILayout.LabelField("Exclude Labels", EditorStyles.boldLabel);
            var allLabels = settings.GetLabels();
            foreach (var label in allLabels)
            {
                var isExcluded = config.ExcludedLabels.Contains(label);
                var newExcluded = EditorGUILayout.Toggle(label, isExcluded);

                if (newExcluded != isExcluded)
                {
                    if (newExcluded)
                        config.ExcludedLabels.Add(label);
                    else
                        config.ExcludedLabels.Remove(label);
                    changed = true;
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Additional Exclusions by Variant", EditorStyles.boldLabel);
            foreach (var variant in new[] { "Default" }.Concat(ForgeSettings.instance.BuildVariants))
            {
                var extra = config.VariantExclusions.FirstOrDefault(v => v.variant == variant);
                var enabled = extra != null;
                var nextEnabled = EditorGUILayout.Toggle(variant, enabled);
                if (enabled != nextEnabled)
                {
                    if (nextEnabled) { extra = new VariantAddressablesExclusions { variant = variant }; config.VariantExclusions.Add(extra); }
                    else config.VariantExclusions.Remove(extra);
                    changed = true;
                }
                if (!nextEnabled) continue;
                EditorGUI.indentLevel++;
                foreach (var group in settings.groups.Where(g => g != null && !g.ReadOnly))
                    changed |= ToggleExclusion(group.Name, extra.groups);
                foreach (var label in settings.GetLabels())
                    changed |= ToggleExclusion(label, extra.labels, "Label: ");
                EditorGUI.indentLevel--;
            }

            // Entries for a renamed or removed variant are not drawn above; each can
            // move to a variant that has no exclusions yet, or be removed.
            var unknown = config.UnknownVariantExclusions(ForgeSettings.instance.BuildVariants);
            if (unknown.Count > 0)
            {
                EditorGUILayout.HelpBox(UnknownVariantsError(config, ForgeSettings.instance.BuildVariants), MessageType.Error);
                var targets = new[] { "Default" }.Concat(ForgeSettings.instance.BuildVariants)
                    .Where(name => config.VariantExclusions.All(v => v.variant != name)).ToArray();
                foreach (var entry in unknown)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(entry.variant, $"{entry.groups.Count} group(s), {entry.labels.Count} label(s)");
                        if (targets.Length > 0)
                        {
                            var target = EditorGUILayout.Popup(0, new[] { "Move to\u2026" }.Concat(targets).ToArray(),
                                GUILayout.Width(120));
                            if (target > 0)
                            {
                                entry.variant = targets[target - 1];
                                changed = true;
                            }
                        }
                        if (GUILayout.Button("Remove", GUILayout.Width(70)))
                        {
                            config.VariantExclusions.Remove(entry);
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
                SaveConfig(forgeProfile, config);
        }

        static bool ToggleExclusion(string name, List<string> values, string prefix = "")
        {
            var before = values.Contains(name);
            var after = EditorGUILayout.Toggle(prefix + name, before);
            if (after == before) return false;
            if (after) values.Add(name); else values.Remove(name);
            return true;
        }

        static AddressablesStripperConfig GetConfig(ForgeProfile profile)
        {
            return profile.GetPluginConfig<AddressablesStripperConfig>(ConfigKey);
        }

        static void SaveConfig(ForgeProfile profile, AddressablesStripperConfig config)
        {
            profile.SetPluginConfig(ConfigKey, config);
        }
    }
}
