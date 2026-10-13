using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.TestTools;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// Build interception: Unity's own Build buttons stop before Unity asks for a
    /// location, other builds fail in the pre-build callback, both with the
    /// dialog, and builds pass while interception is off. The dialog itself needs
    /// an interactive editor, so these tests replace it.
    /// </summary>
    public class ForgeBuildInterceptorTests
    {
        bool previousIntercept;
        int dialogs;

        [SetUp]
        public void SetUp()
        {
            previousIntercept = ForgeSettings.instance.InterceptBuilds;
            dialogs = 0;
            ForgeBuildInterceptor.DialogOverride = () => dialogs++;
        }

        [TearDown]
        public void TearDown()
        {
            ForgeBuildInterceptor.DialogOverride = null;
            ForgeSettings.instance.InterceptBuilds = previousIntercept;
        }

        [Test]
        public void UnitysBuildButtons_StopBeforeTheLocationPrompt_WithTheDialog()
        {
            ForgeSettings.instance.InterceptBuilds = true;
            LogAssert.Expect(LogType.Warning, new Regex("Unity's build was stopped before it started"));

            Assert.Throws<BuildPlayerWindow.BuildMethodException>(
                () => ForgeBuildInterceptor.OnUnityBuildButton(new BuildPlayerOptions()));
            Assert.AreEqual(1, dialogs);
        }

        [Test]
        public void OtherBuilds_FailBeforeTheyStart_WithTheDialog()
        {
            ForgeSettings.instance.InterceptBuilds = true;
            LogAssert.Expect(LogType.Error, new Regex("Build intercepted"));

            var e = Assert.Throws<BuildFailedException>(() => new ForgeBuildInterceptor().OnPreprocessBuild(null));
            StringAssert.StartsWith("Build Forge is configured to intercept builds.", e.Message);
            Assert.AreEqual(1, dialogs);
        }

        [Test]
        public void WithInterceptionOff_BuildsPassThePreBuildCallback()
        {
            ForgeSettings.instance.InterceptBuilds = false;

            Assert.DoesNotThrow(() => new ForgeBuildInterceptor().OnPreprocessBuild(null));
            Assert.AreEqual(0, dialogs);
        }
    }
}
