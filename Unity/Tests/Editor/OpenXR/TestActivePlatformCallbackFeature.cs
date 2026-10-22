using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace BuildForge.Tests.OpenXR
{
    // In a file of its own, named after it: only then does Unity give the class a script, so the copy
    // OpenXR saves in a test host's settings loads again in the next session instead of turning into
    // a null entry that breaks OpenXR's own feature refresh.
    /// <summary>
    /// A feature whose on/off callback turns on <see cref="TestPrerequisiteFeature"/> for the editor's
    /// active platform rather than for its own, as Meta's eye-tracked foveation does going by its
    /// source. Written for these tests, not copied from a vendor.
    /// </summary>
    [OpenXRFeature(UiName = TestActivePlatformCallbackFeature.Name, Company = "Build Forge tests", Version = "1.0.0",
        FeatureId = TestActivePlatformCallbackFeature.Id, BuildTargetGroups = new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })]
    public sealed class TestActivePlatformCallbackFeature : OpenXRFeature
    {
        public const string Id = "com.cortopiastudios.buildforge.tests.activeplatformcallback";
        public const string Name = "Build Forge Test Callback";

        public static int CallbackCount;

        /// <summary>The platform the callback takes for the editor's active one, as in an editor with another platform active; null for the editor's own.</summary>
        public static BuildTargetGroup? ActivePlatform;

        protected override void OnEnabledChange()
        {
            CallbackCount++;
            if (!enabled) return;
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(
                ActivePlatform ?? BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
            if (settings != null)
                settings.GetFeature<TestPrerequisiteFeature>().enabled = true;
        }
    }
}
