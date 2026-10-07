using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEngine.XR.OpenXR;

namespace BuildForge.Editor.OpenXR
{
    /// <summary>
    /// Play Mode runs on Standalone's OpenXR settings. Gives each OpenXR feature
    /// that Standalone shares with the applied profile's platform that
    /// platform's on/off state, interaction profiles included.
    /// </summary>
    internal sealed class PlayModeOpenXRFeatures : IForgePlayModeMirror
    {
        public string DisplayName => "Play Mode OpenXR features";

        public bool CanMirror(BuildTargetGroup source)
            => OpenXRSettings.GetSettingsForBuildTargetGroup(source) != null
               && OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone) != null;

        // Interaction profiles included: Play Mode needs the controllers.
        public string CaptureEditorState(BuildTargetGroup group)
            => OpenXRSettingsState.Capture(group, includeInteractionProfiles: true)?.ToJson();

        public void ApplyToEditor(ForgeProfile profile, BuildTargetGroup group)
            => OpenXRSettingsState.MirrorFeatureStates(
                BuildPipeline.GetBuildTargetGroup(Core.BuildProfileUtility.GetBuildTarget(profile.BuildProfile)), group);

        public void RestoreEditorState(BuildTargetGroup group, string json)
            => OpenXRSettingsState.Restore(group, OpenXREditorStateSnapshot.FromJson(json));
    }
}
