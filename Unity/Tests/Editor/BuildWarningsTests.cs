using System;
using System.Collections.Generic;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// A build logs what the build window warns about before it changes anything,
    /// since the log is all a CI build has to show it.
    /// </summary>
    public class BuildWarningsTests
    {
        /// <summary>A plugin whose Validate returns or throws what the test gives it.</summary>
        sealed class WarningPlugin : IForgePlugin
        {
            readonly Func<IReadOnlyList<string>> validate;

            public WarningPlugin(string name, Func<IReadOnlyList<string>> validate) { DisplayName = name; this.validate = validate; }

            public string DisplayName { get; }
            public bool IsApplicable(BuildProfile profile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
            public IReadOnlyList<string> Validate(ForgeProfile profile, string variant) => validate();
        }

        [Test]
        public void TheWindowsWarnings_AreLogLines_WithTheirPluginsPrefix()
        {
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var settings = new SerializedObject(ForgeSettings.instance);
                var variants = settings.FindProperty("buildVariants");
                variants.arraySize = 1;
                variants.GetArrayElementAtIndex(0).stringValue = "Internal";
                settings.FindProperty("variantRules").arraySize = 0;
                settings.FindProperty("maintainBuildProfileDefines").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var so = new SerializedObject(profile);
                so.FindProperty("outputPath").stringValue = "Builds/{ProfileName}";
                so.ApplyModifiedPropertiesWithoutUndo();
                var plugins = new IForgePlugin[]
                {
                    new WarningPlugin("Quiet", () => Array.Empty<string>()),
                    new WarningPlugin("Loud", () => new[] { "First.", "Second." }),
                    new WarningPlugin("Broken", () => throw new InvalidOperationException("boom")),
                    new WarningPlugin("Null", () => null),
                };

                CollectionAssert.AreEqual(new[]
                {
                    "[Build Forge] The output path has no {Variant} placeholder, so the variants' builds overwrite each other.",
                    "[Build Forge/Loud] First.",
                    "[Build Forge/Loud] Second.",
                    "[Build Forge/Broken] Checking the profile's settings failed: boom",
                }, ForgeBuildRunner.BuildWarnings(profile, "Internal", plugins));
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        /// <summary>A plugin with a switch, applicable or not, as the test gives it.</summary>
        sealed class SwitchedPlugin : IForgePlugin
        {
            readonly bool? enabled;
            readonly bool applicable;

            public SwitchedPlugin(string name, bool? enabled, bool applicable) { DisplayName = name; this.enabled = enabled; this.applicable = applicable; }

            public string DisplayName { get; }
            public bool IsApplicable(BuildProfile profile) => applicable;
            public string NotApplicableReason(BuildProfile profile) => "it builds for Android only.";
            public bool? IsEnabled(ForgeProfile profile) => enabled;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        // A plugin the profile switched on that doesn't apply is skipped with a word; one without
        // a switch that doesn't apply is simply not for this platform.
        [Test]
        public void APluginSwitchedOnThatDoesNotApply_IsSkippedWithItsReason()
        {
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var running = new SwitchedPlugin("Running", true, true);
                var noSwitch = new SwitchedPlugin("Plain", null, true);
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex(@"Keystore is on for .* doesn't run: it builds for Android only\."));

                var plugins = ForgeBuildRunner.PluginsFor(new IForgePlugin[]
                {
                    running,
                    new SwitchedPlugin("Keystore", true, false),
                    new SwitchedPlugin("Manifest", null, false),
                    new SwitchedPlugin("Off", false, true),
                    noSwitch,
                }, profile, null);

                CollectionAssert.AreEqual(new IForgePlugin[] { running, noSwitch }, plugins);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }
    }
}
