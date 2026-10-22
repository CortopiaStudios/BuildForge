using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features;

namespace BuildForge.Tests.OpenXR
{
    // In a file of its own, named after it: only then does Unity give the class a script, so the copy
    // OpenXR saves in a test host's settings loads again in the next session instead of turning into
    // a null entry that breaks OpenXR's own feature refresh.
    /// <summary>The feature <see cref="TestActivePlatformCallbackFeature"/> turns on, with a nested field named like OpenXRFeature's own on/off field.</summary>
    [OpenXRFeature(UiName = TestPrerequisiteFeature.Name, Company = "Build Forge tests", Version = "1.0.0",
        FeatureId = TestPrerequisiteFeature.Id, BuildTargetGroups = new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })]
    public sealed class TestPrerequisiteFeature : OpenXRFeature
    {
        public const string Id = "com.cortopiastudios.buildforge.tests.prerequisite";
        public const string Name = "Build Forge Test Prerequisite";

        [System.Serializable] public sealed class NestedSettings { public bool m_enabled = true; }
        public NestedSettings nested = new NestedSettings();
    }
}
