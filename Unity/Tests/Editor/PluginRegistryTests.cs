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

        sealed class Zeta : IForgePlugin
        {
            public string DisplayName => "Zeta";
            public int Order => 650;
            public bool IsApplicable(UnityEditor.Build.Profile.BuildProfile profile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        sealed class Alpha : IForgePlugin
        {
            public string DisplayName => "Alpha";
            public int Order => 650;
            public bool IsApplicable(UnityEditor.Build.Profile.BuildProfile profile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        // Two plugins with the same Order run in the order of their type names, whatever
        // order discovery found them in.
        [Test]
        public void PluginsWithTheSameOrder_RunByTypeName()
        {
            var zeta = new Zeta();
            var alpha = new Alpha();
            CollectionAssert.AreEqual(new IForgePlugin[] { alpha, zeta }, ForgePluginRegistry.InPluginOrder(new IForgePlugin[] { zeta, alpha }));
            CollectionAssert.AreEqual(new IForgePlugin[] { alpha, zeta }, ForgePluginRegistry.InPluginOrder(new IForgePlugin[] { alpha, zeta }));
        }
    }
}
