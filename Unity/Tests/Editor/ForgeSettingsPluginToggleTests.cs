using System.Collections.Generic;
using BuildForge.Editor.Configuration;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    // Exercises the pure disabled/enabled resolution directly, so nothing touches
    // the ForgeSettings singleton or writes to the host project's
    // ProjectSettings/ForgeSettings.asset.
    public class ForgeSettingsPluginToggleTests
    {
        List<string> disabled;
        List<string> enabled;

        [SetUp]
        public void SetUp()
        {
            disabled = new List<string>();
            enabled = new List<string>();
        }

        [Test]
        public void SafeToEnable_DefaultsEnabled()
        {
            Assert.IsFalse(PluginEnableState.IsDisabled(disabled, enabled, "com.test.fake.plugin", safeToDefaultEnable: true));
        }

        [Test]
        public void NotSafeToEnable_DefaultsDisabled()
        {
            Assert.IsTrue(PluginEnableState.IsDisabled(disabled, enabled, "com.test.fake.plugin2", safeToDefaultEnable: false));
        }

        [Test]
        public void Set_TogglesState()
        {
            const string plugin = "com.test.toggle.test";

            PluginEnableState.Set(disabled, enabled, plugin, isDisabled: true);
            Assert.IsTrue(PluginEnableState.IsDisabled(disabled, enabled, plugin, safeToDefaultEnable: true));

            PluginEnableState.Set(disabled, enabled, plugin, isDisabled: false);
            Assert.IsFalse(PluginEnableState.IsDisabled(disabled, enabled, plugin, safeToDefaultEnable: false));
        }

        [Test]
        public void ExplicitEnable_OverridesUnsafeDefault()
        {
            const string plugin = "com.test.explicit.enable";

            PluginEnableState.Set(disabled, enabled, plugin, isDisabled: false);
            Assert.IsFalse(PluginEnableState.IsDisabled(disabled, enabled, plugin, safeToDefaultEnable: false));
        }

        [Test]
        public void ExplicitDisable_OverridesSafeDefault()
        {
            const string plugin = "com.test.explicit.disable";

            PluginEnableState.Set(disabled, enabled, plugin, isDisabled: true);
            Assert.IsTrue(PluginEnableState.IsDisabled(disabled, enabled, plugin, safeToDefaultEnable: true));
        }

        [Test]
        public void Set_MovesBetweenLists_NoDuplicates()
        {
            const string plugin = "com.test.move";

            PluginEnableState.Set(disabled, enabled, plugin, isDisabled: true);
            PluginEnableState.Set(disabled, enabled, plugin, isDisabled: false);

            // Toggling must not leave stale entries in the opposite list.
            Assert.IsFalse(disabled.Contains(plugin));
            Assert.AreEqual(1, enabled.FindAll(p => p == plugin).Count);
        }
    }
}
