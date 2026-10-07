using BuildForge.Editor.Plugins;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class CloudDiagnosticsConfigTests
    {
        [Test]
        public void Enabled_DefaultsFalse()
        {
            var config = new CloudDiagnosticsConfig();
            Assert.IsFalse(config.Enabled);
        }

        [Test]
        public void CrashReportingMode_DefaultsCIOnly()
        {
            var config = new CloudDiagnosticsConfig();
            Assert.AreEqual(CrashReportingMode.CIOnly, config.CrashReportingMode);
        }

        [Test]
        public void ResolveEnabled_Always_IsTrueRegardlessOfEnvironment()
        {
            Assert.IsTrue(CloudDiagnosticsPlugin.ResolveEnabled(CrashReportingMode.Always, isCI: false));
            Assert.IsTrue(CloudDiagnosticsPlugin.ResolveEnabled(CrashReportingMode.Always, isCI: true));
        }

        [Test]
        public void ResolveEnabled_CIOnly_FollowsEnvironment()
        {
            Assert.IsFalse(CloudDiagnosticsPlugin.ResolveEnabled(CrashReportingMode.CIOnly, isCI: false));
            Assert.IsTrue(CloudDiagnosticsPlugin.ResolveEnabled(CrashReportingMode.CIOnly, isCI: true));
        }

        [Test]
        public void ResolveEnabled_Never_IsFalseRegardlessOfEnvironment()
        {
            Assert.IsFalse(CloudDiagnosticsPlugin.ResolveEnabled(CrashReportingMode.Never, isCI: false));
            Assert.IsFalse(CloudDiagnosticsPlugin.ResolveEnabled(CrashReportingMode.Never, isCI: true));
        }
    }
}
