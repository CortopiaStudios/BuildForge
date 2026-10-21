using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
            public bool IsApplicable(UnityEditor.Build.Profile.BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        sealed class Alpha : IForgePlugin
        {
            public string DisplayName => "Alpha";
            public int Order => 650;
            public bool IsApplicable(UnityEditor.Build.Profile.BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        // Without [ForgePlugin], like Zeta and Alpha, so discovery outside these tests doesn't find it.
        sealed class Omega : IForgePlugin
        {
            public string DisplayName => "Omega";
            public int Order => 100;
            public bool IsApplicable(UnityEditor.Build.Profile.BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        sealed class ThrowingOrder : IForgePlugin
        {
            public string DisplayName => "Throwing Order";
            public int Order => throw new InvalidOperationException("no order");
            public bool IsApplicable(UnityEditor.Build.Profile.BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        // Two plugins with the same Order run in the order of their type names, whatever
        // order discovery found them in.
        [Test]
        public void PluginsWithTheSameOrder_RunByTypeName()
        {
            var failures = new List<(Type, string)>();
            CollectionAssert.AreEqual(new[] { typeof(Alpha), typeof(Zeta) },
                ForgePluginRegistry.Discover(new[] { typeof(Zeta), typeof(Alpha) }, failures).Select(p => p.GetType()));
            CollectionAssert.AreEqual(new[] { typeof(Alpha), typeof(Zeta) },
                ForgePluginRegistry.Discover(new[] { typeof(Alpha), typeof(Zeta) }, failures).Select(p => p.GetType()));
            Assert.IsEmpty(failures);
        }

        // A plugin whose Order throws has no place in the order: it fails to load, which stops
        // builds and Apply while it is on, and the others are still sorted. It used to leave the
        // list unsorted, and builds ran the plugins in discovery order without a word.
        [Test]
        public void APluginWhoseOrderThrows_FailsToLoad_AndTheOthersAreSorted()
        {
            var failures = new List<(Type Type, string Error)>();
            LogAssert.Expect(LogType.Error, new Regex(@"Failed to load plugin .*ThrowingOrder: Order threw: no order"));

            var plugins = ForgePluginRegistry.Discover(new[] { typeof(Zeta), typeof(ThrowingOrder), typeof(Omega), typeof(Alpha) }, failures);

            CollectionAssert.AreEqual(new[] { typeof(Omega), typeof(Alpha), typeof(Zeta) }, plugins.Select(p => p.GetType()));
            Assert.AreEqual(typeof(ThrowingOrder), failures.Single().Type);
            Assert.AreEqual("Order threw: no order", failures.Single().Error);
            StringAssert.Contains("Order threw: no order", ForgePluginRegistry.LoadError(failures, _ => false));
        }
    }
}
