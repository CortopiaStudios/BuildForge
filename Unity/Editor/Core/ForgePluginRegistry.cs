using System;
using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Discovers and manages Build Forge plugins via TypeCache.
    /// Plugins are discovered by the [ForgePlugin] attribute and must implement IForgePlugin.
    /// </summary>
    internal static class ForgePluginRegistry
    {
        static List<IForgePlugin> cachedPlugins;

        /// <summary>The plugins whose creation failed in the last discovery, with the error; tests add to it.</summary>
        internal static readonly List<(Type Type, string Error)> LoadFailures = new();

        /// <summary>
        /// Returns all discovered plugins, sorted by execution order.
        /// </summary>
        public static IReadOnlyList<IForgePlugin> GetPlugins()
        {
            if (cachedPlugins != null)
                return cachedPlugins;

            cachedPlugins = new List<IForgePlugin>();
            LoadFailures.Clear();

            var pluginTypes = TypeCache.GetTypesWithAttribute<ForgePluginAttribute>();

            foreach (var type in pluginTypes)
            {
                if (!typeof(IForgePlugin).IsAssignableFrom(type))
                {
                    Debug.LogWarning(
                        $"[Build Forge] Type {type.FullName} has [ForgePlugin] attribute but does not implement IForgePlugin. Skipping.");
                    continue;
                }

                if (type.IsAbstract || type.IsInterface)
                {
                    Debug.LogWarning(
                        $"[Build Forge] Type {type.FullName} has [ForgePlugin] attribute but is abstract/interface. Skipping.");
                    continue;
                }

                try
                {
                    var plugin = (IForgePlugin)Activator.CreateInstance(type);
                    cachedPlugins.Add(plugin);
                }
                catch (Exception e)
                {
                    var error = (e as System.Reflection.TargetInvocationException)?.InnerException?.Message ?? e.Message;
                    LoadFailures.Add((type, error));
                    Debug.LogError(
                        $"[Build Forge] Failed to instantiate plugin {type.FullName}: {error}");
                }
            }

            cachedPlugins = InPluginOrder(cachedPlugins);
            return cachedPlugins;
        }

        /// <summary>By Order, and plugins of the same Order by type name, so theirs doesn't depend on discovery.</summary>
        internal static List<IForgePlugin> InPluginOrder(IEnumerable<IForgePlugin> plugins)
            => plugins.OrderBy(p => p.Order).ThenBy(p => p.GetType().FullName, StringComparer.Ordinal).ToList();

        /// <summary>
        /// Why builds stop because a plugin failed to load, or null: a plugin that is on
        /// in Project Settings but could not be created would not run, and a profile may
        /// rely on it. One that is off there cannot run anyway and doesn't count.
        /// </summary>
        internal static string LoadError()
        {
            GetPlugins();
            return LoadError(LoadFailures,
                type => ForgeSettings.instance.IsPluginDisabledWithDefault(type.FullName, IsEnabledByDefault(type)));
        }

        internal static string LoadError(IEnumerable<(Type Type, string Error)> failures, Func<Type, bool> isOff)
        {
            var blocking = failures
                .Where(f => !isOff(f.Type))
                .Select(f => $"{f.Type.FullName} ({f.Error})")
                .ToList();
            return blocking.Count == 0 ? null
                : $"Build Forge plugin {string.Join(", ", blocking)} could not be loaded, so it would not run in this build. " +
                  "Fix it, or turn it off in Project Settings > Build Forge > Installed Plugins.";
        }

        /// <summary>
        /// Why builds of the profile stop because plugin settings cannot be loaded, or null:
        /// settings in the profile or in Project Settings whose type is gone or was renamed
        /// would be left out of the build.
        /// </summary>
        internal static string MissingSettingsError(ForgeProfile profile)
        {
            var missing = new List<string>();
            var inProfile = PluginConfigSlot.MissingTypes(profile, "pluginConfigs");
            if (inProfile != null)
                missing.Add($"{inProfile} in Unity Build Profile '{(profile.BuildProfile != null ? profile.BuildProfile.name : profile.name)}'");
            var inProjectSettings = PluginConfigSlot.MissingTypes(ForgeSettings.instance, "globalPluginConfigs");
            if (inProjectSettings != null)
                missing.Add($"{inProjectSettings} in Project Settings > Build Forge");
            return missing.Count == 0 ? null
                : $"Build Forge cannot load the plugin settings {string.Join(", ", missing)}: the plugin, or a class of its " +
                  "settings, is gone or was renamed, so builds would leave the settings out. Install the package that provides " +
                  "the plugin, correct the class in the asset if an update renamed it, or remove the settings if the plugin is " +
                  "gone for good; see Troubleshooting.";
        }

        /// <summary>
        /// Why builds and Apply of the profile stop before they change anything: a plugin
        /// that failed to load (<see cref="LoadError()"/>) or plugin settings Unity cannot
        /// load (<see cref="MissingSettingsError"/>). Null otherwise. Cheap enough to call
        /// on every repaint: the settings check is a native one unless something is missing.
        /// </summary>
        internal static string BlockingError(ForgeProfile profile)
            => LoadError() ?? MissingSettingsError(profile);

        /// <summary>
        /// Reads the plugin's [ForgePlugin] EnabledByDefault flag.
        /// </summary>
        public static bool IsEnabledByDefault(IForgePlugin plugin)
        {
            return IsEnabledByDefault(plugin.GetType());
        }

        /// <summary>
        /// Type-based overload for callers that don't hold a plugin instance.
        /// </summary>
        public static bool IsEnabledByDefault(Type pluginType)
        {
            var attr = (ForgePluginAttribute)Attribute.GetCustomAttribute(
                pluginType, typeof(ForgePluginAttribute));
            return attr == null || attr.EnabledByDefault;
        }

        /// <summary>
        /// Clears the cached plugin list, forcing re-discovery on next access.
        /// </summary>
        public static void ClearCache()
        {
            cachedPlugins = null;
        }
    }
}
