using System;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;

namespace BuildForge.Tests.Editor
{
    public class VariantBuildConfigurationTests
    {
        const string Prefix = "m_PlatformBuildProfile.";

        static BuildVariantRule Rule(VariantOverride development, Action<VariantBuildConfiguration> configure)
        {
            var rule = new BuildVariantRule { Variant = "Default", DevelopmentBuild = development };
            configure(rule.BuildConfiguration);
            return rule;
        }

        // Unity's own value for a UnityEditor.Compression member, which is internal.
        static int CompressionValue(string member)
            => Convert.ToInt32(Enum.Parse(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Compression", true), member));

        [Test] public void DevelopmentOptionsOnWithDevelopmentOff_AreRejected()
        {
            var rule = Rule(VariantOverride.Disabled, c => c.ScriptDebugging = VariantOverride.Enabled);
            Assert.That(BuildVariants.RulesError(new[] { rule }, Array.Empty<string>()),
                Does.Contain("Development Build off").And.Contain("Script Debugging"));
            rule.DevelopmentBuild = VariantOverride.Inherit;
            Assert.That(BuildVariants.RulesError(new[] { rule }, Array.Empty<string>()), Is.Null);
            rule.BuildConfiguration.ScriptDebugging = VariantOverride.Disabled;
            rule.BuildConfiguration.WaitForManagedDebugger = VariantOverride.Enabled;
            Assert.That(BuildVariants.RulesError(new[] { rule }, Array.Empty<string>()), Does.Contain("Wait For Managed Debugger"));
        }

        [Test] public void InvalidBuildConfigurationValues_AreRejected()
        {
            var rule = Rule(VariantOverride.Inherit, c => c.Compression = (VariantCompression)42);
            Assert.That(BuildVariants.RulesError(new[] { rule }, Array.Empty<string>()), Does.Contain("invalid override value"));
        }

        [Test] public void BuildConfiguration_SetsTheProfilesSettings_AndRestoreReturnsThem()
        {
            var profile = VariantRulesTests.CreateNativeProfile();
            try
            {
                var so = new SerializedObject(profile);
                foreach (var field in VariantBuildSettings.Fields)
                    so.FindProperty(Prefix + field).boolValue = false;
                so.FindProperty(Prefix + "m_CompressionType").intValue = -1;
                so.ApplyModifiedPropertiesWithoutUndo();
                var before = VariantBuildSettings.Capture(profile);

                var rule = Rule(VariantOverride.Enabled, c =>
                {
                    c.AutoconnectProfiler = VariantOverride.Enabled;
                    c.DeepProfilingSupport = VariantOverride.Enabled;
                    c.ScriptDebugging = VariantOverride.Enabled;
                    c.WaitForManagedDebugger = VariantOverride.Enabled;
                    c.Compression = VariantCompression.LZ4HC;
                    // Android only: skipped on this Windows profile.
                    c.LinkTimeOptimization = VariantLinkTimeOptimization.Thin;
                    c.DebugSymbols = VariantDebugSymbols.Full;
                });
                Assert.That(VariantBuildSettings.ValidationError(profile, rule), Is.Null);
                VariantBuildSettings.Apply(profile, rule);
                so.Update();
                foreach (var field in VariantBuildSettings.Fields)
                    Assert.IsTrue(so.FindProperty(Prefix + field).boolValue, field);
                Assert.That(so.FindProperty(Prefix + "m_CompressionType").intValue, Is.EqualTo(CompressionValue("Lz4HC")));
                Assert.IsFalse(VariantBuildSettings.HasSetting(profile, "m_LinkTimeOptimization"));
                Assert.IsFalse(VariantBuildSettings.HasSetting(profile, "m_DebugSymbolLevel"));

                VariantBuildSettings.Restore(profile, before);
                Assert.That(VariantBuildSettings.Capture(profile), Is.EqualTo(before));
                so.Update();
                Assert.That(so.FindProperty(Prefix + "m_CompressionType").intValue, Is.EqualTo(-1), "the unset compression comes back");
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test] public void DisabledOptions_TurnOffTheProfilesOwn_AndInheritKeepsTheRest()
        {
            var profile = VariantRulesTests.CreateNativeProfile();
            try
            {
                var so = new SerializedObject(profile);
                foreach (var field in VariantBuildSettings.Fields)
                    so.FindProperty(Prefix + field).boolValue = true;
                so.FindProperty(Prefix + "m_CompressionType").intValue = CompressionValue("Lz4");
                so.ApplyModifiedPropertiesWithoutUndo();

                VariantBuildSettings.Apply(profile, Rule(VariantOverride.Inherit, c =>
                {
                    c.ScriptDebugging = VariantOverride.Disabled;
                    c.Compression = VariantCompression.Default;
                }));
                so.Update();
                Assert.IsFalse(so.FindProperty(Prefix + "m_AllowDebugging").boolValue);
                Assert.IsTrue(so.FindProperty(Prefix + "m_Development").boolValue);
                Assert.IsTrue(so.FindProperty(Prefix + "m_ConnectProfiler").boolValue);
                Assert.That(so.FindProperty(Prefix + "m_CompressionType").intValue, Is.EqualTo(CompressionValue("None")));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        // The editor ledger stored snapshots of the development flags only.
        [Test] public void SnapshotsWithoutEnumSettings_StillRestore()
        {
            var profile = VariantRulesTests.CreateNativeProfile();
            try
            {
                var so = new SerializedObject(profile);
                so.FindProperty(Prefix + "m_Development").boolValue = true;
                so.FindProperty(Prefix + "m_CompressionType").intValue = CompressionValue("Lz4");
                so.ApplyModifiedPropertiesWithoutUndo();
                VariantBuildSettings.Restore(profile,
                    "{\"values\":[false,false,false,false,false],\"present\":[true,false,false,false,false]}");
                so.Update();
                Assert.IsFalse(so.FindProperty(Prefix + "m_Development").boolValue);
                Assert.That(so.FindProperty(Prefix + "m_CompressionType").intValue, Is.EqualTo(CompressionValue("Lz4")));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test] public void Description_ListsWhatTheProfilesBuildsGet()
        {
            var profile = VariantRulesTests.CreateNativeProfile();
            try
            {
                var rule = Rule(VariantOverride.Inherit, c =>
                {
                    c.CppCompilerConfiguration = VariantCppCompilerConfiguration.Master;
                    c.Il2CppStacktraceInformation = VariantStacktraceInformation.MethodFileLineNumber;
                    c.StripEngineCode = VariantOverride.Disabled;
                    c.Compression = VariantCompression.LZ4;
                    c.LinkTimeOptimization = VariantLinkTimeOptimization.Thin;
                    c.DebugSymbols = VariantDebugSymbols.Full;
                });
                CollectionAssert.AreEqual(new[]
                {
                    "C++ Compiler Configuration: Master",
                    "IL2CPP Stacktrace Information: Method Name, File Name, and Line Number",
                    "Strip Engine Code: Disabled",
                    "Compression Method: LZ4"
                }, BuildVariants.DescribeBuildConfiguration(rule, profile));
                CollectionAssert.IsEmpty(BuildVariants.DescribeBuildConfiguration(null, profile));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test] public void DevelopmentOptions_OutsideADevelopmentBuild_AreReportedAsUnused()
        {
            var profile = VariantRulesTests.CreateNativeProfile();
            try
            {
                var so = new SerializedObject(profile);
                foreach (var field in VariantBuildSettings.Fields)
                    so.FindProperty(Prefix + field).boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                var rule = Rule(VariantOverride.Inherit, c => c.ScriptDebugging = VariantOverride.Enabled);
                CollectionAssert.AreEqual(new[] { "Script Debugging" }, VariantBuildSettings.UnusedDevelopmentOptions(profile, rule));
                rule.DevelopmentBuild = VariantOverride.Enabled;
                CollectionAssert.IsEmpty(VariantBuildSettings.UnusedDevelopmentOptions(profile, rule));
                rule.BuildConfiguration.ScriptDebugging = VariantOverride.Inherit;
                rule.BuildConfiguration.WaitForManagedDebugger = VariantOverride.Enabled;
                CollectionAssert.AreEqual(new[] { "Wait For Managed Debugger" }, VariantBuildSettings.UnusedDevelopmentOptions(profile, rule));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test] public void PlayerSettings_AreSetAndRestored()
        {
            var target = NamedBuildTarget.Standalone;
            var maps = new[] { "il2cppCompilerConfiguration", "il2cppCodeGeneration", "il2cppStacktraceInformation", "managedStrippingLevel" };
            var original = Array.ConvertAll(maps, m => VariantPlayerSettings.ReadMap(SerializedPlayerSettings(), m));
            // As in a project that never changed these for Standalone: the maps have no Standalone entry.
            var serializedBefore = Array.ConvertAll(original, m => m.FindAll(pair => pair.key != target.TargetName));
            var playerSettings = SerializedPlayerSettings();
            for (var i = 0; i < maps.Length; i++)
                VariantPlayerSettings.WriteMap(playerSettings, maps[i], serializedBefore[i]);
            playerSettings.ApplyModifiedPropertiesWithoutUndo();
            var compiler = PlayerSettings.GetIl2CppCompilerConfiguration(target);
            var codeGeneration = PlayerSettings.GetIl2CppCodeGeneration(target);
            var stacktrace = PlayerSettings.GetIl2CppStacktraceInformation(target);
            var stripping = PlayerSettings.GetManagedStrippingLevel(target);
            var stripEngineCode = PlayerSettings.stripEngineCode;
            var snapshot = new VariantPlayerSettings.Snapshot { Target = target };
            try
            {
                var configuration = new VariantBuildConfiguration
                {
                    CppCompilerConfiguration = compiler == Il2CppCompilerConfiguration.Master
                        ? VariantCppCompilerConfiguration.Debug : VariantCppCompilerConfiguration.Master,
                    Il2CppCodeGeneration = codeGeneration == Il2CppCodeGeneration.OptimizeSize
                        ? VariantCodeGeneration.OptimizeSpeed : VariantCodeGeneration.OptimizeSize,
                    Il2CppStacktraceInformation = stacktrace == Il2CppStacktraceInformation.MethodOnly
                        ? VariantStacktraceInformation.MethodFileLineNumber : VariantStacktraceInformation.MethodOnly,
                    ManagedStrippingLevel = stripping == ManagedStrippingLevel.High
                        ? VariantStrippingLevel.Low : VariantStrippingLevel.High,
                    StripEngineCode = stripEngineCode ? VariantOverride.Disabled : VariantOverride.Enabled
                };
                VariantPlayerSettings.Apply(configuration, snapshot);
                Assert.IsTrue(snapshot.Changed);
                Assert.That(PlayerSettings.GetIl2CppCompilerConfiguration(target), Is.Not.EqualTo(compiler));
                Assert.That(PlayerSettings.GetIl2CppCodeGeneration(target), Is.Not.EqualTo(codeGeneration));
                Assert.That(PlayerSettings.GetIl2CppStacktraceInformation(target), Is.Not.EqualTo(stacktrace));
                Assert.That(PlayerSettings.GetManagedStrippingLevel(target), Is.Not.EqualTo(stripping));
                Assert.That(PlayerSettings.stripEngineCode, Is.Not.EqualTo(stripEngineCode));

                VariantPlayerSettings.Restore(snapshot);
                Assert.That(PlayerSettings.GetIl2CppCompilerConfiguration(target), Is.EqualTo(compiler));
                Assert.That(PlayerSettings.GetIl2CppCodeGeneration(target), Is.EqualTo(codeGeneration));
                Assert.That(PlayerSettings.GetIl2CppStacktraceInformation(target), Is.EqualTo(stacktrace));
                Assert.That(PlayerSettings.GetManagedStrippingLevel(target), Is.EqualTo(stripping));
                Assert.That(PlayerSettings.stripEngineCode, Is.EqualTo(stripEngineCode));
                // Not only the same values: no entry the maps didn't have, so ProjectSettings.asset is unchanged.
                for (var i = 0; i < maps.Length; i++)
                    CollectionAssert.AreEqual(serializedBefore[i], VariantPlayerSettings.ReadMap(SerializedPlayerSettings(), maps[i]), maps[i]);

                var unchanged = new VariantPlayerSettings.Snapshot { Target = target };
                VariantPlayerSettings.Apply(new VariantBuildConfiguration(), unchanged);
                Assert.IsFalse(unchanged.Changed, "Inherit changes nothing");
            }
            finally
            {
                VariantPlayerSettings.Restore(snapshot);
                playerSettings = SerializedPlayerSettings();
                for (var i = 0; i < maps.Length; i++)
                    VariantPlayerSettings.WriteMap(playerSettings, maps[i], original[i]);
                playerSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static SerializedObject SerializedPlayerSettings()
            => new SerializedObject(BuildProfile.GetActiveBuildProfile()?.GetComponent<PlayerSettings>()
                                    ?? Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
    }
}
