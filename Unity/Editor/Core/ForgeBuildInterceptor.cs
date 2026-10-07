using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Intercepts BuildPipeline.BuildPlayer calls to prevent builds that bypass Build Forge.
    /// Uses IPreprocessBuildWithReport which is called before any build starts.
    /// </summary>
    internal class ForgeBuildInterceptor : IPreprocessBuildWithReport
    {
        // Run very early to intercept before anything else
        public int callbackOrder => -10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            // If Build Forge is performing the build, allow it
            if (ForgeBuildRunner.IsForgeBuildInProgress)
                return;

            // If interception is disabled in settings, allow it
            if (!ForgeSettings.instance.InterceptBuilds)
                return;

            // A build was started outside of Build Forge - block it
            var message =
                "Build Forge is configured to intercept builds.\n\n" +
                "Please use the Build Forge Window (Window > Build Forge > Build) to start builds.\n\n" +
                "This ensures consistent builds with proper settings restoration.\n\n" +
                "You can disable this in Project Settings > Build Forge > Intercept Builds.";

            Debug.LogError($"[Build Forge] Build intercepted. {message}");
            throw new BuildFailedException(message);
        }
    }
}
