using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Editor.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.TestTools;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// A plugin's exception is plugin code too: wherever Build Forge reports one, its message and
    /// stack trace are read guarded, so one whose message throws is named by its type and doesn't
    /// end the failure handling. These plugins have no ForgePlugin attribute, so discovery outside
    /// these tests doesn't find them.
    /// </summary>
    public class PluginExceptionFailureTests
    {
        // An exception whose message and stack trace can't be read, as one that formats a field the
        // failure left null.
        sealed class Unreadable : Exception
        {
            public override string Message => throw new InvalidOperationException("message unavailable");
            public override string StackTrace => throw new InvalidOperationException("stack trace unavailable");
        }

        static readonly string UnreadableType = typeof(Unreadable).FullName;

        [Test]
        public void AnExceptionThatCantBeRead_IsNamedByItsType_WithoutAStackTrace()
        {
            var unreadable = new Unreadable();

            Assert.AreEqual(UnreadableType, ForgeEditorState.MessageOrType(unreadable));
            Assert.IsNull(ForgeEditorState.StackTraceOrNull(unreadable));
            Assert.AreEqual("boom", ForgeEditorState.MessageOrType(new InvalidOperationException("boom")));
        }

        class Cleanup : IForgePlugin
        {
            internal bool Ran;
            readonly Exception failure;

            public Cleanup(Exception failure = null) { this.failure = failure; }

            public virtual string DisplayName => "Cleanup";
            public bool IsApplicable(BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context)
            {
                Ran = true;
                if (failure != null)
                    throw failure;
            }
        }

        sealed class EditorCleanup : Cleanup, IForgeEditorApplicable
        {
            public EditorCleanup(Exception failure = null) : base(failure) { }

            public override string DisplayName => "Editor cleanup";
            public string CaptureEditorState(BuildTargetGroup group) => null;
            public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group, string variant) { }
            public void RestoreEditorState(BuildTargetGroup group, string json) { }
        }

        static readonly string EditorCleanupKey = EditorStateLedger.MakeKey(typeof(EditorCleanup).FullName, BuildTargetGroup.Standalone);

        // The failure's message read the exception's, which threw, and took the remaining hooks along.
        [Test]
        public void RunPostBuild_AnExceptionThatCantBeRead_StillRunsTheRemainingHooks()
        {
            var remaining = new Cleanup();
            var failing = new EditorCleanup(new Unreadable());
            var context = new ForgeBuildContext(null, null, BuildTarget.StandaloneWindows64, "unused", true);
            LogAssert.Expect(LogType.Error, new Regex($"Post-build restore failed in Editor cleanup: {Regex.Escape(UnreadableType)}"));

            // Post-build hooks run in reverse order, so the failing one runs first.
            var notRestored = ForgeBuildRunner.RunPostBuild(new IForgePlugin[] { remaining, failing }, context);

            Assert.IsTrue(failing.Ran);
            Assert.IsTrue(remaining.Ran, "The hooks after one whose exception can't be read still run.");
            CollectionAssert.AreEquivalent(new[] { EditorCleanupKey }, notRestored);
            CollectionAssert.AreEqual(new[] { $"Post-build restore failed in Editor cleanup: {UnreadableType}" }, context.PostBuildFailures);
        }

        // What the guard around the whole post-build loop records. Only a fault of Build Forge's own
        // gets there now that the loop reads every plugin exception guarded, so it is tested directly.
        [Test]
        public void RecordPostBuildEscape_NamesTheFailureByType_AndCountsEveryEditorPluginThatRanAsNotRestored()
        {
            var context = new ForgeBuildContext(null, null, BuildTarget.StandaloneWindows64, "unused", true);
            LogAssert.Expect(LogType.Error, new Regex($"Post-build hooks failed: {Regex.Escape(UnreadableType)}"));

            var notRestored = ForgeBuildRunner.RecordPostBuildEscape(
                new IForgePlugin[] { new Cleanup(), new EditorCleanup() }, context, new Unreadable());

            CollectionAssert.AreEquivalent(new[] { EditorCleanupKey }, notRestored);
            CollectionAssert.AreEqual(new[] { $"Post-build hooks failed: {UnreadableType}" }, context.PostBuildFailures);
        }

        sealed class UnreadableWarnings : IForgePlugin
        {
            public string DisplayName => "Warnings";
            public bool IsApplicable(BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
            public IReadOnlyList<string> Validate(ForgeProfile forgeProfile, string variant) => throw new Unreadable();
        }

        [Test]
        public void BuildWarnings_AValidateWhoseExceptionCantBeRead_KeepsItsWarningLine()
        {
            var previousSettings = EditorJsonUtility.ToJson(ForgeSettings.instance);
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var settings = new SerializedObject(ForgeSettings.instance);
                settings.FindProperty("buildVariants").arraySize = 0;
                settings.FindProperty("maintainBuildProfileDefines").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();

                CollectionAssert.AreEqual(
                    new[] { $"[Build Forge/Warnings] Checking the profile's settings failed: {UnreadableType}" },
                    ForgeBuildRunner.BuildWarnings(profile, null, new IForgePlugin[] { new UnreadableWarnings() }));
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(previousSettings, ForgeSettings.instance);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        sealed class UnreadableProcessor : IForgePlugin, IForgeGradleProcessor
        {
            public string DisplayName => "Processor";
            public bool IsApplicable(BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
            public void OnPostGenerateGradleAndroidProject(ForgeBuildContext context, string path) => throw new Unreadable();
        }

        [Test]
        public void GradleStep_AnExceptionThatCantBeRead_FailsTheStepByItsType()
        {
            var failure = Assert.Throws<BuildFailedException>(() =>
                ForgeGradleCallback.Dispatch(null, new IForgePlugin[] { new UnreadableProcessor() }, "lib"));

            StringAssert.Contains($"The Gradle step of Processor failed: {UnreadableType}", failure.Message);
        }

        sealed class UnreadableConstructor : IForgePlugin
        {
            public UnreadableConstructor() => throw new Unreadable();

            public string DisplayName => "Constructor";
            public bool IsApplicable(BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        sealed class UnreadableOrder : IForgePlugin
        {
            public string DisplayName => "Order";
            public int Order => throw new Unreadable();
            public bool IsApplicable(BuildProfile buildProfile) => true;
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        [Test]
        public void Discover_APluginThatThrowsAnExceptionThatCantBeRead_FailsToLoad_NamedByTheExceptionsType()
        {
            var failures = new List<(Type Type, string Error)>();
            LogAssert.Expect(LogType.Error, new Regex(
                $"Failed to instantiate plugin {Regex.Escape(typeof(UnreadableConstructor).FullName)}: {Regex.Escape(UnreadableType)}"));
            LogAssert.Expect(LogType.Error, new Regex(
                $"Failed to load plugin {Regex.Escape(typeof(UnreadableOrder).FullName)}: Order threw: {Regex.Escape(UnreadableType)}"));

            var plugins = ForgePluginRegistry.Discover(new[] { typeof(UnreadableConstructor), typeof(UnreadableOrder) }, failures);

            CollectionAssert.IsEmpty(plugins);
            CollectionAssert.AreEqual(new[] { UnreadableType, $"Order threw: {UnreadableType}" }, failures.Select(f => f.Error));
        }

        sealed class UnreadableApplicability : IForgePlugin
        {
            public string DisplayName => "Applicability";
            public bool IsApplicable(BuildProfile buildProfile) => throw new Unreadable();
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
        }

        [Test]
        public void BlockingError_APluginWhoseApplicabilityThrowsAnExceptionThatCantBeRead_NamesTheExceptionsType()
        {
            var native = VariantRulesTests.CreateNativeProfile();
            var forge = ScriptableObject.CreateInstance<ForgeProfile>();
            try
            {
                var fp = new SerializedObject(forge);
                fp.FindProperty("buildProfile").objectReferenceValue = native;
                fp.ApplyModifiedPropertiesWithoutUndo();

                var error = ForgePluginRegistry.BlockingError(forge, new IForgePlugin[] { new UnreadableApplicability() });

                StringAssert.Contains($"(UnreadableApplicability.IsApplicable threw: {UnreadableType})", error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(forge);
                UnityEngine.Object.DestroyImmediate(native);
            }
        }

        [Test]
        public void PluginDisplay_AValueWhoseExceptionCantBeRead_GetsTheFallback_AndTheErrorNamesTheExceptionsType()
        {
            PluginDisplay.ForgetReported();
            LogAssert.Expect(LogType.Error, new Regex($"Cleanup\\.Description threw: {Regex.Escape(UnreadableType)}"));

            var value = PluginDisplay.Try(new Cleanup(), "Description", () => throw new Unreadable(), "fallback", out var error);

            Assert.AreEqual("fallback", value);
            Assert.AreEqual($"Cleanup.Description threw: {UnreadableType}", error);
        }

        // Writes its one setting on Apply, as a plugin that fails halfway through.
        sealed class HalfwayPlugin : IForgeEditorApplicable
        {
            internal string Setting = "base";
            internal Exception OnApply, OnRestore;

            public string CaptureEditorState(BuildTargetGroup group) => Setting;

            public void ApplyToEditor(ForgeProfile forgeProfile, BuildTargetGroup group, string variant)
            {
                Setting = "applied";
                if (OnApply != null)
                    throw OnApply;
            }

            public void RestoreEditorState(BuildTargetGroup group, string json)
            {
                if (OnRestore != null)
                    throw OnRestore;
                Setting = json;
            }
        }

        const string Halfway = "Halfway";
        HalfwayPlugin halfway;
        List<EditorStateEntry> entries;
        ForgeProfile profile;

        Func<string, IForgeEditorApplicable> Resolve => name => name == Halfway ? halfway : null;

        EditorStatePair[] Pairs => new[] { new EditorStatePair { PluginTypeName = Halfway, Group = BuildTargetGroup.Standalone, Plugin = halfway } };

        [SetUp]
        public void SetUp()
        {
            halfway = new HalfwayPlugin();
            entries = new List<EditorStateEntry>();
            profile = ScriptableObject.CreateInstance<ForgeProfile>();
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(profile);

        // The stopped Apply's exception read the plugin's message as it was made, which threw before
        // the Apply could put back what it wrote.
        [Test]
        public void Apply_AnApplyWhoseExceptionCantBeRead_StopsAndPutsBackWhatItWrote()
        {
            halfway.OnApply = new Unreadable();
            LogAssert.Expect(LogType.Error, new Regex($"apply failed\\. {Regex.Escape(UnreadableType)}"));

            var stopped = Assert.Throws<EditorStateApplyException>(() =>
                EditorStateApplier.Apply(entries, Pairs, profile, Resolve, null));

            Assert.AreEqual(UnreadableType, stopped.Reason);
            Assert.AreEqual("base", halfway.Setting, "The Apply's write is put back.");
            CollectionAssert.IsEmpty(entries);
        }

        [Test]
        public void RevertAll_ARestoreWhoseExceptionCantBeRead_KeepsTheEntry_AndNamesTheExceptionsType()
        {
            EditorStateApplier.Apply(entries, Pairs, profile, Resolve, null);
            halfway.OnRestore = new Unreadable();
            LogAssert.Expect(LogType.Error, new Regex($"restore failed\\. {Regex.Escape(UnreadableType)}"));

            var failure = Assert.Throws<AggregateException>(() => EditorStateApplier.RevertAll(entries, Resolve));

            var restore = (EditorStateRestoreException)failure.InnerExceptions.Single();
            Assert.AreEqual($"restore failed. {UnreadableType}", restore.Reason);
            Assert.AreEqual(1, entries.Count, "The entry stays for a retry.");
        }

        [Test]
        public void ResumeAfterBuild_AReapplyWhoseExceptionCantBeRead_IsReported_AndTheEntryStaysSuspended()
        {
            EditorStateApplier.Apply(entries, Pairs, profile, Resolve, null);
            EditorStateApplier.SuspendForBuild(entries, Resolve, profile, null);
            halfway.OnApply = new Unreadable();
            LogAssert.Expect(LogType.Error, new Regex($"re-apply after build failed\\. {Regex.Escape(UnreadableType)}"));

            var result = EditorStateApplier.ResumeAfterBuild(entries, profile, Resolve);

            Assert.AreEqual(1, result.Failures.Count);
            Assert.AreEqual(EditorStatePhase.Suspended, entries.Single().phase);
        }
    }
}
