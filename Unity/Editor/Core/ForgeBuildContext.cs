using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Context object passed to plugins during the build process.
    /// Contains all information about the current build.
    /// </summary>
    public class ForgeBuildContext
    {
        public BuildProfile BuildProfile { get; }
        public Configuration.ForgeProfile ForgeProfile { get; }
        public BuildTarget BuildTarget { get; }
        public BuildTargetGroup BuildTargetGroup { get; }
        public string OutputPath { get; }
        public bool Succeeded { get; internal set; }
        /// <summary>The effective native development mode, resolved before any build plugin runs.</summary>
        public bool DevelopmentBuild { get; internal set; }

        /// <summary>The build variant (Project Settings > Build Forge > Build Variants), or null for the plain build.</summary>
        public string Variant { get; }

        /// <summary>The variant's scripting define (BUILD_VARIANT_&lt;NAME&gt;), or null for the plain build.</summary>
        public string VariantDefine => Variant != null ? BuildVariants.DefineFor(Variant) : null;

        /// <summary>
        /// True when the build runs on a build machine rather than a developer's
        /// editor — batch mode by default, overridable with -forgeCI [true|false]
        /// or the FORGE_CI environment variable. Plugins use it for work that is
        /// only worth doing in CI (symbol upload, for example).
        /// </summary>
        public bool IsCI { get; }

        /// <summary>
        /// Messages for post-build restore/cleanup steps that threw. The build
        /// artifact is complete when this is non-empty, but the project may be
        /// left in a modified state — the CLI treats it as a build failure.
        /// </summary>
        internal List<string> PostBuildFailures { get; } = new();

        /// <summary>The number the Build Number plugin set for this build, on every platform; null without it.</summary>
        internal int? BuildNumber { get; set; }

        readonly Dictionary<string, object> properties = new();

        // Contexts are created exclusively by ForgeBuildRunner; plugins receive
        // them, they don't fabricate them.
        internal ForgeBuildContext(
            BuildProfile buildProfile,
            Configuration.ForgeProfile forgeProfile,
            BuildTarget buildTarget,
            string outputPath,
            bool isCI,
            string variant = null)
        {
            BuildProfile = buildProfile;
            DevelopmentBuild = EditorUserBuildSettings.development;
            ForgeProfile = forgeProfile;
            BuildTarget = buildTarget;
            BuildTargetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
            OutputPath = outputPath;
            IsCI = isCI;
            Variant = string.IsNullOrEmpty(variant) ? null : variant;
        }

        /// <summary>
        /// Store arbitrary data in the context for use by other plugins.
        /// </summary>
        public void SetProperty(string key, object value)
        {
            properties[key] = value;
        }

        /// <summary>
        /// Retrieve data stored by this or other plugins.
        /// </summary>
        public T GetProperty<T>(string key, T defaultValue = default)
        {
            return properties.TryGetValue(key, out var value) && value is T typed
                ? typed
                : defaultValue;
        }

    }
}
