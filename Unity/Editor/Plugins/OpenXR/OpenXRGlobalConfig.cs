using System;
using UnityEngine;

namespace BuildForge.Editor.OpenXR
{
    [Serializable]
    internal class OpenXRGlobalConfig
    {
        [SerializeField] bool showInteractionProfileOverrides;

        public bool ShowInteractionProfileOverrides
        {
            get => showInteractionProfileOverrides;
            set => showInteractionProfileOverrides = value;
        }
    }
}
