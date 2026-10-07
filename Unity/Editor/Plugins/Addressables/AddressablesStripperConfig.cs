using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildForge.Editor.Addressables
{
    [Serializable]
    internal class AddressablesStripperConfig
    {
        [Tooltip("Addressable groups to exclude from this build (by group name).")]
        [SerializeField] List<string> excludedGroups = new();

        [Tooltip("Addressable labels to exclude from this build.")]
        [SerializeField] List<string> excludedLabels = new();

        [SerializeField] List<VariantAddressablesExclusions> variantExclusions = new();

        public List<string> ExcludedGroups => excludedGroups;
        public List<string> ExcludedLabels => excludedLabels;
        internal List<VariantAddressablesExclusions> VariantExclusions => variantExclusions;

        internal AddressablesStripperConfig ForVariant(string variant)
        {
            var result = new AddressablesStripperConfig();
            var extra = variantExclusions.FirstOrDefault(v => v.variant == (variant ?? "Default"));
            result.excludedGroups.AddRange(excludedGroups.Concat(extra?.groups ?? new List<string>()).Distinct());
            result.excludedLabels.AddRange(excludedLabels.Concat(extra?.labels ?? new List<string>()).Distinct());
            return result;
        }

        /// <summary>
        /// Entries for a variant that is not configured, left behind by a rename or
        /// removal. They apply to no build, so a renamed variant would silently ship
        /// the content they exclude; the plugin stops builds until they are moved
        /// or removed.
        /// </summary>
        internal List<VariantAddressablesExclusions> UnknownVariantExclusions(IReadOnlyList<string> variants) =>
            variantExclusions.Where(v => v.variant != "Default" && !(variants?.Contains(v.variant) ?? false)).ToList();
    }

    [Serializable]
    internal sealed class VariantAddressablesExclusions
    {
        public string variant = "Default";
        public List<string> groups = new();
        public List<string> labels = new();
    }
}
