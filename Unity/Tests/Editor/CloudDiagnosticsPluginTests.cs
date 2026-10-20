using System;
using System.IO;
using System.Reflection;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.Plugins;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.CrashReporting;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// The plugin sets both of Unity's crash reporting settings for a build: crash reporting,
    /// and the Unity Build Profile's own Diagnostics Data, which it finds by Unity's serialized
    /// field name.
    /// </summary>
    public class CloudDiagnosticsPluginTests
    {
        static SerializedProperty DiagnosticsData(BuildProfile profile)
            => new SerializedObject(profile).FindProperty(CloudDiagnosticsPlugin.DiagnosticsDataPath);

        static void SetStored(BuildProfile profile, int state)
        {
            var so = new SerializedObject(profile);
            so.FindProperty(CloudDiagnosticsPlugin.DiagnosticsDataPath).intValue = state;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // A Unity version that renames the field fails here, before builds of a profile with the
        // plugin on stop for it.
        [Test]
        public void DiagnosticsData_IsWhereThePluginSetsIt()
        {
            var windows = VariantRulesTests.CreateNativeProfile();
            try
            {
                var property = DiagnosticsData(windows);
                Assert.IsNotNull(property, "A Windows Build Profile has no Diagnostics Data setting.");
                Assert.AreEqual(SerializedPropertyType.Enum, property.propertyType);
            }
            finally { UnityEngine.Object.DestroyImmediate(windows); }

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                Assert.Ignore("This editor has no Android Build Support.");
            var android = VariantRulesTests.CreateNativeProfile(BuildTarget.Android);
            try
            {
                Assert.IsNotNull(DiagnosticsData(android), "An Android Build Profile has no Diagnostics Data setting.");
            }
            finally { UnityEngine.Object.DestroyImmediate(android); }
        }

        [Test]
        public void SetDiagnosticsData_SetsTheProfilesOwnValue_AndReturnsTheOneItReplaced()
        {
            var profile = VariantRulesTests.CreateNativeProfile();
            try
            {
                SetStored(profile, CloudDiagnosticsPlugin.DiagnosticsProjectDefault);
                Assert.AreEqual(CloudDiagnosticsPlugin.DiagnosticsProjectDefault,
                    CloudDiagnosticsPlugin.SetDiagnosticsData(profile, CloudDiagnosticsPlugin.DiagnosticsEnabled));
                Assert.AreEqual(CloudDiagnosticsPlugin.DiagnosticsEnabled, DiagnosticsData(profile).intValue);
                Assert.AreEqual(CloudDiagnosticsPlugin.DiagnosticsEnabled,
                    CloudDiagnosticsPlugin.SetDiagnosticsData(profile, CloudDiagnosticsPlugin.DiagnosticsDisabled));
                Assert.AreEqual(CloudDiagnosticsPlugin.DiagnosticsDisabled, DiagnosticsData(profile).intValue);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        // A local build with CI Only has both settings off, whatever the profile says, and gets
        // them back afterwards, and ProjectSettings/UnityConnectSettings.asset as it was.
        [Test]
        public void ABuild_TurnsBothOff_AndPutsThemBack()
        {
            var native = VariantRulesTests.CreateNativeProfile();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            var crashReporting = CrashReportingSettings.enabled;
            var connectSettingsFile = File.ReadAllBytes(CloudDiagnosticsPlugin.ConnectSettingsPath);
            try
            {
                SetStored(native, CloudDiagnosticsPlugin.DiagnosticsEnabled);
                forge.SetPluginConfig("CloudDiagnostics",
                    new CloudDiagnosticsConfig { Enabled = true, CrashReportingMode = CrashReportingMode.CIOnly });
                var plugin = new CloudDiagnosticsPlugin();
                var context = new ForgeBuildContext(native, forge, BuildTarget.StandaloneWindows64, "Builds/Test", isCI: false);

                plugin.OnPreBuild(context);
                Assert.AreEqual(CloudDiagnosticsPlugin.DiagnosticsDisabled, DiagnosticsData(native).intValue);
                Assert.IsFalse(CrashReportingSettings.enabled);

                // A build with both off writes m_Enabled as this session has it, off.
                File.AppendAllText(CloudDiagnosticsPlugin.ConnectSettingsPath, "# written during the build\n");

                plugin.OnPostBuild(context);
                Assert.AreEqual(CloudDiagnosticsPlugin.DiagnosticsEnabled, DiagnosticsData(native).intValue);
                Assert.AreEqual(crashReporting, CrashReportingSettings.enabled);
                CollectionAssert.AreEqual(connectSettingsFile, File.ReadAllBytes(CloudDiagnosticsPlugin.ConnectSettingsPath));
            }
            finally
            {
                CrashReportingSettings.enabled = crashReporting;
                File.WriteAllBytes(CloudDiagnosticsPlugin.ConnectSettingsPath, connectSettingsFile);
                UnityEngine.Object.DestroyImmediate(forge);
                UnityEngine.Object.DestroyImmediate(native);
            }
        }

        // The build writes a changed crash reporting setting to ProjectSettings/UnityConnectSettings.asset;
        // the file gets its bytes from before the build back, and Unity's settings are marked unchanged, so
        // that no save writes this session's values over them.
        [Test]
        public void TheConnectSettingsFile_GetsItsBytesBack_AndIsNotSavedAgain()
        {
            var path = CloudDiagnosticsPlugin.ConnectSettingsPath;
            var connectSettings = Unsupported.GetSerializedAssetInterfaceSingleton("UnityConnectSettings");
            Assert.IsNotNull(connectSettings, "Unity has no UnityConnectSettings object.");
            Assert.IsTrue(File.Exists(path), $"No {path} in the test project.");
            var before = File.ReadAllBytes(path);
            try
            {
                File.AppendAllText(path, "# written during the build\n");
                EditorUtility.SetDirty(connectSettings);
                CloudDiagnosticsPlugin.RestoreConnectSettingsFile(before);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
                Assert.IsFalse(EditorUtility.IsDirty(connectSettings));
            }
            finally { File.WriteAllBytes(path, before); }
        }

        // A CI build with CI Only turns crash reporting on; afterwards the setting and its file are as before.
        [Test]
        public void ABuildThatTurnsCrashReportingOn_LeavesItAndItsFileAsTheyWere()
        {
            var native = VariantRulesTests.CreateNativeProfile();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            var path = CloudDiagnosticsPlugin.ConnectSettingsPath;
            var crashReporting = CrashReportingSettings.enabled;
            var before = File.ReadAllBytes(path);
            try
            {
                Assume.That(crashReporting, Is.False, "The test project has crash reporting on.");
                forge.SetPluginConfig("CloudDiagnostics",
                    new CloudDiagnosticsConfig { Enabled = true, CrashReportingMode = CrashReportingMode.CIOnly });
                var plugin = new CloudDiagnosticsPlugin();
                var context = new ForgeBuildContext(native, forge, BuildTarget.StandaloneWindows64, "Builds/Test", isCI: true);

                plugin.OnPreBuild(context);
                Assert.IsTrue(CrashReportingSettings.enabled);
                Assert.AreEqual(CloudDiagnosticsPlugin.DiagnosticsEnabled, DiagnosticsData(native).intValue);
                File.AppendAllText(path, "# written during the build\n");

                plugin.OnPostBuild(context);
                Assert.IsFalse(CrashReportingSettings.enabled);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
                Assert.IsFalse(EditorUtility.IsDirty(Unsupported.GetSerializedAssetInterfaceSingleton("UnityConnectSettings")));
            }
            finally
            {
                CrashReportingSettings.enabled = crashReporting;
                File.WriteAllBytes(path, before);
                EditorUtility.ClearDirty(Unsupported.GetSerializedAssetInterfaceSingleton("UnityConnectSettings"));
                UnityEngine.Object.DestroyImmediate(forge);
                UnityEngine.Object.DestroyImmediate(native);
            }
        }

        // When putting Diagnostics Data back fails, crash reporting and its file are put back all the
        // same, and nothing of the build is kept for the next one.
        [Test]
        public void ARestoreThatFails_StillPutsCrashReportingAndItsFileBack()
        {
            var native = VariantRulesTests.CreateNativeProfile();
            var bare = ScriptableObject.CreateInstance<BuildProfile>();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            var path = CloudDiagnosticsPlugin.ConnectSettingsPath;
            var crashReporting = CrashReportingSettings.enabled;
            var before = File.ReadAllBytes(path);
            try
            {
                Assume.That(crashReporting, Is.False, "The test project has crash reporting on.");
                forge.SetPluginConfig("CloudDiagnostics",
                    new CloudDiagnosticsConfig { Enabled = true, CrashReportingMode = CrashReportingMode.Always });
                var plugin = new CloudDiagnosticsPlugin();
                var context = new ForgeBuildContext(native, forge, BuildTarget.StandaloneWindows64, "Builds/Test", isCI: false);

                plugin.OnPreBuild(context);
                Assert.IsTrue(CrashReportingSettings.enabled);
                File.AppendAllText(path, "# written during the build\n");
                // A profile without the setting stands in for one that can't be written to after the build.
                typeof(CloudDiagnosticsPlugin).GetField("diagnosticsProfile", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(plugin, bare);

                Assert.Throws<InvalidOperationException>(() => plugin.OnPostBuild(context));
                Assert.IsFalse(CrashReportingSettings.enabled);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(path));

                CrashReportingSettings.enabled = true;
                plugin.OnPostBuild(context);
                Assert.IsTrue(CrashReportingSettings.enabled, "A second post-build put back a value from the build.");
            }
            finally
            {
                CrashReportingSettings.enabled = crashReporting;
                File.WriteAllBytes(path, before);
                EditorUtility.ClearDirty(Unsupported.GetSerializedAssetInterfaceSingleton("UnityConnectSettings"));
                UnityEngine.Object.DestroyImmediate(forge);
                UnityEngine.Object.DestroyImmediate(bare);
                UnityEngine.Object.DestroyImmediate(native);
            }
        }

        [Test]
        public void ABuild_Stops_WhereUnityStoresNoDiagnosticsData()
        {
            var bare = ScriptableObject.CreateInstance<BuildProfile>();
            try
            {
                var message = Assert.Throws<InvalidOperationException>(
                    () => CloudDiagnosticsPlugin.SetDiagnosticsData(bare, CloudDiagnosticsPlugin.DiagnosticsDisabled)).Message;
                StringAssert.Contains("stores no Diagnostics Data setting", message);
                StringAssert.Contains("Update Build Forge", message);
            }
            finally { UnityEngine.Object.DestroyImmediate(bare); }
        }
    }
}
