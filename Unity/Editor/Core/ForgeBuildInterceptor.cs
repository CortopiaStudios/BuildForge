using System;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Stops player builds that bypass Build Forge while Project Settings > Build
    /// Forge > Intercept Builds is on. Unity's own Build and Build And Run (the
    /// Build Profiles window, File > Build And Run) are caught through
    /// BuildPlayerWindow's options handler, before Unity asks for a location; any
    /// other BuildPipeline.BuildPlayer call fails in the pre-build callback, which
    /// Unity calls before any build starts. An interactive editor shows a dialog
    /// either way, since a console line is easy to miss.
    /// </summary>
    [InitializeOnLoad]
    internal class ForgeBuildInterceptor : IPreprocessBuildWithReport
    {
        internal const string Message =
            "Build Forge is configured to intercept builds.\n\n" +
            "Please use the Build Forge Window (Window > Build Forge > Build) or the Build Forge element in the " +
            "main toolbar to start builds.\n\n" +
            "This ensures consistent builds with proper settings restoration.\n\n" +
            "You can disable this in Project Settings > Build Forge > Intercept Builds.";

        const string DialogTitle = "Build Forge intercepted this build";
        const string DialogMessage =
            "Builds of this project go through Build Forge, so that every build gets its plugin settings, " +
            "manifest and restore. Unity's own Build buttons and scripts that call BuildPipeline.BuildPlayer " +
            "are blocked.\n\n" +
            "Build from the Build Forge window, or with Build or Build and Run in the Build Forge element of " +
            "the main toolbar.\n\n" +
            "To let other builds through, turn off Project Settings > Build Forge > Intercept Builds.";
        const string BuildWindowMenu = "Window/Build Forge/Build";

        /// <summary>Called instead of the dialog when set, so tests don't wait for a click.</summary>
        internal static Action DialogOverride;

        static ForgeBuildInterceptor()
        {
            // Batch mode has no Build Profiles window. Registering replaces a handler
            // another script registered (Unity warns), so only while interception is
            // on; the handler then only blocks and never needs Unity's own default,
            // which alone knows whether to ask for a location. A change of the setting
            // reaches the handler at the next domain reload, see OnUnityBuildButton.
            if (Application.isBatchMode || !ForgeSettings.instance.InterceptBuilds)
                return;
            BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(OnUnityBuildButton);
        }

        /// <summary>
        /// Unity's Build and Build And Run call this for the build options, before
        /// asking for a location. Throwing BuildMethodException ends the build
        /// without one and without an error.
        /// </summary>
        internal static BuildPlayerOptions OnUnityBuildButton(BuildPlayerOptions options)
        {
            // Interception was turned off after this handler was registered: Unity's
            // default, except that it asks for a location every time until the next
            // domain reload unregisters the handler.
            if (!ForgeSettings.instance.InterceptBuilds || ForgeBuildRunner.IsForgeBuildInProgress)
                return BuildPlayerWindow.DefaultBuildMethods.GetBuildPlayerOptions(options);

            Debug.LogWarning("[Build Forge] Unity's build was stopped before it started. " + Message.Replace("\n\n", " "));
            ShowDialog();
            throw new BuildPlayerWindow.BuildMethodException();
        }

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
            Debug.LogError($"[Build Forge] Build intercepted. {Message}");
            ShowDialog();
            throw new BuildFailedException(Message);
        }

        static void ShowDialog()
        {
            if (DialogOverride != null)
            {
                DialogOverride();
                return;
            }
            // Batch mode has nobody to show it to; the log has the message.
            if (Application.isBatchMode)
                return;
            if (EditorUtility.DisplayDialog(DialogTitle, DialogMessage, "Open Build Forge", "Close"))
                // Once the build call has returned.
                EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem(BuildWindowMenu);
        }
    }
}
