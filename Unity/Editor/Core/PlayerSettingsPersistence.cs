using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace BuildForge.Editor.Core
{
    internal static class PlayerSettingsPersistence
    {
        // Unity's profile stores an embedded YAML copy separately from its live
        // PlayerSettings object. SaveAssets alone can save the previous copy.
        // This is the serializer Unity's own profile editor uses on 6000.3/6000.6.
        static readonly MethodInfo SerializePlayerSettings = typeof(BuildProfile).GetMethod(
            "SerializePlayerSettings", BindingFlags.Instance | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);

        internal static void SaveRestoredSettings()
        {
            // Re-resolve after BuildPlayer: loaded assets can be unloaded during a build.
            var profile = BuildProfile.GetActiveBuildProfile();
            if (profile != null)
            {
                if (SerializePlayerSettings == null)
                    throw new NotSupportedException(
                        "This Unity editor cannot serialize restored Build Profile PlayerSettings.");

                SerializePlayerSettings.Invoke(profile, null);
                EditorUtility.SetDirty(profile);
            }

            AssetDatabase.SaveAssets();
        }
    }
}
