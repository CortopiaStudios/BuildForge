using System;
using System.Reflection;
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
    /// dialog, and builds pass while interception is off. The handler for the
    /// Build buttons is registered only in an interactive editor with
    /// interception on. The dialog itself needs an interactive editor, so these
    /// tests replace it.
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

        // Unity keeps the registered handler here (the same field from 6000.3.0f1 to 6000.7.0b4).
        static readonly FieldInfo OptionsHandler =
            typeof(BuildPlayerWindow).GetField("getBuildPlayerOptionsHandler", BindingFlags.Static | BindingFlags.NonPublic);

        [Test]
        public void Register_HandsUnitysBuildButtonsToTheInterceptor_InAnInteractiveEditorWithInterceptionOn()
        {
            Assert.IsNotNull(OptionsHandler, "BuildPlayerWindow.getBuildPlayerOptionsHandler is missing");
            var previous = OptionsHandler.GetValue(null);
            try
            {
                OptionsHandler.SetValue(null, null);
                ForgeBuildInterceptor.Register(batchMode: true, interceptBuilds: true);
                ForgeBuildInterceptor.Register(batchMode: false, interceptBuilds: false);
                Assert.IsNull(OptionsHandler.GetValue(null));

                ForgeBuildInterceptor.Register(batchMode: false, interceptBuilds: true);

                var handler = (Delegate)OptionsHandler.GetValue(null);
                Assert.AreEqual(typeof(ForgeBuildInterceptor).GetMethod(nameof(ForgeBuildInterceptor.OnUnityBuildButton),
                    BindingFlags.Static | BindingFlags.NonPublic), handler?.Method);
            }
            finally
            {
                OptionsHandler.SetValue(null, previous);
            }
        }
    }
}
