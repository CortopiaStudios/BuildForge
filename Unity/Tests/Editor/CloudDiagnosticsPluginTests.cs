using System.IO;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.Plugins;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.CrashReporting;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// The plugin sets crash reporting for a build, and afterwards leaves it, and the file
    /// Unity keeps it in, as they were.
    /// </summary>
    public class CloudDiagnosticsPluginTests
    {
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
    }
}
