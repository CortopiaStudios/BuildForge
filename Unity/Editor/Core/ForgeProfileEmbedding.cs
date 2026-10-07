using System;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// A Build Forge Profile is stored inside its Unity Build Profile as a Build
    /// Profile component (<c>BuildProfile.CreateComponent</c>, public since Unity
    /// 6.3): a sub-asset of the profile's .asset. The two share a file and a
    /// name, and renaming, duplicating or deleting the Build Profile carries the
    /// Build Forge settings with it.
    /// </summary>
    internal static class ForgeProfileEmbedding
    {
        /// <summary>The sub-asset's name in the Build Profile's .asset.</summary>
        public const string ComponentName = "Build Forge";

        /// <summary>
        /// Only Build Profile assets in the project can hold Build Forge settings:
        /// Unity's platform profiles live in Library/, which is not committed.
        /// </summary>
        public static bool CanHold(BuildProfile buildProfile) =>
            buildProfile != null && AssetDatabase.GetAssetPath(buildProfile).StartsWith("Assets/", StringComparison.Ordinal);

        /// <summary>The Build Forge Profile stored in <paramref name="buildProfile"/>, or null.</summary>
        public static ForgeProfile Get(BuildProfile buildProfile) =>
            CanHold(buildProfile) ? buildProfile.GetComponent<ForgeProfile>() : null;

        /// <summary>Adds Build Forge settings to a Build Profile asset, or returns the ones it has.</summary>
        public static ForgeProfile Add(BuildProfile buildProfile)
        {
            if (!CanHold(buildProfile))
                throw new InvalidOperationException("Build Forge settings can only be added to a Build Profile asset in the project.");
            var existing = Get(buildProfile);
            if (existing != null)
                return existing;

            var profile = buildProfile.CreateComponent<ForgeProfile>();
            profile.name = ComponentName;
            Save(buildProfile, profile);
            BuildProfileDefines.EnsureDeferred(buildProfile);
            return profile;
        }

        /// <summary>Removes the Build Forge settings from <paramref name="buildProfile"/>.</summary>
        public static void Remove(BuildProfile buildProfile)
        {
            if (Get(buildProfile) == null)
                return;
            buildProfile.RemoveComponent<ForgeProfile>();
            EditorUtility.SetDirty(buildProfile);
            AssetDatabase.SaveAssetIfDirty(buildProfile);
        }

        static void Save(BuildProfile buildProfile, ForgeProfile profile)
        {
            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(buildProfile);
            AssetDatabase.SaveAssetIfDirty(buildProfile);
        }
    }
}
