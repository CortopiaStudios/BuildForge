using System;
using UnityEngine;

namespace BuildForge.Editor.Plugins
{
    /// <summary>Per-profile settings of <see cref="AndroidManifestPlugin"/>.</summary>
    [Serializable]
    internal class AndroidManifestConfig
    {
        /// <summary>The profile's own main manifest; none builds with the project's.</summary>
        public TextAsset mainManifest;
    }
}
