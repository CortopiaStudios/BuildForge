using BuildForge.Editor.Core;

namespace BuildForge
{
    /// <summary>
    /// CI entry points for Build Forge.
    /// Build usage: unity run "&lt;project dir&gt;" --non-interactive -- -nographics -silent-crashes -logFile -
    ///        -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Build
    ///        -activeBuildProfile "Assets/Path/UnityProfile.asset" [-forgeVariant "Internal"]
    /// (the build window generates this for the selected profile)
    /// It builds the Build Forge settings of the active Unity Build Profile.
    /// Unity CLI supplies -quit. Keep this entry point synchronous: finish all
    /// build work and restoration before explicitly exiting with the build result.
    /// Batch mode marks the build as CI for plugins (ForgeBuildContext.IsCI); pass
    /// -forgeCI false (or set FORGE_CI=0) for a local scripted build that should not be.
    /// Activate usage: the same command with -executeMethod BuildForge.CommandLine.Activate
    ///        -forgeBuildProfile "Assets/Path/UnityProfile.asset" instead of -activeBuildProfile
    /// and the build's arguments. Run it before a build that changes platform: across a
    /// platform change, Unity 6000.3.23's -activeBuildProfile compiles the new profile's
    /// defines for the previous platform first.
    /// </summary>
    public static class CommandLine
    {
        public static void Build() => ForgeBuildRunner.BuildFromCommandLine();

        /// <summary>
        /// Activates the Build Profile given to -forgeBuildProfile, like the build
        /// window's Activate, and exits with 0, or with 1 and the reason in the log.
        /// </summary>
        public static void Activate() => ForgeEditorState.ActivateFromCommandLine();
    }
}
