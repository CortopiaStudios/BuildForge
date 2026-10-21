using System.Linq;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// A plugin's switch in Project Settings > Build Forge > Installed Plugins is stored under
    /// its full type name, so renaming a bundled plugin's class or namespace would reset the
    /// switch in every project that set it. These names are part of the settings format.
    /// </summary>
    public class BundledPluginNamesTests
    {
        static readonly string[] AlwaysInstalled =
        {
            "BuildForge.Editor.Plugins.AndroidKeystoreEnvPlugin",
            "BuildForge.Editor.Plugins.AndroidManifestPlugin",
            "BuildForge.Editor.Plugins.BuildNumberPlugin",
            "BuildForge.Editor.Plugins.CloudDiagnosticsPlugin",
            "BuildForge.Editor.Plugins.GitMetadataPlugin",
            "BuildForge.Editor.Plugins.XRVendorFilterPlugin",
        };

        // Compiled only while their package is installed.
        static readonly string[] WithTheirPackage =
        {
            "BuildForge.Editor.Addressables.AddressablesRebuildPlugin",
            "BuildForge.Editor.Addressables.AddressablesStripperPlugin",
            "BuildForge.Editor.OpenXR.OpenXRPlugin",
            "BuildForge.Editor.XRManagement.XRLoaderSelectionPlugin",
        };

        [Test]
        public void TheBundledPlugins_KeepTheirNames()
        {
            var bundled = TypeCache.GetTypesWithAttribute<ForgePluginAttribute>()
                .Where(t => t.Assembly.GetName().Name.StartsWith("BuildForge.Editor"))
                .Select(t => t.FullName)
                .ToList();

            CollectionAssert.IsSubsetOf(AlwaysInstalled, bundled);
            CollectionAssert.IsSubsetOf(bundled, AlwaysInstalled.Concat(WithTheirPackage).ToList(),
                "A bundled plugin was renamed or added: its name keys its switch in projects' Build Forge settings.");
        }

        [Test]
        public void TheBuildNumberPlugin_IsFoundByTheNameTheCoreUses()
            => Assert.IsNotNull(ForgePluginRegistry.GetPlugins().FirstOrDefault(p => p.GetType().FullName == ForgeBuildRunner.BuildNumberPluginName));
    }
}
