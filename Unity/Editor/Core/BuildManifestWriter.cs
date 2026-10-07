using System.IO;
using BuildForge.Editor.Configuration;
using BuildForge.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Writes the build manifest for the build in progress and gets it into the
    /// player's StreamingAssets without ever touching the project's Assets
    /// folder: the JSON goes to a Library temp folder, and
    /// <see cref="BuildManifestInjector"/> hands that folder to the build
    /// pipeline through <c>BuildPlayerContext.AddAdditionalPathToStreamingAssets</c>
    /// (the same mechanism Unity's own Adaptive Probe Volumes use). Earlier the
    /// manifest was created under Assets/StreamingAssets and deleted after the
    /// build; see ARCHITECTURE.md "Build Manifest" for why that stopped working
    /// in batch mode on 6000.6.
    /// </summary>
    internal static class BuildManifestWriter
    {
        /// <summary>Folder handed to the build; its contents land in StreamingAssets/BuildForge.</summary>
        internal const string ManifestDirectory = "Library/BuildForge/Manifest";
        const string ManifestFileName = "BuildManifest.json";
        internal static string ManifestPath => Path.Combine(ManifestDirectory, ManifestFileName);

        /// <summary>The StreamingAssets subfolder the runtime loader reads from.</summary>
        internal const string StreamingAssetsSubfolder = "BuildForge";

        public static BuildManifest CreateManifest(ForgeProfile forgeProfile, ForgeBuildContext context)
        {
            // Reading the static PlayerSettings API is correct here, not a bug: this
            // only runs from RunBuild, which guarantees the target Build Profile is
            // active (it throws otherwise), and Unity's static PlayerSettings API
            // reads the active profile's settings — so these values are the profile's,
            // including any OnPreBuild plugin and CLI -forgeVersion overrides applied first.
            // Do NOT "fix" this by parsing the profile's YAML blob; that would be more
            // fragile and redundant with the active-profile guarantee.
            var manifest = new BuildManifest
            {
                BuildProfileName = forgeProfile.DisplayName,
                BuildVariant = BuildVariants.EffectiveName(context.Variant, BuildVariants.Configured.Count > 0) ?? "",
                BuildTimestamp = System.DateTime.UtcNow.ToString("o"),
                BuildTarget = context.BuildTarget.ToString(),
                BuildTargetGroup = context.BuildTargetGroup.ToString(),
                ProductName = PlayerSettings.productName,
                ProductVersion = PlayerSettings.bundleVersion,
                BuildNumber = BuildNumberFor(context),
                UnityVersion = Application.unityVersion,
                BuildForgeVersion = GetBuildForgeVersion(),
                // Both follow the active Build Profile like the rest of the static API.
                DevelopmentBuild = context.DevelopmentBuild,
                ManagedCodeVariant = GetManagedCodeVariant(context.BuildTargetGroup)
            };

            return manifest;
        }

        /// <summary>
        /// Unity 6.6 split the managed code variant out of Development Build into
        /// its own player setting; earlier editors have no such setting, and the
        /// manifest field stays empty there.
        /// </summary>
        static string GetManagedCodeVariant(BuildTargetGroup group)
        {
#if UNITY_6000_6_OR_NEWER
            return PlayerSettings.GetManagedCodeVariant(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group)).ToString();
#else
            return "";
#endif
        }

        /// <summary>Writes the manifest to the Library temp folder the injector adds to the build.</summary>
        public static void WriteManifest(BuildManifest manifest)
        {
            try
            {
                // Start from an empty folder so nothing from an earlier build rides along.
                if (Directory.Exists(ManifestDirectory))
                    Directory.Delete(ManifestDirectory, true);
                Directory.CreateDirectory(ManifestDirectory);

                var json = manifest.ToJson();
                File.WriteAllText(ManifestPath, json);
                Debug.Log("[Build Forge] Build manifest written for the build.");

                if (ForgeSettings.instance.LogManifestContents)
                    Debug.Log($"[Build Forge] Manifest contents:\n{json}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Build Forge] Failed to write build manifest: {e.Message}");
                throw;
            }
        }

        /// <summary>Removes the temp manifest after the build. Nothing in Assets/ is involved.</summary>
        public static void CleanupManifest()
        {
            if (Directory.Exists(ManifestDirectory))
                Directory.Delete(ManifestDirectory, true);
        }

        static string GetBuildForgeVersion()
        {
            try
            {
                var packageJsonPath = "Packages/com.cortopiastudios.buildforge/package.json";
                if (File.Exists(packageJsonPath))
                {
                    var json = File.ReadAllText(packageJsonPath);
                    var packageInfo = JsonUtility.FromJson<PackageJsonVersion>(json);
                    return packageInfo?.version ?? "unknown";
                }
            }
            catch
            {
            }

            return "unknown";
        }

        /// <summary>
        /// The build's number: the Build Number plugin's on every platform, otherwise
        /// the Android version code or iOS build number; empty when there is none.
        /// </summary>
        internal static string BuildNumberFor(ForgeBuildContext context)
            => context.BuildNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture)
               ?? GetBuildNumber(context.BuildTarget);

        static string GetBuildNumber(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android:
                    return PlayerSettings.Android.bundleVersionCode.ToString();
                case BuildTarget.iOS:
                    return PlayerSettings.iOS.buildNumber;
                default:
                    return "";
            }
        }

        [System.Serializable]
        class PackageJsonVersion
        {
            public string version;
        }
    }

    /// <summary>
    /// Adds the manifest folder to the player's StreamingAssets for Build Forge
    /// builds. Unity copies the folder's contents into StreamingAssets/BuildForge
    /// at build time; the project's Assets folder is never touched, so there is
    /// nothing to import, clean up, or that version control could see.
    /// </summary>
    internal class BuildManifestInjector : BuildPlayerProcessor
    {
        public override int callbackOrder => 0;

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (!ForgeBuildRunner.IsForgeBuildInProgress || !ForgeSettings.instance.WriteBuildManifest)
                return;

            if (!File.Exists(BuildManifestWriter.ManifestPath))
                throw new BuildFailedException(
                    "[Build Forge] The required build manifest is missing; aborting the build.");

            buildPlayerContext.AddAdditionalPathToStreamingAssets(
                BuildManifestWriter.ManifestDirectory, BuildManifestWriter.StreamingAssetsSubfolder);
        }
    }
}
