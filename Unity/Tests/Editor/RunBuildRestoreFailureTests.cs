using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor.Build.Reporting;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// The public RunBuild throws when the player was built but a step after the build
    /// failed, so a script that judges the build by its report doesn't take a possibly
    /// modified project for a clean build. A real build of it is in the variant proof
    /// (Tools~/VariantProof).
    /// </summary>
    public class RunBuildRestoreFailureTests
    {
        [Test]
        public void ABuiltPlayer_WithAFailedStepAfterIt_Throws_WithTheFailures()
        {
            var failures = new[] { "Post-build restore failed in OpenXR: boom", "Restoring the version failed: boom" };

            var failure = ForgeBuildRunner.RestoreFailure(null, BuildResult.Succeeded, failures);

            CollectionAssert.AreEqual(failures, failure.Failures);
            StringAssert.StartsWith("The player was built, but 2 restore or cleanup step(s) after the build failed", failure.Message);
            StringAssert.Contains("Post-build restore failed in OpenXR: boom", failure.Message);
        }

        [TestCase(BuildResult.Failed)]
        [TestCase(BuildResult.Cancelled)]
        [TestCase(BuildResult.Unknown)]
        public void APlayerThatWasNotBuilt_IsReportedByItsReport(BuildResult result)
            => Assert.IsNull(ForgeBuildRunner.RestoreFailure(null, result, new[] { "Restoring the version failed: boom" }));

        [Test]
        public void ABuiltPlayer_WithEveryStepAfterItDone_DoesNotThrow()
            => Assert.IsNull(ForgeBuildRunner.RestoreFailure(null, BuildResult.Succeeded, new string[0]));
    }
}
