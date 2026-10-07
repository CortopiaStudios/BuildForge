using System.Collections.Generic;
using BuildForge.Editor.Core;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// Build variants: project-wide names that add one BUILD_VARIANT_&lt;NAME&gt;
    /// define. Naming, strict resolution, list validation, and the define-list
    /// arithmetic used at build time and for editor iteration.
    /// </summary>
    public class BuildVariantsTests
    {
        static readonly List<string> Variants = new() { "Internal", "QA Build" };

        // The window remembers its selection per editor session; a fresh editor
        // has nothing stored and must start on the applied variant (a stored
        // default must not be mistaken for "nothing stored").
        [Test]
        public void RestoreSelection_NothingStored_FollowsAppliedVariant()
        {
            Assert.AreEqual("Internal", BuildVariants.RestoreSelection("", "Internal"));
            Assert.IsNull(BuildVariants.RestoreSelection("", null));
            Assert.IsNull(BuildVariants.RestoreSelection(null, ""));
        }

        [Test]
        public void RestoreSelection_StoredValueWins()
        {
            Assert.IsNull(BuildVariants.RestoreSelection(BuildVariants.DefaultLabel, "Internal"));
            Assert.AreEqual("QA Build", BuildVariants.RestoreSelection("QA Build", "Internal"));
        }

        [Test]
        public void StoreSelection_RoundTrips()
        {
            Assert.AreEqual("Internal", BuildVariants.RestoreSelection(BuildVariants.StoreSelection("Internal"), null));
            Assert.IsNull(BuildVariants.RestoreSelection(BuildVariants.StoreSelection(null), "Internal"));
        }

        [TestCase("Internal", "BUILD_VARIANT_INTERNAL")]
        [TestCase("QA Build", "BUILD_VARIANT_QA_BUILD")]
        [TestCase("internal-v2", "BUILD_VARIANT_INTERNAL_V2")]
        public void DefineFor_UsesTheProfileSanitizer_WithVariantPrefix(string name, string expected)
        {
            Assert.AreEqual(expected, BuildVariants.DefineFor(name));
        }

        [Test]
        public void Resolve_NoName_IsNoVariant_NoError()
        {
            Assert.IsNull(BuildVariants.Resolve(null, Variants, out var e1));
            Assert.IsNull(e1);
            Assert.IsNull(BuildVariants.Resolve("  ", Variants, out var e2));
            Assert.IsNull(e2);
        }

        [Test]
        public void Resolve_KnownName_ReturnsIt_TrimmingWhitespace()
        {
            Assert.AreEqual("Internal", BuildVariants.Resolve(" Internal ", Variants, out var error));
            Assert.IsNull(error);
        }

        [Test]
        public void Resolve_DefaultName_IsTheDefaultBuild_NoError()
        {
            Assert.IsNull(BuildVariants.Resolve("default", Variants, out var e1));
            Assert.IsNull(e1);
            Assert.IsNull(BuildVariants.Resolve("Default", Variants, out var e2));
            Assert.IsNull(e2);
            Assert.IsNull(BuildVariants.Resolve("<default>", Variants, out var e3));
            Assert.IsNull(e3);
        }

        [Test]
        public void EffectiveName_DefaultBuild_IsDefault_OnlyWhenVariantsAreConfigured()
        {
            Assert.AreEqual("Internal", BuildVariants.EffectiveName("Internal", true));
            Assert.AreEqual("Default", BuildVariants.EffectiveName(null, true));
            Assert.IsNull(BuildVariants.EffectiveName(null, false));
        }

        [Test]
        public void MarkedProductName_SuffixesVariantBuilds_NeverTheDefault()
        {
            Assert.AreEqual("Game (Internal)", BuildVariants.MarkedProductName("Game", "Internal", true));
            Assert.AreEqual("Game", BuildVariants.MarkedProductName("Game", null, true), "Default build (no variant).");
            Assert.AreEqual("Game", BuildVariants.MarkedProductName("Game", "Default", true), "Default build (by name).");
            Assert.AreEqual("Game", BuildVariants.MarkedProductName("Game", "Internal", false), "Marking off.");
        }

        [Test]
        public void ExpectedDefine_DefaultBuild_OnlyWhenVariantsAreConfigured()
        {
            Assert.AreEqual("BUILD_VARIANT_INTERNAL", BuildVariants.ExpectedDefine("Internal", true));
            Assert.AreEqual("BUILD_VARIANT_DEFAULT", BuildVariants.ExpectedDefine(null, true));
            Assert.IsNull(BuildVariants.ExpectedDefine(null, false));
        }

        [Test]
        public void Resolve_UnknownName_IsAnError_ListingTheVariants()
        {
            Assert.IsNull(BuildVariants.Resolve("Nightly", Variants, out var error));
            StringAssert.Contains("'Nightly'", error);
            StringAssert.Contains("'Internal'", error);
            StringAssert.Contains("'QA Build'", error);
        }

        [Test]
        public void Resolve_UnknownName_WithNoVariantsDefined_SaysSo()
        {
            Assert.IsNull(BuildVariants.Resolve("Internal", new List<string>(), out var error));
            StringAssert.Contains("no variants are defined", error);
        }

        [Test]
        public void ListWarning_FlagsEmpty_Duplicate_AndCollidingNames()
        {
            Assert.IsNull(BuildVariants.ListWarning(null));
            Assert.IsNull(BuildVariants.ListWarning(Variants));
            StringAssert.Contains("empty", BuildVariants.ListWarning(new List<string> { "Internal", " " }));
            StringAssert.Contains("more than once", BuildVariants.ListWarning(new List<string> { "Internal", "Internal" }));
            var collision = BuildVariants.ListWarning(new List<string> { "QA Build", "QA-Build" });
            StringAssert.Contains("BUILD_VARIANT_QA_BUILD", collision);
            StringAssert.Contains("'<'", BuildVariants.ListWarning(new List<string> { "<default>" }));
            StringAssert.Contains("reserved", BuildVariants.ListWarning(new List<string> { "Default" }));
            StringAssert.Contains("reserved", BuildVariants.ListWarning(new List<string> { " default " }));
            StringAssert.Contains("no letters or digits", BuildVariants.ListWarning(new List<string> { "!!!" }));
        }

        [Test]
        public void WithVariantDefine_Adds_Replaces_Removes_KeepsOtherDefines()
        {
            var added = BuildVariants.WithVariantDefine(new[] { "USER", "BUILD_PROFILE_QUEST" }, "Internal", true, out var c1);
            Assert.IsTrue(c1);
            CollectionAssert.AreEqual(new[] { "USER", "BUILD_PROFILE_QUEST", "BUILD_VARIANT_INTERNAL" }, added);

            var replaced = BuildVariants.WithVariantDefine(added, "QA Build", true, out var c2);
            Assert.IsTrue(c2);
            CollectionAssert.AreEqual(new[] { "USER", "BUILD_PROFILE_QUEST", "BUILD_VARIANT_QA_BUILD" }, replaced);

            var stripped = BuildVariants.WithVariantDefine(replaced, null, false, out var c3);
            Assert.IsTrue(c3);
            CollectionAssert.AreEqual(new[] { "USER", "BUILD_PROFILE_QUEST" }, stripped);

            BuildVariants.WithVariantDefine(stripped, null, false, out var c4);
            Assert.IsFalse(c4, "Nothing to strip means no change.");
            BuildVariants.WithVariantDefine(added, "Internal", true, out var c5);
            Assert.IsFalse(c5, "Already present means no change.");
        }

        [Test]
        public void WithVariantDefine_DefaultBuild_GetsTheDefaultDefine_OnlyWithVariantsConfigured()
        {
            var withVariants = BuildVariants.WithVariantDefine(new[] { "USER", "BUILD_VARIANT_INTERNAL" }, null, true, out var c1);
            Assert.IsTrue(c1);
            CollectionAssert.AreEqual(new[] { "USER", "BUILD_VARIANT_DEFAULT" }, withVariants);

            var none = BuildVariants.WithVariantDefine(new[] { "USER", "BUILD_VARIANT_DEFAULT" }, null, false, out var c2);
            Assert.IsTrue(c2);
            CollectionAssert.AreEqual(new[] { "USER" }, none);
        }

        [Test]
        public void WithVariantDefine_NullList_IsJustTheDefine()
        {
            CollectionAssert.AreEqual(new[] { "BUILD_VARIANT_INTERNAL" },
                BuildVariants.WithVariantDefine(null, "Internal", true, out _));
            CollectionAssert.AreEqual(new[] { "BUILD_VARIANT_DEFAULT" },
                BuildVariants.WithVariantDefine(null, null, true, out _));
            CollectionAssert.IsEmpty(BuildVariants.WithVariantDefine(null, null, false, out _));
        }

        // A variant define on a profile this machine has not applied was saved
        // elsewhere (committed while applied); where it is applied it is expected.
        [Test]
        public void StrayDefines_OnlyWhenNotAppliedHere()
        {
            var defines = new[] { "USER", "BUILD_VARIANT_INTERNAL", "INTERNAL_TOOLS" };
            CollectionAssert.AreEqual(new[] { "BUILD_VARIANT_INTERNAL" }, BuildVariants.StrayDefines(defines, false));
            CollectionAssert.IsEmpty(BuildVariants.StrayDefines(defines, true));
            CollectionAssert.IsEmpty(BuildVariants.StrayDefines(new[] { "USER" }, false));
            CollectionAssert.IsEmpty(BuildVariants.StrayDefines(null, false));
        }
    }
}
