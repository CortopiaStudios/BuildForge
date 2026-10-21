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
        public void EnabledByDefault_StartsEnabled()
        {
            Assert.IsFalse(PluginEnableState.IsDisabled(disabled, enabled, "com.test.fake.plugin", enabledByDefault: true));
        }

        [Test]
        public void NotEnabledByDefault_StartsDisabled()
        {
            Assert.IsTrue(PluginEnableState.IsDisabled(disabled, enabled, "com.test.fake.plugin2", enabledByDefault: false));
        }

        [Test]
        public void Set_TogglesState()
        {
            const string plugin = "com.test.toggle.test";

            PluginEnableState.Set(disabled, enabled, plugin, isDisabled: true);
            Assert.IsTrue(PluginEnableState.IsDisabled(disabled, enabled, plugin, enabledByDefault: true));

            PluginEnableState.Set(disabled, enabled, plugin, isDisabled: false);
            Assert.IsFalse(PluginEnableState.IsDisabled(disabled, enabled, plugin, enabledByDefault: false));
        }

        [Test]
        public void ExplicitEnable_OverridesDisabledByDefault()
        {
            const string plugin = "com.test.explicit.enable";

            PluginEnableState.Set(disabled, enabled, plugin, isDisabled: false);
            Assert.IsFalse(PluginEnableState.IsDisabled(disabled, enabled, plugin, enabledByDefault: false));
        }

        [Test]
        public void ExplicitDisable_OverridesEnabledByDefault()
        {
            const string plugin = "com.test.explicit.disable";

            PluginEnableState.Set(disabled, enabled, plugin, isDisabled: true);
            Assert.IsTrue(PluginEnableState.IsDisabled(disabled, enabled, plugin, enabledByDefault: true));
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

        [Test]
        public void Stale_ListsEntriesOfPluginsThatAreNotInstalled_WithTheirState()
        {
            disabled.AddRange(new[] { "com.test.Renamed", "com.test.InstalledOff" });
            enabled.AddRange(new[] { "com.test.Uninstalled", "com.test.InstalledOn" });

            var stale = PluginEnableState.Stale(disabled, enabled, new[] { "com.test.InstalledOff", "com.test.InstalledOn" });

            CollectionAssert.AreEqual(
                new[] { ("com.test.Renamed", true), ("com.test.Uninstalled", false) },
                stale);
        }

        [Test]
        public void Stale_AllInstalled_IsEmpty()
        {
            disabled.Add("com.test.Off");
            enabled.Add("com.test.On");

            Assert.IsEmpty(PluginEnableState.Stale(disabled, enabled, new[] { "com.test.On", "com.test.Off" }));
        }

        [Test]
        public void Stale_NameInBothListsOrTwice_IsListedOnceAsOff()
        {
            // As IsDisabled decides: the disabled list wins.
            disabled.AddRange(new[] { "com.test.Gone", "com.test.Gone" });
            enabled.Add("com.test.Gone");

            CollectionAssert.AreEqual(
                new[] { ("com.test.Gone", true) },
                PluginEnableState.Stale(disabled, enabled, new string[0]));
        }

        [Test]
        public void Stale_IsSortedByName()
        {
            disabled.AddRange(new[] { "com.test.C", "com.test.A" });
            enabled.Add("com.test.B");

            CollectionAssert.AreEqual(
                new[] { ("com.test.A", true), ("com.test.B", false), ("com.test.C", true) },
                PluginEnableState.Stale(disabled, enabled, new string[0]));
        }

        [Test]
        public void Remove_ClearsEveryEntryOfTheName_AndKeepsTheOthers()
        {
            disabled.AddRange(new[] { "com.test.Gone", "com.test.Kept", "com.test.Gone" });
            enabled.AddRange(new[] { "com.test.Gone", "com.test.KeptOn" });

            PluginEnableState.Remove(disabled, enabled, "com.test.Gone");

            CollectionAssert.AreEqual(new[] { "com.test.Kept" }, disabled);
            CollectionAssert.AreEqual(new[] { "com.test.KeptOn" }, enabled);
            // Back to its default, whichever that is.
            Assert.IsFalse(PluginEnableState.IsDisabled(disabled, enabled, "com.test.Gone", enabledByDefault: true));
            Assert.IsTrue(PluginEnableState.IsDisabled(disabled, enabled, "com.test.Gone", enabledByDefault: false));
        }
    }
}
