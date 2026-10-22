using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.TestTools;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// A plugin whose name throws is named by its type wherever a build names it, so its
    /// name can't stop the build, replace the error the build reports, or skip the rest of
    /// the cleanup. These plugins have no ForgePlugin attribute, so discovery outside these
    /// tests doesn't find them.
    /// </summary>
    public class PluginNameFailureTests
    {
        sealed class RemainingCleanup : IForgePlugin
        {
            internal bool Ran;
            public string DisplayName => "Remaining cleanup";
            public bool IsApplicable(BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) => Ran = true;
        }

        // A name that is gone once the restore failed, as one read from an asset the build unloaded.
        sealed class NameUnavailableAfterCleanup : IForgePlugin
        {
            bool attempted;
            public string DisplayName => attempted
                ? throw new InvalidOperationException("DisplayName unavailable after cleanup failure")
                : "Cleanup with transient name";
            public bool IsApplicable(BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context)
            {
                attempted = true;
                throw new InvalidOperationException("Primary cleanup failure");
            }
        }

        // The failure's message read the name, which threw, and took the remaining hooks along.
        [Test]
        public void RunPostBuild_ANameThatThrowsWhileReportingAFailedRestore_StillRunsTheRemainingHooks()
        {
            var remaining = new RemainingCleanup();
            var failing = new NameUnavailableAfterCleanup();
            var context = new ForgeBuildContext(null, null, BuildTarget.StandaloneWindows64, "unused", true);
            LogAssert.Expect(LogType.Error, new Regex(
                $"Post-build restore failed in {Regex.Escape(typeof(NameUnavailableAfterCleanup).FullName)}: Primary cleanup failure"));
            Exception escaped = null;
            try
            {
                // Post-build hooks run in reverse order, so the failing one runs first.
                ForgeBuildRunner.RunPostBuild(new IForgePlugin[] { remaining, failing }, context);
            }
            catch (Exception e)
            {
                escaped = e;
            }

            Assert.IsTrue(escaped == null && remaining.Ran && context.PostBuildFailures.Count == 1,
                "Cleanup must contain the exception, run all hooks and record the failure. " +
                $"Escaped={escaped?.Message}; RemainingHookRan={remaining.Ran}; RecordedFailures={context.PostBuildFailures.Count}");
            StringAssert.Contains("Primary cleanup failure", context.PostBuildFailures[0]);
            StringAssert.Contains(typeof(NameUnavailableAfterCleanup).FullName, context.PostBuildFailures[0]);
        }

        class Nameless : IForgePlugin
        {
            internal bool PostBuildRan;
            readonly bool fails;

            public Nameless(bool fails) { this.fails = fails; }

            public string DisplayName => throw new InvalidOperationException("no name");
            public bool IsApplicable(BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context)
            {
                PostBuildRan = true;
                if (fails)
                    throw new InvalidOperationException("restore boom");
            }
        }

        sealed class NamelessApplicable : Nameless, IForgeEditorApplicable
        {
            public NamelessApplicable(bool fails) : base(fails) { }

            public string CaptureEditorState(BuildTargetGroup group) => null;
            public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group, string variant) { }
            public void RestoreEditorState(BuildTargetGroup group, string json) { }
        }

        // The log line before each hook reads the name too: one that throws from the start
        // doesn't skip the hook, and a restore that fails is still listed as not restored.
        [Test]
        public void RunPostBuild_ANameThatThrowsFromTheStart_StillRunsTheHook_AndListsAFailedRestore()
        {
            var restored = new Nameless(fails: false);
            var failing = new NamelessApplicable(fails: true);
            var context = new ForgeBuildContext(null, null, BuildTarget.StandaloneWindows64, "unused", true);
            LogAssert.Expect(LogType.Error, new Regex(
                $"Post-build restore failed in {Regex.Escape(typeof(NamelessApplicable).FullName)}: restore boom"));

            var notRestored = ForgeBuildRunner.RunPostBuild(new IForgePlugin[] { failing, restored }, context);

            Assert.IsTrue(restored.PostBuildRan, "The hook of a plugin whose name throws runs.");
            Assert.IsTrue(failing.PostBuildRan, "The hook of a plugin whose name throws runs.");
            CollectionAssert.AreEquivalent(
                new[] { EditorStateLedger.MakeKey(typeof(NamelessApplicable).FullName, BuildTargetGroup.Standalone) },
                notRestored);
            Assert.AreEqual(1, context.PostBuildFailures.Count);
            StringAssert.Contains(typeof(NamelessApplicable).FullName, context.PostBuildFailures[0]);
        }

        sealed class NamelessWarnings : IForgePlugin
        {
            readonly Func<IReadOnlyList<string>> validate;

            public NamelessWarnings(Func<IReadOnlyList<string>> validate) { this.validate = validate; }

            public string DisplayName => throw new InvalidOperationException("no name");
            public bool IsApplicable(BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
            public IReadOnlyList<string> Validate(ForgeProfile forgeProfile, string variant) => validate();
        }

        // Warnings never stop a build, and neither does the name of the plugin that gives them.
        [Test]
        public void BuildWarnings_ANameThatThrows_KeepsTheWarnings_WithTheTypeAsPrefix()
        {
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var settings = new SerializedObject(ForgeSettings.instance);
                settings.FindProperty("buildVariants").arraySize = 0;
                settings.FindProperty("maintainBuildProfileDefines").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var prefix = $"[Build Forge/{typeof(NamelessWarnings).FullName}]";

                CollectionAssert.AreEqual(new[]
                {
                    $"{prefix} First.",
                    $"{prefix} Checking the profile's settings failed: boom",
                }, ForgeBuildRunner.BuildWarnings(profile, null, new IForgePlugin[]
                {
                    new NamelessWarnings(() => new[] { "First." }),
                    new NamelessWarnings(() => throw new InvalidOperationException("boom")),
                }));
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        sealed class NamelessNotApplicable : IForgePlugin
        {
            public string DisplayName => throw new InvalidOperationException("no name");
            public bool IsApplicable(BuildProfile buildProfile) => false;
            public string NotApplicableReason(BuildProfile buildProfile) => "it builds for Android only.";
            public bool? IsEnabled(ForgeProfile forgeProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        [Test]
        public void PluginsFor_ANameThatThrows_KeepsTheNotApplicableNote()
        {
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex(
                    $"{Regex.Escape(typeof(NamelessNotApplicable).FullName)} is on for .* doesn't run: it builds for Android only\\."));

                var plugins = ForgeBuildRunner.PluginsFor(new IForgePlugin[] { new NamelessNotApplicable() }, profile, null);

                CollectionAssert.IsEmpty(plugins);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        sealed class NamelessProcessor : IForgePlugin, IForgeGradleProcessor
        {
            internal readonly List<string> Calls = new();
            readonly Exception failure;

            public NamelessProcessor(Exception failure = null) { this.failure = failure; }

            public string DisplayName => throw new InvalidOperationException("no name");
            public bool IsApplicable(BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }

            public void OnPostGenerateGradleAndroidProject(ForgeBuildContext context, string path)
            {
                Calls.Add(path);
                if (failure != null)
                    throw failure;
            }
        }

        // The name used to be read before the step and again for its error, which it replaced.
        [Test]
        public void GradleStep_ANameThatThrows_RunsTheStep_AndKeepsItsError()
        {
            var working = new NamelessProcessor();
            var broken = new NamelessProcessor(new InvalidOperationException("no manifest"));

            var failure = Assert.Throws<BuildFailedException>(() =>
                ForgeGradleCallback.Dispatch(null, new IForgePlugin[] { working, broken }, "lib"));

            CollectionAssert.AreEqual(new[] { "lib" }, working.Calls);
            CollectionAssert.AreEqual(new[] { "lib" }, broken.Calls);
            StringAssert.Contains($"The Gradle step of {typeof(NamelessProcessor).FullName} failed: no manifest", failure.Message);
        }
    }
}
