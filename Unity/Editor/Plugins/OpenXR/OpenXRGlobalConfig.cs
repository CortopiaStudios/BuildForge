using System;
using UnityEngine;

namespace BuildForge.Editor.OpenXR
{
    [Serializable]
    internal class OpenXRGlobalConfig
    {
        [SerializeField] bool showInteractionProfileOverrides;
        [SerializeField] bool matchPlatformFeaturesInPlayMode;

        public bool ShowInteractionProfileOverrides
        {
            get => showInteractionProfileOverrides;
            set => showInteractionProfileOverrides = value;
        }

        /// <summary>
        /// Play Mode Follows Applied Profile's OpenXR Features: false (the default)
        /// turns on Standalone's features that the applied platform has on and
        /// turns none off; true also turns off those it has off, so Play Mode
        /// matches the platform where Standalone has its features.
        /// </summary>
        public bool MatchPlatformFeaturesInPlayMode
        {
            get => matchPlatformFeaturesInPlayMode;
            set => matchPlatformFeaturesInPlayMode = value;
        }
    }
}
