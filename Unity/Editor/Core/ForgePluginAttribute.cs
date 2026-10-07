using System;

namespace BuildForge.Editor.Core
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ForgePluginAttribute : Attribute
    {
        /// <summary>
        /// When false, the plugin is disabled by default until the user explicitly
        /// enables it in Project Settings. Use for plugins that modify build output
        /// (PlayerSettings, build artifacts) so they don't silently affect builds
        /// on first install.
        /// </summary>
        public bool SafeToDefaultEnable { get; set; } = true;
    }
}
