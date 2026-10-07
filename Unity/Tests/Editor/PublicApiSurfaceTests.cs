using System;
using System.Linq;
using System.Reflection;
using BuildForge.Editor.Core;
using BuildForge.Runtime;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// Locks the 1.0 public API surface (see ARCHITECTURE.md "Public API
    /// Surface"). Exposing a type later is additive and non-breaking; hiding one
    /// after release is breaking — so any change to these lists must be a
    /// deliberate decision, not a side effect.
    /// </summary>
    public class PublicApiSurfaceTests
    {
        static readonly string[] ExpectedEditorPublicTypes =
        {
            "BuildForge.CommandLine",
            "BuildForge.Editor.Configuration.ForgeProfile",
            "BuildForge.Editor.Configuration.ForgeSettings",
            "BuildForge.Editor.Core.ForgeBuildContext",
            "BuildForge.Editor.Core.ForgeBuildRunner",
            "BuildForge.Editor.Core.ForgePluginAttribute",
            "BuildForge.Editor.Core.IForgeEditorApplicable",
            "BuildForge.Editor.Core.IForgeGradleProcessor",
            "BuildForge.Editor.Core.IForgeManifestContributor",
            "BuildForge.Editor.Core.IForgePlugin",
        };

        static readonly string[] ExpectedRuntimePublicTypes =
        {
            "BuildForge.Runtime.BuildManifest",
            "BuildForge.Runtime.BuildManifestLoader",
            "BuildForge.Runtime.ManifestEntry",
            "BuildForge.Runtime.ManifestSection",
        };

        static string[] PublicTypesOf(Assembly assembly) =>
            assembly.GetExportedTypes().Select(t => t.FullName).OrderBy(n => n).ToArray();

        [Test]
        public void EditorAssembly_PublicSurface_IsExactlyTheApprovedList()
        {
            CollectionAssert.AreEqual(
                ExpectedEditorPublicTypes.OrderBy(n => n).ToArray(),
                PublicTypesOf(typeof(ForgeBuildRunner).Assembly));
        }

        [Test]
        public void RuntimeAssembly_PublicSurface_IsExactlyTheApprovedList()
        {
            CollectionAssert.AreEqual(
                ExpectedRuntimePublicTypes.OrderBy(n => n).ToArray(),
                PublicTypesOf(typeof(BuildManifest).Assembly));
        }

        [Test]
        public void BundledPluginAssemblies_ExposeNoPublicTypes()
        {
            // The OpenXR/Addressables/XR Management assemblies only exist when
            // their Unity package is installed; assert emptiness for whichever
            // are loaded.
            var pluginAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a =>
                {
                    var name = a.GetName().Name;
                    return name == "BuildForge.Editor.OpenXR"
                        || name == "BuildForge.Editor.Addressables"
                        || name == "BuildForge.Editor.XRManagement";
                });

            foreach (var assembly in pluginAssemblies)
            {
                CollectionAssert.IsEmpty(PublicTypesOf(assembly),
                    $"{assembly.GetName().Name} must not expose public types.");
            }
        }

        [Test]
        public void ForgeBuildContext_HasNoPublicConstructor()
        {
            // Plugins receive contexts from the runner; they must not be able to
            // fabricate them.
            CollectionAssert.IsEmpty(
                typeof(ForgeBuildContext).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }
    }
}
