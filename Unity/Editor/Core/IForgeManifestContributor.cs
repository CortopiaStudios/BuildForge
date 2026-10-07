using BuildForge.Runtime;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Implement this interface on a plugin to contribute data to the build manifest.
    /// </summary>
    public interface IForgeManifestContributor
    {
        /// <summary>
        /// Called during manifest generation to allow plugins to add custom data.
        /// </summary>
        void ContributeToManifest(BuildManifest manifest, ForgeBuildContext context);
    }
}
