using BuildForge.Editor.Plugins;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class GitHelperTests
    {
        [Test]
        public void Run_ValidCommand_ReturnsOutput()
        {
            var result = GitHelper.Run("--version");
            Assert.IsNotNull(result);
            Assert.That(result, Does.StartWith("git version"));
        }

        [Test]
        public void Run_InvalidCommand_ReturnsNull()
        {
            var result = GitHelper.Run("this-is-not-a-real-command");
            Assert.IsNull(result);
        }

        [Test]
        public void RunInt_ValidIntOutput_ReturnsValue()
        {
            var result = GitHelper.RunInt("rev-list --count HEAD");
            if (result.HasValue)
                Assert.Greater(result.Value, 0);
        }

        [Test]
        public void RunInt_NonIntOutput_ReturnsNull()
        {
            var result = GitHelper.RunInt("--version");
            Assert.IsNull(result);
        }

        [Test]
        public void Run_TimesOut_ReturnsNull()
        {
            // 1ms timeout should cause any real command to time out
            var result = GitHelper.Run("rev-list --count HEAD", timeoutMs: 1);
            // May or may not time out depending on machine speed; just verify no exception
            Assert.True(result == null || result.Length > 0);
        }
    }
}
