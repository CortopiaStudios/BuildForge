using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace BuildForge.Editor.OpenXR
{
    /// <summary>
    /// Play Mode runs on Standalone's OpenXR settings. Turns on each OpenXR
    /// feature of Standalone that the applied profile's platform has on,
    /// interaction profiles included. By default it turns none off, so
    /// Standalone keeps the features it has on itself; with OpenXR Features set
    /// to Match the platform's, it turns off those the platform has off too.
    /// </summary>
    internal sealed class PlayModeOpenXRFeatures : IForgePlayModeMirror
    {
        static readonly GUIContent[] FeatureModes =
        {
            new GUIContent("Turn on the platform's"),
            new GUIContent("Match the platform's"),
        };

        public string DisplayName => "Play Mode OpenXR features";

        public bool CanMirror(BuildTargetGroup source)
            => OpenXRSettings.GetSettingsForBuildTargetGroup(source) != null
               && OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone) != null;

        // Interaction profiles included: Play Mode needs the controllers.
        public string CaptureEditorState(BuildTargetGroup group)
            => OpenXRSettingsState.Capture(group, includeInteractionProfiles: true)?.ToJson();

        public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group, string variant)
        {
            var source = BuildPipeline.GetBuildTargetGroup(Core.BuildProfileUtility.GetBuildTarget(forgeProfile.BuildProfile));
            var match = OpenXRPlugin.GetGlobalConfig().MatchPlatformFeaturesInPlayMode;
            // Only Match turns features off, which a ticked group can forbid; before anything is written.
            var conflict = match ? OpenXRPlugin.MatchConflictError(source, group, () => OpenXRPlugin.TickedFeatureGroups(group)) : null;
            if (conflict != null)
                throw new System.InvalidOperationException("[Build Forge/OpenXR] " + conflict);
            var written = OpenXRSettingsState.CopyFeatureStates(source, group, match);
            // A state OpenXR refused stops the Apply, which puts back what it wrote, instead of being
            // recorded as applied.
            OpenXRPlugin.VerifyFeatureStates(written.Keys, feature => written[feature], group,
                (feature, groups) => $"OpenXR feature '{feature.GetType().FullName}' did not take the state Play Mode Follows " +
                                     $"Applied Profile copied from {source}.");
        }

        public void RestoreEditorState(BuildTargetGroup group, string json)
            => OpenXRSettingsState.Restore(group, OpenXREditorStateSnapshot.FromJson(json));

        public string MergeEditorState(BuildTargetGroup group, string baselineJson, string expectedJson, string currentJson)
            => OpenXRSettingsState.Merge(baselineJson, expectedJson, currentJson);

        public void OnDrawPlayModeSettingsGUI()
        {
            var config = OpenXRPlugin.GetGlobalConfig();
            var mode = EditorGUILayout.Popup(new GUIContent("OpenXR Features",
                    "What applying a profile of another platform does to Standalone's OpenXR features. Turn on the " +
                    "platform's: the features the platform has on come on, and Standalone keeps the ones it has on " +
                    "itself, such as hand tracking for a tool that runs in Play Mode. Match the platform's: the features " +
                    "it has off go off too, so Play Mode has exactly the platform's features where Standalone has them."),
                config.MatchPlatformFeaturesInPlayMode ? 1 : 0, FeatureModes);
            if (mode == (config.MatchPlatformFeaturesInPlayMode ? 1 : 0))
                return;
            config.MatchPlatformFeaturesInPlayMode = mode == 1;
            OpenXRPlugin.SaveGlobalConfig(config);
        }
    }
}
