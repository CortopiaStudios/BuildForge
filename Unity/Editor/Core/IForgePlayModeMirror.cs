using UnityEditor;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Gives Play Mode the XR state of an applied profile's platform. Play Mode
    /// always runs on Standalone's XR settings, so applying a profile for
    /// another platform, such as Android, changes nothing there by itself.
    ///
    /// With Project Settings &gt; Build Forge &gt; Play Mode Follows Applied
    /// Profile on, Apply adds one Standalone entry per mirror after the
    /// profile's own plugins, so the mirror copies the platform's state as the
    /// plugins left it. <c>ApplyToEditor</c> receives Standalone as the group and
    /// copies from the profile's platform; capture and restore work on
    /// Standalone. Revert to Baseline, drift and builds treat the entries like
    /// any plugin's.
    /// </summary>
    internal interface IForgePlayModeMirror : IForgeEditorApplicable
    {
        /// <summary>A short name for where the applied state is listed, such as drift.</summary>
        string DisplayName { get; }

        /// <summary>True when both the source platform and Standalone have settings to copy.</summary>
        bool CanMirror(BuildTargetGroup source);
    }
}
