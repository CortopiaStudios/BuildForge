using System;
using System.Collections.Generic;
using BuildForge.Editor.Core;
using NUnit.Framework;
using UnityEditor.Build;
using UnityEditor.Build.Profile;

namespace BuildForge.Tests.Editor
{
    public class ForgeGradleCallbackTests
    {
        class Plain : IForgePlugin
        {
            public virtual string DisplayName => "Plain";
            public void OnPreBuild(ForgeBuildContext context) { }
            public void OnPostBuild(ForgeBuildContext context) { }
            public bool IsApplicable(BuildProfile profile) => true;
        }

        class Processor : Plain, IForgeGradleProcessor
        {
            readonly string name;
            readonly List<string> calls;
            readonly Exception failure;

            public Processor(string name, List<string> calls, Exception failure = null)
            {
                this.name = name;
                this.calls = calls;
                this.failure = failure;
            }

            public override string DisplayName => name;

            public void OnPostGenerateGradleAndroidProject(ForgeBuildContext context, string path)
            {
                calls.Add($"{name} {path}");
                if (failure != null)
                    throw failure;
            }
        }

        [Test]
        public void Dispatch_CallsTheProcessorsInTheGivenOrder()
        {
            var calls = new List<string>();
            ForgeGradleCallback.Dispatch(null, new IForgePlugin[] { new Processor("B", calls), new Plain(), new Processor("A", calls) }, "lib");
            CollectionAssert.AreEqual(new[] { "B lib", "A lib" }, calls);
        }

        [Test]
        public void Dispatch_FailsTheBuildNamingThePlugin()
        {
            var calls = new List<string>();
            var failure = Assert.Throws<BuildFailedException>(() => ForgeGradleCallback.Dispatch(null,
                new IForgePlugin[] { new Processor("Broken", calls, new InvalidOperationException("no manifest")), new Processor("Next", calls) },
                "lib"));
            StringAssert.Contains("The Gradle step of Broken failed: no manifest", failure.Message);
            CollectionAssert.AreEqual(new[] { "Broken lib" }, calls, "The build stops at the failing step.");

            var own = new BuildFailedException("left over");
            Assert.AreSame(own, Assert.Throws<BuildFailedException>(() =>
                ForgeGradleCallback.Dispatch(null, new IForgePlugin[] { new Processor("Check", calls, own) }, "lib")));
        }

        [Test]
        public void CallbackOrder_RunsAfterMetasGradleStep()
            => Assert.Greater(ForgeGradleCallback.CallbackOrder, 99999);
    }
}
