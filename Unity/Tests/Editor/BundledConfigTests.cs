using System;
using System.Linq;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// The bundled plugins' settings classes, as Build Profiles and ForgeSettings.asset store
    /// them: stored settings name each class by its namespace, name and assembly, so renaming
    /// or moving one turns the settings of every profile that has them into a missing type,
    /// which stops its builds. And their default values, which a profile that never changed
    /// them follows: a profile stores no output path while it keeps the default, and no plugin
    /// settings equal to a new instance's, so a changed default changes its builds without a
    /// diff in the project.
    /// </summary>
    public class BundledConfigTests
    {
        // The assembly, the class, whether the assembly needs an optional package, and the
        // class's default values as JsonUtility writes them.
        static readonly (string Assembly, string Type, bool Optional, string Defaults)[] Configs =
        {
            ("BuildForge.Editor.AndroidKeystoreEnv", "BuildForge.Editor.Plugins.AndroidKeystoreEnvGlobalConfig", false,
                "{\"keystorePathEnvVar\":\"\",\"keystorePassEnvVar\":\"\",\"keyAliasEnvVar\":\"\",\"keyAliasPassEnvVar\":\"\"}"),
            ("BuildForge.Editor.AndroidKeystoreEnv", "BuildForge.Editor.Plugins.AndroidKeystoreEnvProfileConfig", false,
                "{\"enabled\":false,\"keystorePathMode\":0,\"keystorePathEnvVar\":\"\",\"keystorePassMode\":0,\"keystorePassEnvVar\":\"\"," +
                "\"keyAliasMode\":0,\"keyAliasEnvVar\":\"\",\"keyAliasPassMode\":0,\"keyAliasPassEnvVar\":\"\"}"),
            ("BuildForge.Editor.AndroidManifest", "BuildForge.Editor.Plugins.AndroidManifestConfig", false,
                "{\"mainManifest\":{\"instanceID\":0}}"),
            ("BuildForge.Editor.BuildNumber", "BuildForge.Editor.Plugins.BuildNumberConfig", false,
                "{\"enabled\":false,\"source\":0,\"envVarName\":\"\",\"offset\":0}"),
            ("BuildForge.Editor.CloudDiagnostics", "BuildForge.Editor.Plugins.CloudDiagnosticsConfig", false,
                "{\"enabled\":false,\"crashReportingMode\":1}"),
            ("BuildForge.Editor.GitMetadata", "BuildForge.Editor.Plugins.GitMetadataConfig", false,
                "{\"includeBranch\":false,\"includeCommit\":true,\"includeShortCommit\":true,\"includeDescribe\":false,\"includeExactTag\":false}"),
            ("BuildForge.Editor.XRVendorFilter", "BuildForge.Editor.Plugins.XRVendorFilterConfig", false,
                "{\"enabled\":false,\"excludedPackages\":[],\"extraManifestPrefixes\":[],\"checkLeftovers\":true}"),
            ("BuildForge.Editor.Addressables", "BuildForge.Editor.Addressables.AddressablesRebuildConfig", true,
                "{\"rebuildBeforeBuild\":false,\"cleanBeforeRebuild\":false}"),
            ("BuildForge.Editor.Addressables", "BuildForge.Editor.Addressables.AddressablesStripperConfig", true,
                "{\"excludedGroups\":[],\"excludedLabels\":[],\"variantExclusions\":[]}"),
            ("BuildForge.Editor.OpenXR", "BuildForge.Editor.OpenXR.OpenXRGlobalConfig", true,
                "{\"showInteractionProfileOverrides\":false,\"matchPlatformFeaturesInPlayMode\":false}"),
            ("BuildForge.Editor.OpenXR", "BuildForge.Editor.OpenXR.OpenXRProfileConfig", true,
                "{\"onlyListedFeatures\":false,\"enabledFeatures\":[],\"disabledFeatures\":[],\"settingsOverrides\":[],\"featureOverrides\":[]," +
                "\"metaQuestSetup\":false}"),
            ("BuildForge.Editor.XRManagement", "BuildForge.Editor.XRManagement.XRLoaderSelectionConfig", true,
                "{\"enabled\":false,\"loaders\":[]}"),
        };

        static System.Reflection.Assembly Loaded(string assemblyName)
            => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName);

        // A deliberate rename needs [MovedFrom] on the class, so stored settings find it, and
        // an update here. An optional plugin is checked where its package is installed.
        [Test]
        public void TheBundledSettingsClasses_KeepTheNamesStoredSettingsUse()
        {
            foreach (var (assemblyName, typeName, optional, _) in Configs)
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

        // After 1.0 a default that changes what a build produces, or where it goes, changes only
        // in a major version, or the new behavior is opt-in (see ARCHITECTURE.md). This makes a
        // change deliberate: update the value here with it.
        [Test]
        public void TheDefaultsThatUntouchedProfilesFollow_ChangeOnlyOnPurpose()
        {
            Assert.AreEqual("Builds/{Target}/{ProfileName}/{Variant}/{ProjectName}", ForgeProfile.DefaultOutputPath);

            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                // Without the managed-reference block, whose format is Unity's.
                var json = Regex.Replace(JsonUtility.ToJson(profile), ",\"references\":\\{.*\\}}$", "}");
                Expect("ForgeProfile", "{\"buildProfile\":{\"instanceID\":0},\"outputPath\":\"\",\"removePreviousBuildOutput\":true," +
                    "\"restrictVariants\":false,\"allowedVariants\":[],\"variantRestrictionReason\":\"\",\"pluginConfigs\":[]}", json);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }

            // Project Settings > Build Forge, as a project that never saved its settings has them. A second
            // instance of the singleton, created after the real one, leaves the real one alone; Unity
            // logs that one already exists.
            var settings = ForgeSettings.instance;
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new Regex("ScriptableSingleton already exists"));
            var fresh = ScriptableObject.CreateInstance<ForgeSettings>();
            try
            {
                var json = Regex.Replace(JsonUtility.ToJson(fresh), ",\"references\":\\{.*\\}}$", "}");
                Expect("ForgeSettings", "{\"interceptBuilds\":true,\"writeBuildManifest\":true,\"logManifestContents\":false," +
                    "\"mangleProductName\":true,\"revealBuildInFileBrowser\":true,\"saveProfilesOnEdit\":true," +
                    "\"maintainBuildProfileDefines\":false,\"playModeFollowsAppliedProfile\":false,\"buildVariants\":[]," +
                    "\"variantRules\":[],\"markVariantBuilds\":true,\"disabledPlugins\":[],\"enabledPlugins\":[],\"globalPluginConfigs\":[]}", json);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fresh);
            }
            Assert.AreSame(settings, ForgeSettings.instance);

            Expect("BuildVariantRule", "{\"variant\":\"\",\"developmentBuild\":0,\"markProductName\":0,\"scriptingDefines\":[],\"version\":\"\"," +
                "\"buildConfiguration\":{\"autoconnectProfiler\":0,\"deepProfilingSupport\":0,\"scriptDebugging\":0,\"waitForManagedDebugger\":0," +
                "\"cppCompilerConfiguration\":0,\"il2CppCodeGeneration\":0,\"il2CppStacktraceInformation\":0,\"managedStrippingLevel\":0," +
                "\"stripEngineCode\":0,\"compression\":0,\"linkTimeOptimization\":0,\"debugSymbols\":0}}",
                JsonUtility.ToJson(new BuildVariantRule()));

            foreach (var (assemblyName, typeName, _, defaults) in Configs)
            {
                var type = Loaded(assemblyName)?.GetType(typeName);
                if (type != null)
                    Expect(typeName, defaults, JsonUtility.ToJson(Activator.CreateInstance(type, true)));
            }
        }

        // The whole value in the message, to copy into the list when the change is deliberate.
        static void Expect(string what, string expected, string actual)
            => Assert.IsTrue(expected == actual, $"The default values of {what} changed. Now: {actual}");
    }
}
