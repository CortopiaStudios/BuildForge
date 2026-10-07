using BuildForge.Editor.Plugins;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class BuildNumberConfigTests
    {
        [Test]
        public void EnvVarName_Default_IsBuildNumber()
        {
            var config = new BuildNumberConfig();
            Assert.AreEqual("BUILD_NUMBER", config.EnvVarName);
        }

        [Test]
        public void EnvVarName_SetEmpty_ReturnsFallbackDefault()
        {
            var config = new BuildNumberConfig();
            config.EnvVarName = "";
            Assert.AreEqual("BUILD_NUMBER", config.EnvVarName);
        }

        [Test]
        public void EnvVarName_SetNull_ReturnsFallbackDefault()
        {
            var config = new BuildNumberConfig();
            config.EnvVarName = null;
            Assert.AreEqual("BUILD_NUMBER", config.EnvVarName);
        }

        [Test]
        public void EnvVarName_SetCustom_ReturnsCustom()
        {
            var config = new BuildNumberConfig();
            config.EnvVarName = "MY_BUILD_NUM";
            Assert.AreEqual("MY_BUILD_NUM", config.EnvVarName);
        }

        [Test]
        public void Enabled_DefaultsFalse()
        {
            var config = new BuildNumberConfig();
            Assert.IsFalse(config.Enabled);
        }

        [Test]
        public void Source_DefaultsGitCommitCount()
        {
            var config = new BuildNumberConfig();
            Assert.AreEqual(BuildNumberSource.GitCommitCount, config.Source);
        }

        [Test]
        public void Offset_DefaultsZero()
        {
            var config = new BuildNumberConfig();
            Assert.AreEqual(0, config.Offset);
        }
    }
}
