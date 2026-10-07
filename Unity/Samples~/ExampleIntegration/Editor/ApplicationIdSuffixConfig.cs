using System;
using System.Collections.Generic;

namespace BuildForge.Samples.ExampleIntegration
{
    /// <summary>
    /// Per-profile settings of <see cref="ApplicationIdSuffixPlugin"/>. Build Forge
    /// stores one instance in each Build Profile that uses the plugin, as a managed
    /// reference, so every field is one readable line in the Build Profile asset.
    /// Renaming this class, its namespace or its assembly makes stored settings an
    /// unknown type; use Unity's [MovedFrom] attribute when you rename it.
    /// </summary>
    [Serializable]
    public sealed class ApplicationIdSuffixConfig
    {
        /// <summary>The plugin's per-profile switch. Off by default, so adding the plugin changes no build.</summary>
        public bool enabled;

        /// <summary>Appended to the Android application identifier, for example ".internal".</summary>
        public string suffix = ".internal";

        /// <summary>The variants whose builds get the suffix. The default build never does.</summary>
        public List<string> variants = new List<string>();
    }
}
