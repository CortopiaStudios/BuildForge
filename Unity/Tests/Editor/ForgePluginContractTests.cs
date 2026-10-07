using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.Plugins;
using NUnit.Framework;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// Locks the contracts of IForgePlugin's default interface methods:
    /// IsEnabled defaults to null ("no on/off switch" — never rendered as a
    /// checkbox, never skipped, never "(disabled)"), NotApplicableReason
    /// defaults to null (generic inspector message). Plugins with a switch
    /// report it via IsEnabled and persist it via SetEnabled.
    /// </summary>
    public class ForgePluginContractTests
    {
        ForgeProfile profile;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<ForgeProfile>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(profile);
        }

        class MinimalPlugin : IForgePlugin
        {
            public string DisplayName => "Minimal";
            public bool IsApplicable(UnityEditor.Build.Profile.BuildProfile p) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        [Test]
        public void IsEnabled_WithoutOverride_DefaultsToNull()
        {
            IForgePlugin plugin = new MinimalPlugin();
            Assert.IsNull(plugin.IsEnabled(profile));
        }

        [Test]
        public void DescribeBuild_WithoutOverride_DefaultsToNull()
        {
            IForgePlugin plugin = new MinimalPlugin();
            Assert.IsNull(plugin.DescribeBuild(profile, isCI: false));
        }

        [Test]
        public void DescribeBuild_CloudDiagnostics_SaysWhatTheBuildGets()
        {
            IForgePlugin plugin = new CloudDiagnosticsPlugin();
            profile.SetPluginConfig("CloudDiagnostics",
                new CloudDiagnosticsConfig { Enabled = true, CrashReportingMode = CrashReportingMode.CIOnly });

            StringAssert.Contains("crash reporting off", plugin.DescribeBuild(profile, isCI: false));
            StringAssert.Contains("local build", plugin.DescribeBuild(profile, isCI: false));
            StringAssert.Contains("crash reporting on", plugin.DescribeBuild(profile, isCI: true));
            StringAssert.Contains("CI build", plugin.DescribeBuild(profile, isCI: true));
        }

        [Test]
        public void DescribeBuild_BuildNumber_NamesSourceAndOffset()
        {
            IForgePlugin plugin = new BuildNumberPlugin();
            profile.SetPluginConfig("BuildNumber",
                new BuildNumberConfig { Enabled = true, Source = BuildNumberSource.GitCommitCount, Offset = 20 });

            Assert.AreEqual("build number from git commit count + 20", plugin.DescribeBuild(profile, isCI: false));
        }

        [Test]
        public void IsEnabled_BuildNumber_DefaultsOffAndRoundTripsViaSetEnabled()
        {
            IForgePlugin plugin = new BuildNumberPlugin();
            Assert.AreEqual(false, plugin.IsEnabled(profile),
                "Build Number must default to disabled.");

            plugin.SetEnabled(profile, true);
            Assert.AreEqual(true, plugin.IsEnabled(profile));

            plugin.SetEnabled(profile, false);
            Assert.AreEqual(false, plugin.IsEnabled(profile));
        }

        [Test]
        public void IsEnabled_AndroidKeystoreEnv_DefaultsOffAndRoundTripsViaSetEnabled()
        {
            IForgePlugin plugin = new AndroidKeystoreEnvPlugin();
            Assert.AreEqual(false, plugin.IsEnabled(profile),
                "Android Keystore Env must default to disabled.");

            plugin.SetEnabled(profile, true);
            Assert.AreEqual(true, plugin.IsEnabled(profile));
        }

        [Test]
        public void IsEnabled_CloudDiagnostics_DefaultsOffAndRoundTripsViaSetEnabled()
        {
            IForgePlugin plugin = new CloudDiagnosticsPlugin();
            Assert.AreEqual(false, plugin.IsEnabled(profile),
                "Cloud Diagnostics must default to disabled.");

            plugin.SetEnabled(profile, true);
            Assert.AreEqual(true, plugin.IsEnabled(profile));
        }

        [Test]
        public void SetEnabled_PreservesOtherConfigValues()
        {
            var config = new BuildNumberConfig { Offset = 42 };
            profile.SetPluginConfig("BuildNumber", config);

            IForgePlugin plugin = new BuildNumberPlugin();
            plugin.SetEnabled(profile, true);

            var reloaded = profile.GetPluginConfig<BuildNumberConfig>("BuildNumber");
            Assert.IsTrue(reloaded.Enabled);
            Assert.AreEqual(42, reloaded.Offset);
        }

        [Test]
        public void BuildOnlyPlugins_AreNotEditorApplicable()
        {
            // Build numbers, signing, symbol upload and manifest metadata have no
            // meaning as persistent editor state; only settings-shaped plugins
            // (OpenXR) opt in via IForgeEditorApplicable.
            Assert.IsFalse(new BuildNumberPlugin() is IForgeEditorApplicable);
            Assert.IsFalse(new AndroidKeystoreEnvPlugin() is IForgeEditorApplicable);
            Assert.IsFalse(new CloudDiagnosticsPlugin() is IForgeEditorApplicable);
            Assert.IsFalse(new GitMetadataPlugin() is IForgeEditorApplicable);
        }

        [Test]
        public void Description_WithoutOverride_DefaultsToNull()
        {
            IForgePlugin plugin = new MinimalPlugin();
            Assert.IsNull(plugin.Description);
        }

        [Test]
        public void NotApplicableReason_WithoutOverride_DefaultsToNull()
        {
            IForgePlugin plugin = new MinimalPlugin();
            Assert.IsNull(plugin.NotApplicableReason(null));
        }

        [Test]
        public void NotApplicableReason_AndroidKeystoreEnv_ExplainsPlatformScope()
        {
            IForgePlugin plugin = new AndroidKeystoreEnvPlugin();
            StringAssert.Contains("Android", plugin.NotApplicableReason(null));
        }
    }
}
