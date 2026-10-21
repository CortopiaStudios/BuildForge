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
    }
}
