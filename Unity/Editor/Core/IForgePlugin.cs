using System.Collections.Generic;
using UnityEditor.Build.Profile;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Interface for Build Forge build plugins. Plugins extend the build process
    /// with custom pre-build and post-build steps.
    /// </summary>
    public interface IForgePlugin
    {
        /// <summary>
        /// Display name for this plugin.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// One-line summary of what the plugin does, shown as a tooltip when
        /// hovering the plugin's name in plugin lists (profile inspector,
        /// Project Settings, Build window). Return null (the default) for no
        /// tooltip.
        /// </summary>
        string Description => null;

        /// <summary>
        /// Execution order. Lower values execute first.
        /// </summary>
        int Order => 0;

        /// <summary>
        /// Called before the build begins. Use this to apply custom settings.
        /// Store any state needed for restoration.
        /// Complete the work before returning; the player build starts next.
        /// Do not use async void or defer required work to an editor callback.
        /// </summary>
        void OnPreBuild(ForgeBuildContext context);

        /// <summary>
        /// Called after the build completes (success or failure).
        /// Restore any settings changed in OnPreBuild.
        /// Called in a finally block, so it runs even on build failure.
        /// Not guaranteed on editor crash or domain reload — see ARCHITECTURE.md.
        /// Complete restoration before returning; the CLI may exit immediately.
        /// The selected variant's scripting define, development flags and
        /// marked product name are applied before OnPreBuild and restored after
        /// every OnPostBuild, so a plugin may snapshot and restore any setting.
        /// </summary>
        void OnPostBuild(ForgeBuildContext context);

        /// <summary>
        /// Returns true if this plugin is applicable for the given build profile.
        /// </summary>
        bool IsApplicable(BuildProfile profile);

        /// <summary>
        /// A short sentence explaining why <see cref="IsApplicable"/> returned
        /// false for this profile, shown in the profile inspector (e.g. "Only
        /// applicable to Android build profiles."). Return null for a generic
        /// message. Only called when IsApplicable is false; the profile is
        /// never null.
        /// </summary>
        string NotApplicableReason(BuildProfile profile) => null;

        /// <summary>
        /// Per-profile enable switch. Return null when the plugin has no on/off
        /// switch (the default) — including plugins that are merely
        /// unconfigured. When non-null, the profile inspector renders an
        /// "Enabled" checkbox above the plugin GUI (written back via
        /// <see cref="SetEnabled"/>), shows "(disabled)" in the foldout header
        /// when false, and the build runner skips the plugin entirely when
        /// false — OnPreBuild/OnPostBuild need no enabled guard of their own.
        /// </summary>
        bool? IsEnabled(Configuration.ForgeProfile forgeProfile) => null;

        /// <summary>
        /// One short line saying what this plugin will do in a build of
        /// <paramref name="forgeProfile"/>, shown next to the plugin in the
        /// build window's "Plugins for this build" list (e.g. "crash reporting
        /// off (CI only; local build)", "build number from git commit count + 20").
        /// <paramref name="isCI"/> is the CI detection the build will see
        /// (<see cref="ForgeBuildContext.IsCI"/>). Return null (the default)
        /// for no description. Only called for plugins that will run.
        /// </summary>
        string DescribeBuild(Configuration.ForgeProfile forgeProfile, bool isCI) => null;

        /// <summary>Variant-aware description; existing plugins retain their original implementation.</summary>
        string DescribeBuild(Configuration.ForgeProfile forgeProfile, bool isCI, string variant)
            => DescribeBuild(forgeProfile, isCI);

        /// <summary>
        /// Persists the per-profile enable switch; called by the profile
        /// inspector's "Enabled" checkbox. Plugins returning non-null from
        /// <see cref="IsEnabled"/> must implement this. Default does nothing.
        /// </summary>
        void SetEnabled(Configuration.ForgeProfile forgeProfile, bool enabled) { }


        /// <summary>
        /// Validates the profile configuration and returns a list of warnings.
        /// Called by the build window to show warning icons. Return an empty
        /// list if everything is valid.
        /// </summary>
        IReadOnlyList<string> Validate(Configuration.ForgeProfile forgeProfile) =>
            System.Array.Empty<string>();

        /// <summary>Variant-aware validation; the default delegates to the original contract.</summary>
        IReadOnlyList<string> Validate(Configuration.ForgeProfile forgeProfile, string variant)
            => Validate(forgeProfile);

        /// <summary>
        /// Draw plugin configuration UI in the Build Forge section of the Build Profile editor.
        /// Called for each applicable plugin when the section is drawn.
        /// Default implementation does nothing.
        /// </summary>
        void OnDrawProfileGUI(Configuration.ForgeProfile forgeProfile) { }

        /// <summary>
        /// Draw global plugin settings UI in Project Settings > Build Forge.
        /// Called under the plugin's toggle in the Installed Plugins section.
        /// Default implementation does nothing.
        /// </summary>
        void OnDrawSettingsGUI() { }
    }
}
