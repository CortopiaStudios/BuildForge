using UnityEditor;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Optional capability for plugins whose per-profile overrides are worth
    /// applying to the editor persistently, so a developer can iterate in Play
    /// Mode against a profile's configuration (e.g. OpenXR features). Build-only
    /// plugins (build numbers, signing, symbol upload) must not implement this.
    ///
    /// Contract:
    /// - Use build-target-group-explicit APIs (e.g.
    ///   <c>OpenXRSettings.GetSettingsForBuildTargetGroup</c>,
    ///   <c>PlayerSettings.GetX(NamedBuildTarget)</c>), never active-profile
    ///   statics: Build Forge activates the Unity Build Profile only after these
    ///   calls, so the static PlayerSettings API may still point at another profile.
    /// - Flush changes to disk (<c>AssetDatabase.SaveAssets</c>) before returning.
    /// - Snapshots must be <c>JsonUtility</c>-compatible and deterministic (sort
    ///   lists): Build Forge detects drift by comparing snapshot strings.
    /// - Do not keep references to the profile between calls.
    /// </summary>
    public interface IForgeEditorApplicable
    {
        /// <summary>
        /// Serializes the current global state of everything this plugin may
        /// write for the given build target group. Return null when there is
        /// nothing to manage for that group (the plugin is then skipped).
        /// </summary>
        string CaptureEditorState(BuildTargetGroup group);

        /// <summary>
        /// Writes the profile's overrides for the given build target group into
        /// the global project settings and saves them.
        /// </summary>
        void ApplyToEditor(Configuration.ForgeProfile forgeProfile, BuildTargetGroup group);

        /// <summary>Applies settings for the requested variant without depending on editor compilation defines.</summary>
        void ApplyToEditor(Configuration.ForgeProfile forgeProfile, BuildTargetGroup group, string variant)
            => ApplyToEditor(forgeProfile, group);

        /// <summary>
        /// Writes a snapshot previously returned by <see cref="CaptureEditorState"/>
        /// back into the global project settings and saves them.
        /// </summary>
        void RestoreEditorState(BuildTargetGroup group, string json);
    }
}
