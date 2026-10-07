using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Returns all discovered plugins, sorted by execution order.
        /// </summary>
        public static IReadOnlyList<IForgePlugin> GetPlugins()
        {
            if (cachedPlugins != null)
                return cachedPlugins;

            cachedPlugins = new List<IForgePlugin>();

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
                    Debug.LogError(
                        $"[Build Forge] Failed to instantiate plugin {type.FullName}: {e.Message}");
                }
            }

            cachedPlugins = cachedPlugins.OrderBy(p => p.Order).ToList();
            return cachedPlugins;
        }

        /// <summary>
        /// Reads the plugin's [ForgePlugin] SafeToDefaultEnable flag.
        /// </summary>
        public static bool IsSafeToDefaultEnable(IForgePlugin plugin)
        {
            return IsSafeToDefaultEnable(plugin.GetType());
        }

        /// <summary>
        /// Type-based overload for callers that don't hold a plugin instance.
        /// </summary>
        public static bool IsSafeToDefaultEnable(Type pluginType)
        {
            var attr = (ForgePluginAttribute)Attribute.GetCustomAttribute(
                pluginType, typeof(ForgePluginAttribute));
            return attr == null || attr.SafeToDefaultEnable;
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
