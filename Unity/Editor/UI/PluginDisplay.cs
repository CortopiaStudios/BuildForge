using System;
using System.Collections.Generic;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.UI
{
    /// <summary>
    /// A plugin's values as the windows show them. A plugin that throws while a window asks
    /// for one gets a fallback and one logged error, instead of ending the window's drawing,
    /// which would take the action bar with it and repeat the error on every repaint. Only
    /// for display: builds call plugins unguarded, and stop on what they throw.
    /// </summary>
    internal static class PluginDisplay
    {
        static readonly HashSet<string> Reported = new();

        /// <summary>For tests: report each failure again.</summary>
        internal static void ForgetReported() => Reported.Clear();

        /// <summary><paramref name="call"/>'s value, or <paramref name="fallback"/> with <paramref name="error"/> when it throws.</summary>
        internal static T Try<T>(IForgePlugin plugin, string what, Func<T> call, T fallback, out string error)
        {
            try
            {
                error = null;
                return call();
            }
            catch (Exception e)
            {
                error = $"{plugin.GetType().Name}.{what} threw: {e.Message}";
                if (Reported.Add($"{plugin.GetType().FullName}.{what}"))
                    Debug.LogError($"[Build Forge] {error}\n{e}");
                return fallback;
            }
        }

        internal static string Name(IForgePlugin plugin) => Try(plugin, "DisplayName", () => plugin.DisplayName, plugin.GetType().Name, out _);

        internal static string Description(IForgePlugin plugin) => Try(plugin, "Description", () => plugin.Description, null, out _);

        /// <summary>Whether it applies; one that throws counts as not applying, with the error as the reason.</summary>
        internal static bool Applies(IForgePlugin plugin, BuildProfile buildProfile, out string reason)
        {
            var applies = Try(plugin, "IsApplicable", () => plugin.IsApplicable(buildProfile), false, out var error);
            reason = error ?? (applies ? null : Try(plugin, "NotApplicableReason", () => plugin.NotApplicableReason(buildProfile), null, out _));
            return applies;
        }

        internal static bool? Enabled(IForgePlugin plugin, ForgeProfile profile)
            => Try(plugin, "IsEnabled", () => plugin.IsEnabled(profile), null, out _);

        internal static string Describe(IForgePlugin plugin, ForgeProfile profile, bool isCI, string variant)
            => Try(plugin, "DescribeBuild", () => plugin.DescribeBuild(profile, isCI, variant), null, out _);

        /// <summary>The plugin's warnings; one that throws gives a warning saying so.</summary>
        internal static IReadOnlyList<string> Warnings(IForgePlugin plugin, ForgeProfile profile, string variant)
        {
            var warnings = Try(plugin, "Validate", () => plugin.Validate(profile, variant), null, out var error);
            if (error != null)
                return new[] { $"{Name(plugin)} could not check this profile's settings: {error}" };
            return warnings ?? Array.Empty<string>();
        }
    }
}
