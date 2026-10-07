using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Enforces the 1:1 relationship between Build Forge Profiles and Unity
    /// Build Profiles. A Unity Build Profile identifies its Build Forge Profile
    /// (the CI entry point resolves it from the active Build Profile), which
    /// only holds when no two Build Forge Profiles reference the same one.
    /// The pure members take the profile list so they are testable without
    /// the asset database.
    /// </summary>
    internal static class ForgeProfileLookup
    {
        /// <summary>Every Build Forge Profile: the ones stored in Unity Build Profile assets.</summary>
        public static List<ForgeProfile> FindAll()
        {
            return AssetDatabase.FindAssets($"t:{nameof(BuildProfile)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<BuildProfile>)
                .Select(ForgeProfileEmbedding.Get)
                .Where(p => p != null)
                .ToList();
        }

        /// <summary>Other Build Forge Profiles that reference the same Unity Build Profile.</summary>
        public static List<ForgeProfile> FindSharing(ForgeProfile profile, IReadOnlyList<ForgeProfile> all)
        {
            if (profile == null || profile.BuildProfile == null)
                return new List<ForgeProfile>();
            return all.Where(p => p != null && p != profile && p.BuildProfile == profile.BuildProfile).ToList();
        }

        /// <summary>
        /// The error to show for <paramref name="profile"/> when it shares its
        /// Unity Build Profile with another Build Forge Profile, or null.
        /// </summary>
        public static string SharedBuildProfileError(ForgeProfile profile, IReadOnlyList<ForgeProfile> all)
        {
            var sharing = FindSharing(profile, all);
            if (sharing.Count == 0)
                return null;
            var names = string.Join(", ", sharing.Select(p => $"'{p.ProfileName}'"));
            return $"Unity Build Profile '{profile.BuildProfile.name}' has more than one set of Build Forge settings " +
                   $"(also {names}). Each Unity Build Profile must have exactly one; " +
                   "create a separate Unity Build Profile for each variant.";
        }

        /// <summary>
        /// The single Build Forge Profile for <paramref name="buildProfile"/>, or
        /// null with <paramref name="error"/> set when there is none or more than one.
        /// </summary>
        public static ForgeProfile ResolveForBuildProfile(BuildProfile buildProfile,
            IReadOnlyList<ForgeProfile> all, out string error)
        {
            error = null;
            if (buildProfile == null)
            {
                error = "No Unity Build Profile is active.";
                return null;
            }

            var matches = all.Where(p => p != null && p.BuildProfile == buildProfile).ToList();
            if (matches.Count == 1)
                return matches[0];

            if (matches.Count == 0)
            {
                error = $"Unity Build Profile '{buildProfile.name}' has no Build Forge settings. " +
                        "Select it in the Project window and use Assets > Build Forge > Add Build Forge Settings.";
                return null;
            }

            var names = string.Join(", ", matches.Select(p => $"'{p.ProfileName}'"));
            error = $"Unity Build Profile '{buildProfile.name}' has {matches.Count} sets of Build Forge settings " +
                    $"({names}); it must have exactly one.";
            return null;
        }
    }
}
