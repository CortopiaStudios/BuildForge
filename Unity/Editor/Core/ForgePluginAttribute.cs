using System;

namespace BuildForge.Editor.Core
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ForgePluginAttribute : Attribute
    {
        /// <summary>
        /// Whether the plugin starts enabled in Project Settings &gt; Build Forge &gt;
        /// Installed Plugins, where a project can turn it off; true by default. A
        /// plugin must not change any build until a profile turns it on or
        /// configures it. Set this to false for a plugin that can't follow that
        /// rule, so a project enables it knowingly.
        /// </summary>
        public bool EnabledByDefault { get; set; } = true;
    }
}
