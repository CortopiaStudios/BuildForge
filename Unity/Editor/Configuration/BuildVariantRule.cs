using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildForge.Editor.Configuration
{
    internal enum VariantOverride { Inherit, Disabled, Enabled }

    /// <summary>Optional behavior for one existing variant name; "Default" addresses the plain build.</summary>
    [Serializable]
    internal sealed class BuildVariantRule
    {
        [SerializeField] string variant = "Default";
        [SerializeField] VariantOverride developmentBuild;
        [SerializeField] VariantOverride markProductName;
        [Tooltip("Defines managed by variants, in addition to BUILD_VARIANT_<NAME>.")]
        [SerializeField] List<string> scriptingDefines = new();
        [Tooltip("The version (Player Settings > Version) of this variant's builds, for example {Version}.{BuildNumber}d. " +
                 "Empty keeps the version. {Version} is the Player Settings version, {BuildNumber} the build number " +
                 "(the Build Number plugin's, otherwise the Android version code or iOS build number), {Variant} the variant's name.")]
        [SerializeField] string version = "";
        [Tooltip("Unity build settings for this variant's builds: development options, IL2CPP, stripping and build output. " +
                 "Inherit keeps the profile's own setting. Builds only; Apply does not write them.")]
        [SerializeField] VariantBuildConfiguration buildConfiguration = new();

        public string Variant { get => variant; set => variant = value; }
        public VariantOverride DevelopmentBuild { get => developmentBuild; set => developmentBuild = value; }
        public VariantOverride MarkProductName { get => markProductName; set => markProductName = value; }
        public List<string> ScriptingDefines => scriptingDefines;
        public string Version { get => version; set => version = value; }
        public VariantBuildConfiguration BuildConfiguration => buildConfiguration ??= new VariantBuildConfiguration();
    }
}
