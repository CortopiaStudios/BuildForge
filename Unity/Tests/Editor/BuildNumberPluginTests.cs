using System;
using System.Linq;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.Plugins;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    public class BuildNumberPluginTests
    {
        [TestCase(100, 3, 103)]
        [TestCase(100, -3, 97)]
        [TestCase(0, 0, 0)]
        [TestCase(int.MaxValue, 0, int.MaxValue)]
        [TestCase(0, int.MaxValue, int.MaxValue)]
        public void Calculate_ValidResult(int baseNumber, int offset, int expected)
        {
            Assert.IsTrue(BuildNumberPlugin.TryCalculateBuildNumber(baseNumber, offset, out var number));
            Assert.AreEqual(expected, number);
        }

        [TestCase(int.MaxValue, 3)]
        [TestCase(int.MaxValue, int.MaxValue)]
        [TestCase(0, int.MinValue)]
        [TestCase(100, -101)]
        [TestCase(-1, 3)]
        public void Calculate_InvalidResult(int baseNumber, int offset)
        {
            Assert.IsFalse(BuildNumberPlugin.TryCalculateBuildNumber(baseNumber, offset, out _));
        }

        [TestCase(int.MaxValue, 3)]
        [TestCase(100, -101)]
        public void PreBuild_InvalidNumber_DoesNotMutatePlayerSettings(int baseNumber, int offset)
        {
            var envName = "BUILDFORGE_TEST_NUMBER_" + Guid.NewGuid().ToString("N");
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            var android = PlayerSettings.Android.bundleVersionCode;
            var ios = PlayerSettings.iOS.buildNumber;
            var plugin = new BuildNumberPlugin();
            try
            {
                Environment.SetEnvironmentVariable(envName, baseNumber.ToString());
                profile.SetPluginConfig("BuildNumber", new BuildNumberConfig
                {
                    Enabled = true,
                    Source = BuildNumberSource.EnvironmentVariable,
                    EnvVarName = envName,
                    Offset = offset,
                });
                var context = new ForgeBuildContext(null, profile, BuildTarget.StandaloneWindows64, "unused", true);

                var failure = Assert.Throws<InvalidOperationException>(() => plugin.OnPreBuild(context));
                StringAssert.Contains("must be between 0 and 2147483647", failure.Message);
                Assert.AreEqual(android, PlayerSettings.Android.bundleVersionCode);
                Assert.AreEqual(ios, PlayerSettings.iOS.buildNumber);
                plugin.OnPostBuild(context);
                Assert.AreEqual(android, PlayerSettings.Android.bundleVersionCode);
                Assert.AreEqual(ios, PlayerSettings.iOS.buildNumber);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envName, null);
                PlayerSettings.Android.bundleVersionCode = android;
                PlayerSettings.iOS.buildNumber = ios;
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        // Through the parser, so an empty value is tested as one: in the
        // editor, setting a variable to an empty string removes it.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("12a")]
        [TestCase("-5")]
        public void ParseBuildNumber_UnusableValue_IsNegative(string value)
        {
            Assert.Less(BuildNumberPlugin.ParseBuildNumber(value), 0);
        }

        [TestCase("0", 0)]
        [TestCase("436", 436)]
        public void ParseBuildNumber_WholeNumber(string value, int expected)
        {
            Assert.AreEqual(expected, BuildNumberPlugin.ParseBuildNumber(value));
        }

        [Test]
        public void PreBuild_EnvVarMissing_FailsWithoutChangingPlayerSettings()
        {
            var envName = UnsetEnvVarName();
            var profile = EnvVarProfile(envName);
            var android = PlayerSettings.Android.bundleVersionCode;
            var ios = PlayerSettings.iOS.buildNumber;
            var plugin = new BuildNumberPlugin();
            try
            {
                var context = new ForgeBuildContext(null, profile, BuildTarget.StandaloneWindows64, "unused", true);

                var failure = Assert.Throws<InvalidOperationException>(() => plugin.OnPreBuild(context));
                StringAssert.Contains($"{envName} environment variable could not be read", failure.Message);
                Assert.AreEqual(android, PlayerSettings.Android.bundleVersionCode);
                Assert.AreEqual(ios, PlayerSettings.iOS.buildNumber);
                plugin.OnPostBuild(context);
                Assert.AreEqual(android, PlayerSettings.Android.bundleVersionCode);
                Assert.AreEqual(ios, PlayerSettings.iOS.buildNumber);
            }
            finally
            {
                PlayerSettings.Android.bundleVersionCode = android;
                PlayerSettings.iOS.buildNumber = ios;
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Validate_EnvVarMissing_WarnsThatTheBuildWillFail()
        {
            var envName = UnsetEnvVarName();
            var profile = EnvVarProfile(envName);
            try
            {
                var warnings = new BuildNumberPlugin().Validate(profile);

                Assert.AreEqual(1, warnings.Count);
                StringAssert.Contains(envName, warnings[0]);
                StringAssert.Contains("will fail", warnings[0]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Validate_EnvVarSet_NoWarning()
        {
            var envName = UnsetEnvVarName();
            var profile = EnvVarProfile(envName);
            try
            {
                Environment.SetEnvironmentVariable(envName, "436");

                CollectionAssert.IsEmpty(new BuildNumberPlugin().Validate(profile));
            }
            finally
            {
                Environment.SetEnvironmentVariable(envName, null);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void EffectiveSource_EnvironmentVariableOnCI_FollowsCIDetection()
        {
            Assert.AreEqual(BuildNumberSource.EnvironmentVariable,
                BuildNumberPlugin.EffectiveSource(BuildNumberSource.EnvironmentVariableOnCI, isCI: true));
            Assert.AreEqual(BuildNumberSource.GitCommitCount,
                BuildNumberPlugin.EffectiveSource(BuildNumberSource.EnvironmentVariableOnCI, isCI: false));
            foreach (var isCI in new[] { true, false })
            {
                Assert.AreEqual(BuildNumberSource.GitCommitCount,
                    BuildNumberPlugin.EffectiveSource(BuildNumberSource.GitCommitCount, isCI));
                Assert.AreEqual(BuildNumberSource.EnvironmentVariable,
                    BuildNumberPlugin.EffectiveSource(BuildNumberSource.EnvironmentVariable, isCI));
            }
        }

        [Test]
        public void PreBuild_EnvVarOnCI_OnCI_UsesTheVariable()
        {
            var envName = UnsetEnvVarName();
            var profile = EnvVarProfile(envName, BuildNumberSource.EnvironmentVariableOnCI);
            var android = PlayerSettings.Android.bundleVersionCode;
            var ios = PlayerSettings.iOS.buildNumber;
            var plugin = new BuildNumberPlugin();
            try
            {
                Environment.SetEnvironmentVariable(envName, "436");
                var context = new ForgeBuildContext(null, profile, BuildTarget.StandaloneWindows64, "unused", isCI: true);

                plugin.OnPreBuild(context);
                Assert.AreEqual(436, PlayerSettings.Android.bundleVersionCode);
                Assert.AreEqual("436", PlayerSettings.iOS.buildNumber);
                plugin.OnPostBuild(context);
                Assert.AreEqual(android, PlayerSettings.Android.bundleVersionCode);
                Assert.AreEqual(ios, PlayerSettings.iOS.buildNumber);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envName, null);
                PlayerSettings.Android.bundleVersionCode = android;
                PlayerSettings.iOS.buildNumber = ios;
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void PreBuild_EnvVarOnCI_Locally_IgnoresTheVariable()
        {
            var envName = UnsetEnvVarName();
            var profile = EnvVarProfile(envName, BuildNumberSource.EnvironmentVariableOnCI);
            var android = PlayerSettings.Android.bundleVersionCode;
            var ios = PlayerSettings.iOS.buildNumber;
            var plugin = new BuildNumberPlugin();
            try
            {
                Environment.SetEnvironmentVariable(envName, "436");
                var context = new ForgeBuildContext(null, profile, BuildTarget.StandaloneWindows64, "unused", isCI: false);

                // The test host may not be a git repository: then the build
                // fails on the commit count, which still is not the variable.
                try
                {
                    plugin.OnPreBuild(context);
                    Assert.AreNotEqual(436, PlayerSettings.Android.bundleVersionCode, "A local build read the CI variable.");
                }
                catch (InvalidOperationException failure)
                {
                    StringAssert.Contains("git commit count", failure.Message);
                }
                plugin.OnPostBuild(context);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envName, null);
                PlayerSettings.Android.bundleVersionCode = android;
                PlayerSettings.iOS.buildNumber = ios;
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Validate_EnvVarOnCI_WarnsAboutAMissingVariableOnlyOnCI()
        {
            var envName = UnsetEnvVarName();
            var profile = EnvVarProfile(envName, BuildNumberSource.EnvironmentVariableOnCI);
            try
            {
                var plugin = new BuildNumberPlugin();

                var onCI = plugin.Validate(profile, isCI: true);
                Assert.AreEqual(1, onCI.Count);
                StringAssert.Contains(envName, onCI[0]);
                Assert.IsFalse(plugin.Validate(profile, isCI: false).Any(warning => warning.Contains(envName)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DescribeBuild_EnvVarOnCI_NamesTheSourceThisBuildUses()
        {
            var profile = EnvVarProfile("BUILD_NUMBER", BuildNumberSource.EnvironmentVariableOnCI);
            try
            {
                var plugin = new BuildNumberPlugin();

                Assert.AreEqual("build number from $BUILD_NUMBER (CI build)", plugin.DescribeBuild(profile, isCI: true));
                Assert.AreEqual("build number from git commit count (local build)", plugin.DescribeBuild(profile, isCI: false));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        static string UnsetEnvVarName() => "BUILDFORGE_TEST_NUMBER_" + Guid.NewGuid().ToString("N");

        static ForgeProfile EnvVarProfile(string envName, BuildNumberSource source = BuildNumberSource.EnvironmentVariable)
        {
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            profile.SetPluginConfig("BuildNumber", new BuildNumberConfig
            {
                Enabled = true,
                Source = source,
                EnvVarName = envName,
            });
            return profile;
        }
    }
}
