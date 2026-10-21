using System;
using System.Linq;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// The bundled plugins' settings classes, as Build Profiles and ForgeSettings.asset store
    /// them: stored settings name each class by its namespace, name and assembly, so renaming
    /// or moving one turns the settings of every profile that has them into a missing type,
    /// which stops its builds.
    /// </summary>
    public class BundledConfigTests
    {
        // The assembly, the class, and whether the assembly needs an optional package.
        static readonly (string Assembly, string Type, bool Optional)[] Configs =
        {
            ("BuildForge.Editor.AndroidKeystoreEnv", "BuildForge.Editor.Plugins.AndroidKeystoreEnvGlobalConfig", false),
            ("BuildForge.Editor.AndroidKeystoreEnv", "BuildForge.Editor.Plugins.AndroidKeystoreEnvProfileConfig", false),
            ("BuildForge.Editor.AndroidManifest", "BuildForge.Editor.Plugins.AndroidManifestConfig", false),
            ("BuildForge.Editor.BuildNumber", "BuildForge.Editor.Plugins.BuildNumberConfig", false),
            ("BuildForge.Editor.CloudDiagnostics", "BuildForge.Editor.Plugins.CloudDiagnosticsConfig", false),
            ("BuildForge.Editor.GitMetadata", "BuildForge.Editor.Plugins.GitMetadataConfig", false),
            ("BuildForge.Editor.XRVendorFilter", "BuildForge.Editor.Plugins.XRVendorFilterConfig", false),
            ("BuildForge.Editor.Addressables", "BuildForge.Editor.Addressables.AddressablesRebuildConfig", true),
            ("BuildForge.Editor.Addressables", "BuildForge.Editor.Addressables.AddressablesStripperConfig", true),
            ("BuildForge.Editor.OpenXR", "BuildForge.Editor.OpenXR.OpenXRGlobalConfig", true),
            ("BuildForge.Editor.OpenXR", "BuildForge.Editor.OpenXR.OpenXRProfileConfig", true),
            ("BuildForge.Editor.XRManagement", "BuildForge.Editor.XRManagement.XRLoaderSelectionConfig", true),
        };

        static System.Reflection.Assembly Loaded(string assemblyName)
            => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName);

        // A deliberate rename needs [MovedFrom] on the class, so stored settings find it, and
        // an update here. An optional plugin is checked where its package is installed.
        [Test]
        public void TheBundledSettingsClasses_KeepTheNamesStoredSettingsUse()
        {
            foreach (var (assemblyName, typeName, optional) in Configs)
            {
                var assembly = Loaded(assemblyName);
                if (assembly == null && optional)
                    continue;
                Assert.IsNotNull(assembly, $"The bundled plugin assembly {assemblyName} is not loaded.");
                var type = assembly.GetType(typeName);
                Assert.IsNotNull(type, $"{typeName} is no longer in {assemblyName}: profiles that store its settings would stop building.");
                Assert.IsTrue(type.IsSerializable, $"{typeName} is no longer [Serializable].");
            }
        }

        // A settings class added to a bundled plugin is listed here too, so it is covered.
        [Test]
        public void EveryBundledSettingsClass_IsListed()
        {
            var listed = Configs.Select(c => c.Type).ToList();
            var found = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name.StartsWith("BuildForge.Editor.", StringComparison.Ordinal))
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && t.IsSerializable && t.Name.EndsWith("Config", StringComparison.Ordinal))
                .Select(t => t.FullName);
            CollectionAssert.IsEmpty(found.Except(listed), "Bundled settings classes missing from this list.");
        }
    }
}
