using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.UI;
using NUnit.Framework;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.TestTools;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// A plugin that throws while a window asks it for a value gets a fallback and one logged
    /// error, so the window keeps drawing, its action bar included.
    /// </summary>
    public class PluginDisplayTests
    {
        sealed class Throwing : IForgePlugin
        {
            public string DisplayName => throw new InvalidOperationException("no name");
            public string Description => throw new InvalidOperationException("no description");
            public bool IsApplicable(BuildProfile profile) => throw new InvalidOperationException("no platform");
            public bool? IsEnabled(ForgeProfile profile) => throw new InvalidOperationException("no config");
            public IReadOnlyList<string> Validate(ForgeProfile profile, string variant) => throw new InvalidOperationException("no check");
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        [Test]
        public void AThrowingPlugin_GetsFallbacks_AndOneErrorPerValue()
        {
            var plugin = new Throwing();
            PluginDisplay.ForgetReported();
            LogAssert.Expect(LogType.Error, new Regex(@"Throwing\.DisplayName threw: no name"));
            LogAssert.Expect(LogType.Error, new Regex(@"Throwing\.IsApplicable threw: no platform"));
            LogAssert.Expect(LogType.Error, new Regex(@"Throwing\.Validate threw: no check"));

            Assert.AreEqual("Throwing", PluginDisplay.Name(plugin));
            Assert.AreEqual("Throwing", PluginDisplay.Name(plugin), "Logged once, not on every repaint.");
            Assert.IsFalse(PluginDisplay.Applies(plugin, null, out var reason));
            StringAssert.Contains("no platform", reason);
            StringAssert.Contains("could not check this profile's settings", PluginDisplay.Warnings(plugin, null, null)[0]);
        }
    }
}
