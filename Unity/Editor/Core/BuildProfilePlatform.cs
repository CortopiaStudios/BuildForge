using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// A Unity Build Profile's own platform, which Unity's Build Profiles window shows by
    /// name and icon: Meta Quest for a Meta Quest profile, although it builds for Android.
    /// Unity has no public API for it (6000.3 to 6000.6), so the platform id comes from the
    /// profile and its name and icon name from Unity's internal BuildTargetDiscovery, the
    /// source of the Build Profiles window. Both are null when that lookup is unavailable,
    /// and callers then show the build target instead.
    /// </summary>
    internal static class BuildProfilePlatform
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static readonly PropertyInfo PlatformIdProperty = typeof(BuildProfile).GetProperty("platformId", Any);
        static readonly MethodInfo DisplayNameMethod = DiscoveryMethod("BuildPlatformDisplayName");
        static readonly MethodInfo IconNameMethod = DiscoveryMethod("BuildPlatformIconName");
        static readonly Dictionary<string, (string displayName, string iconName)> Cache = new();

        /// <summary>The platform's name as Unity shows it, for example "Meta Quest", "Android™" or "Windows".</summary>
        internal static string DisplayName(BuildProfile profile) => Lookup(profile).displayName;

        /// <summary>The name of the platform's icon, for example "BuildSettings.Meta".</summary>
        internal static string IconName(BuildProfile profile) => Lookup(profile).iconName;

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

        static (string displayName, string iconName) Lookup(BuildProfile profile)
        {
            var id = PlatformId(profile);
            if (id == null)
                return default;
            if (!Cache.TryGetValue(id, out var found))
                Cache[id] = found = (Call(DisplayNameMethod, id), Call(IconNameMethod, id));
            return found;
        }

        // The overload that takes a platform GUID. GUID is UnityEditor.GUID before 6000.6
        // and UnityEngine.GUID from it, so the parameter's own type is constructed.
        static MethodInfo DiscoveryMethod(string name)
            => typeof(BuildProfile).Assembly.GetType("UnityEditor.BuildTargetDiscovery")?
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == name && m.ReturnType == typeof(string)
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "GUID");

        static string Call(MethodInfo method, string platformId)
        {
            if (method == null)
                return null;
            try
            {
                var guid = Activator.CreateInstance(method.GetParameters()[0].ParameterType, platformId);
                var value = method.Invoke(null, new[] { guid }) as string;
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
