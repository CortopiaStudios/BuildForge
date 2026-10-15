using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// A Unity Build Profile's own platform, which Unity's Build Profiles window shows by
    /// name and icon: Meta Quest for a Meta Quest profile, although it builds for Android.
    /// Unity has no public API for it (6000.3 to 6000.6), so the platform id comes from the
    /// profile, its name from Unity's internal BuildTargetDiscovery and its icons from the
    /// internal BuildProfileModuleUtil, the sources of the Build Profiles window. Both are
    /// null when that lookup is unavailable, and callers then show the build target instead.
    /// </summary>
    internal static class BuildProfilePlatform
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static readonly PropertyInfo PlatformIdProperty = typeof(BuildProfile).GetProperty("platformId", Any);
        static readonly MethodInfo DisplayNameMethod = PlatformMethod("UnityEditor.BuildTargetDiscovery", "BuildPlatformDisplayName", typeof(string));
        static readonly MethodInfo IconMethod = PlatformMethod("UnityEditor.Build.Profile.BuildProfileModuleUtil", "GetPlatformIcon", typeof(Texture2D));
        static readonly MethodInfo SmallIconMethod = PlatformMethod("UnityEditor.Build.Profile.BuildProfileModuleUtil", "GetPlatformIconSmall", typeof(Texture2D));
        static readonly Dictionary<string, string> DisplayNames = new();
        static readonly Dictionary<(string id, bool small, bool highDpi), Texture2D> Icons = new();

        /// <summary>The platform's name as Unity shows it, for example "Meta Quest", "Android™" or "Windows".</summary>
        internal static string DisplayName(BuildProfile profile)
        {
            var id = PlatformId(profile);
            if (id == null)
                return null;
            if (!DisplayNames.TryGetValue(id, out var name))
                DisplayNames[id] = name = Invoke(DisplayNameMethod, id) is string value && value.Length > 0 ? value : null;
            return name;
        }

        /// <summary>
        /// The platform's icon as the Build Profiles window loads it: large (32 points, as in
        /// its header) or small (16 points, as in its list), as the @2x texture on a
        /// high-DPI display.
        /// </summary>
        internal static Texture2D Icon(BuildProfile profile, bool small)
        {
            // Unity gives a platform it doesn't know its generic editor icon; callers show
            // the build target's instead, as they show its name.
            var id = PlatformId(profile);
            if (id == null || DisplayName(profile) == null)
                return null;
            var key = (id, small, EditorGUIUtility.pixelsPerPoint > 1f);
            if (!Icons.TryGetValue(key, out var icon))
                Icons[key] = icon = Invoke(small ? SmallIconMethod : IconMethod, id) as Texture2D;
            return icon;
        }

        /// <summary>The platform's id as the profile stores it (<c>m_PlatformId</c>), or null.</summary>
        internal static string PlatformId(BuildProfile profile)
        {
            if (profile == null)
                return null;
            if (PlatformIdProperty?.GetValue(profile) is string id && id.Length > 0)
                return id;
            var stored = new SerializedObject(profile).FindProperty("m_PlatformId")?.stringValue;
            return string.IsNullOrEmpty(stored) ? null : stored;
        }

        // The overload that takes a platform GUID. GUID is UnityEditor.GUID before 6000.6
        // and UnityEngine.GUID from it, so the parameter's own type is constructed.
        static MethodInfo PlatformMethod(string typeName, string name, Type returnType)
            => typeof(BuildProfile).Assembly.GetType(typeName)?
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == name && m.ReturnType == returnType
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "GUID");

        static object Invoke(MethodInfo method, string platformId)
        {
            if (method == null)
                return null;
            try
            {
                var guid = Activator.CreateInstance(method.GetParameters()[0].ParameterType, platformId);
                return method.Invoke(null, new[] { guid });
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
