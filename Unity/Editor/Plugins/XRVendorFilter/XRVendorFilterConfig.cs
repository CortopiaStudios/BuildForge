using System;
using System.Collections.Generic;

namespace BuildForge.Editor.Plugins
{
    /// <summary>Per-profile settings of <see cref="XRVendorFilterPlugin"/>.</summary>
    [Serializable]
    internal class XRVendorFilterConfig
    {
        public bool enabled;

        /// <summary>Packages whose Android libraries stay out of this profile's builds.</summary>
        public List<string> excludedPackages = new();

        /// <summary>
        /// Manifest entry name prefixes to remove besides the known vendors'
        /// (for packages Build Forge has no rules for), such as "com.example.".
        /// </summary>
        public List<string> extraManifestPrefixes = new();

        /// <summary>Fail the build when an excluded library or a vendor manifest entry is still in the Gradle project.</summary>
        public bool checkLeftovers = true;
    }
}
