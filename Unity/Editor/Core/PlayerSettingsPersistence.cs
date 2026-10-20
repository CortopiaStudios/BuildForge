using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace BuildForge.Editor.Core
{
    internal static class PlayerSettingsPersistence
    {
        // Up to 6000.6, Unity's profile stores an embedded YAML copy separately from its
        // live PlayerSettings object, and SaveAssets alone can save the previous copy.
        // This is the serializer Unity's own profile editor uses on 6000.3/6000.6.
        static readonly MethodInfo SerializePlayerSettings = typeof(BuildProfile).GetMethod(
            "SerializePlayerSettings", BindingFlags.Instance | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);

        // From 6000.7, the profile keeps its own Player Settings in a sub-asset that the
        // static API writes to directly: there is no copy to refresh, only the asset to save.
        static readonly FieldInfo PlayerSettingsSubasset = typeof(BuildProfile).GetField(
            "buildProfilePlayerSettings", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Whether this editor keeps a profile's own Player Settings where Build Forge can save them.</summary>
        internal static bool CanSaveProfilePlayerSettings => SerializePlayerSettings != null || PlayerSettingsSubasset != null;

        internal static void SaveRestoredSettings()
        {
            // Re-resolve after BuildPlayer: loaded assets can be unloaded during a build.
            var profile = BuildProfile.GetActiveBuildProfile();
            NotSupportedException unsupported = null;
            if (profile != null)
            {
                if (SerializePlayerSettings != null)
                {
                    SerializePlayerSettings.Invoke(profile, null);
                    EditorUtility.SetDirty(profile);
                }
                else if (PlayerSettingsSubasset != null)
                {
                    // Null for a profile without its own Player Settings, which uses the project's.
                    if (PlayerSettingsSubasset.GetValue(profile) is UnityEngine.Object subasset && subasset != null)
                        EditorUtility.SetDirty(subasset);
                }
                else
                    unsupported = new NotSupportedException(
                        "This Unity editor cannot serialize restored Build Profile PlayerSettings.");
            }

            // The project's settings are saved even where the profile's copy can't be.
            AssetDatabase.SaveAssets();
            if (unsupported != null)
                throw unsupported;
        }
    }
}
