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

        /// <summary>The plugins whose creation or Order failed in the last discovery, with the error; tests add to it.</summary>
        internal static readonly List<(Type Type, string Error)> LoadFailures = new();

        /// <summary>
        /// Returns all discovered plugins, sorted by execution order.
        /// </summary>
        public static IReadOnlyList<IForgePlugin> GetPlugins()
        {
            if (cachedPlugins != null)
                return cachedPlugins;

            LoadFailures.Clear();
            cachedPlugins = Discover(TypeCache.GetTypesWithAttribute<ForgePluginAttribute>(), LoadFailures);
            return cachedPlugins;
        }

        /// <summary>
        /// Creates a plugin of each type, in plugin order: by Order, and plugins of the same
        /// Order by type name, so theirs doesn't depend on discovery. A type whose constructor
        /// or Order throws is left out and added to <paramref name="failures"/>.
        /// </summary>
        internal static List<IForgePlugin> Discover(IEnumerable<Type> types, List<(Type Type, string Error)> failures)
        {
            var created = new List<(IForgePlugin Plugin, int Order)>();
            foreach (var type in types)
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

                IForgePlugin plugin = null;
                try
                {
                    plugin = (IForgePlugin)Activator.CreateInstance(type);
                    // Read once, here: a plugin without a place in the order fails to load,
                    // so builds and Apply stop while it is on instead of running the plugins
                    // in discovery order.
                    created.Add((plugin, plugin.Order));
                }
                catch (Exception e)
                {
                    var inner = (e as System.Reflection.TargetInvocationException)?.InnerException;
                    var error = (inner != null ? ForgeEditorState.MessageOrType(inner) : null) ?? ForgeEditorState.MessageOrType(e);
                    if (plugin != null)
                        error = $"Order threw: {error}";
                    failures.Add((type, error));
                    Debug.LogError(
                        $"[Build Forge] Failed to {(plugin == null ? "instantiate" : "load")} plugin {type.FullName}: {error}");
                }
            }

            return created
                .OrderBy(p => p.Order)
                .ThenBy(p => p.Plugin.GetType().FullName, StringComparer.Ordinal)
                .Select(p => p.Plugin)
                .ToList();
        }

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
                  "settings, is gone or was renamed, so builds would leave the settings out. Install, or update, the package that provides " +
                  "the plugin, correct the class in the asset if an update renamed it, or remove the settings if the plugin is " +
                  "gone for good; see Troubleshooting.";
        }

        /// <summary>
        /// Why builds and Apply of the profile stop before they change anything: a plugin
        /// that failed to load (<see cref="LoadError()"/>), plugin settings Unity cannot
        /// load (<see cref="MissingSettingsError"/>), or a plugin that throws when asked
        /// whether it runs for the profile (<see cref="CallError(ForgeProfile)"/>). Null otherwise.
        /// For one profile at a time on a repaint, as the selected profile's details do: the
        /// settings check is a native one unless something is missing, but every plugin that
        /// is on is asked, and some of them read the Build Profile to answer.
        /// </summary>
        internal static string BlockingError(ForgeProfile profile)
            => BlockingError(profile, GetPlugins());

        internal static string BlockingError(ForgeProfile profile, IEnumerable<IForgePlugin> plugins)
            => LoadError() ?? MissingSettingsError(profile) ?? CallError(profile, plugins, IsOff);

        /// <summary>
        /// <see cref="CallError(ForgeProfile, IEnumerable{IForgePlugin}, Func{Type, bool})"/> for the
        /// discovered plugins that are on in Project Settings.
        /// </summary>
        internal static string CallError(ForgeProfile profile) => CallError(profile, GetPlugins(), IsOff);

        static bool IsOff(Type type) => ForgeSettings.instance.IsPluginDisabledWithDefault(type.FullName, IsEnabledByDefault(type));

        /// <summary>
        /// Why builds and Apply of the profile stop because a plugin that is on in Project
        /// Settings (not <paramref name="isOff"/>) throws in IsApplicable or IsEnabled, or
        /// null: whether it would run is unknown, and a build or Apply would stop on the
        /// throw, while the windows, which ask guarded, would show it as not running.
        /// </summary>
        internal static string CallError(ForgeProfile profile, IEnumerable<IForgePlugin> plugins, Func<Type, bool> isOff)
        {
            // Nothing is built or applied without a Build Profile, and both stop on that first.
            if (profile.BuildProfile == null)
                return null;
            var failing = new List<string>();
            foreach (var plugin in plugins)
            {
                if (isOff(plugin.GetType()))
                    continue;
                TakesPart(plugin, profile, out var error);
                if (error != null)
                    failing.Add($"{plugin.GetType().FullName} ({error})");
            }
            return failing.Count == 0 ? null
                : $"Build Forge plugin {string.Join(", ", failing)} could not say whether it runs for '{profile.DisplayName}', " +
                  "so Build Forge can't tell whether it would run in this build. " +
                  "Fix it, or turn it off in Project Settings > Build Forge > Installed Plugins.";
        }

        /// <summary>
        /// Whether the plugin runs for the profile, as builds and Apply decide: it applies to
        /// the profile's Build Profile and the profile doesn't switch it off. Asked without
        /// throwing, for checks and the windows: a plugin whose IsApplicable or IsEnabled
        /// throws doesn't run, <paramref name="error"/> names the call, and
        /// <see cref="BlockingError(ForgeProfile)"/> stops builds and Apply on it. Builds and
        /// Apply ask unguarded.
        /// </summary>
        internal static bool TakesPart(IForgePlugin plugin, ForgeProfile profile, out string error)
        {
            // Both, whatever the first answers: builds ask IsEnabled first, Apply IsApplicable.
            var applies = Ask(plugin, "IsApplicable", () => plugin.IsApplicable(profile.BuildProfile), out var applicableError);
            var enabled = Ask(plugin, "IsEnabled", () => plugin.IsEnabled(profile), out var enabledError);
            error = applicableError ?? enabledError;
            return error == null && applies && enabled != false;
        }

        // Worded like the windows' errors for a plugin that throws (PluginDisplay).
        static T Ask<T>(IForgePlugin plugin, string member, Func<T> call, out string error)
        {
            try
            {
                error = null;
                return call();
            }
            catch (Exception e)
            {
                error = $"{plugin.GetType().Name}.{member} threw: {ForgeEditorState.MessageOrType(e)}";
                return default;
            }
        }

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
