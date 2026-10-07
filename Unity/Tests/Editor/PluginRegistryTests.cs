using BuildForge.Editor.Core;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class PluginRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            ForgePluginRegistry.ClearCache();
        }

        [Test]
        public void GetPlugins_ReturnsNonNullList()
        {
            var plugins = ForgePluginRegistry.GetPlugins();
            Assert.IsNotNull(plugins);
        }

        [Test]
        public void GetPlugins_ReturnsSameInstance_WhenCalledTwice()
        {
            var first = ForgePluginRegistry.GetPlugins();
            var second = ForgePluginRegistry.GetPlugins();
            Assert.AreSame(first, second);
        }

        [Test]
        public void GetPlugins_ReturnsNewInstance_AfterClearCache()
        {
            var first = ForgePluginRegistry.GetPlugins();
            ForgePluginRegistry.ClearCache();
            var second = ForgePluginRegistry.GetPlugins();
            Assert.AreNotSame(first, second);
        }

        [Test]
        public void XRSettingsResolveBeforeContentGeneration()
        {
            // Only assert edges whose optional packages are installed. The full
            // host exercises all three edges; a non-XR project needs no loaders.
            var plugins = ForgePluginRegistry.GetPlugins();
            foreach (var (earlier, later) in new[] {
                ("XR Loaders", "OpenXR"),
                ("OpenXR", "Addressables Stripper"),
                ("OpenXR", "Addressables Rebuild") })
            {
                var first = System.Linq.Enumerable.FirstOrDefault(plugins, p => p.DisplayName == earlier);
                var second = System.Linq.Enumerable.FirstOrDefault(plugins, p => p.DisplayName == later);
                if (first != null && second != null)
                    Assert.Less(first.Order, second.Order, $"{earlier} must finish before {later} observes settings.");
            }
        }

        [Test]
        public void GetPlugins_AreSortedByOrder()
        {
            var plugins = ForgePluginRegistry.GetPlugins();
            for (int i = 1; i < plugins.Count; i++)
            {
                Assert.LessOrEqual(plugins[i - 1].Order, plugins[i].Order,
                    $"Plugin {plugins[i - 1].DisplayName} (order {plugins[i - 1].Order}) " +
                    $"should come before {plugins[i].DisplayName} (order {plugins[i].Order})");
            }
        }
    }
}
