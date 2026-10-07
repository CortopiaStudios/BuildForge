namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Optional plugin capability: a step in the generated Android Gradle
    /// project of a Build Forge build. Unity calls Build Forge's Gradle callback
    /// (IPostGenerateGradleAndroidProject) at callback order 1000000, after the
    /// vendors' own steps (Meta's runs at 99999), and Build Forge calls this on
    /// the build's plugins in plugin order. Only for Android builds that Build
    /// Forge runs, and only for plugins whose OnPreBuild ran.
    /// </summary>
    public interface IForgeGradleProcessor
    {
        /// <param name="context">The build in progress.</param>
        /// <param name="path">The unityLibrary module of the Gradle project, as Unity passes it.</param>
        void OnPostGenerateGradleAndroidProject(ForgeBuildContext context, string path);
    }
}
