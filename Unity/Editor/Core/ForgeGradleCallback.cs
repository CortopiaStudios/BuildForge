using System;
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEngine;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Forwards Unity's Gradle project callback to the plugins of the Build
    /// Forge build in progress that implement <see cref="IForgeGradleProcessor"/>.
    /// </summary>
    internal static class ForgeGradleCallback
    {
        /// <summary>
        /// Unity callback order of Build Forge's Gradle step, also used for its
        /// late pre-build step: after the vendors' own callbacks (Meta's Gradle
        /// step runs at 99999, its pre-build step at 3). IForgeGradleProcessor
        /// documents the value.
        /// </summary>
        internal const int CallbackOrder = 1000000;

        /// <summary>Calls each processor among <paramref name="plugins"/> in the given order.</summary>
        internal static void Dispatch(ForgeBuildContext context, IEnumerable<IForgePlugin> plugins, string path)
        {
            foreach (var plugin in plugins)
            {
                if (!(plugin is IForgeGradleProcessor processor))
                    continue;
                Debug.Log($"[Build Forge] Running Gradle step: {plugin.DisplayName}");
                try
                {
                    processor.OnPostGenerateGradleAndroidProject(context, path);
                }
                catch (BuildFailedException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    throw new BuildFailedException($"[Build Forge] The Gradle step of {plugin.DisplayName} failed: {e.Message}");
                }
            }
        }
    }

#if UNITY_ANDROID
    internal sealed class ForgeGradleProjectCallback : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => ForgeGradleCallback.CallbackOrder;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var context = ForgeBuildRunner.CurrentContext;
            if (context != null)
                ForgeGradleCallback.Dispatch(context, ForgeBuildRunner.CurrentPlugins, path);
        }
    }
#endif
}
