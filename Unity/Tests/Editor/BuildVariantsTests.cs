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
        // has nothing stored and must start on the applied variant.
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
            Assert.AreEqual("QA Build", BuildVariants.RestoreSelection("QA Build", "Internal"));
        }

        [Test]
        public void StoreSelection_RoundTrips()
        {
            Assert.AreEqual("Internal", BuildVariants.RestoreSelection(BuildVariants.StoreSelection("Internal"), null));
            Assert.AreEqual("Internal", BuildVariants.RestoreSelection(BuildVariants.StoreSelection(null), "Internal"),
                "Nothing selected follows the applied variant.");
        }

        [TestCase("Internal", "BUILD_VARIANT_INTERNAL")]
        [TestCase("QA Build", "BUILD_VARIANT_QA_BUILD")]
        [TestCase("internal-v2", "BUILD_VARIANT_INTERNAL_V2")]
        public void DefineFor_UsesTheProfileSanitizer_WithVariantPrefix(string name, string expected)
        {
            Assert.AreEqual(expected, BuildVariants.DefineFor(name));
        }

        [Test]
        public void Resolve_NoName_WithoutVariants_IsNoVariant_NoError()
        {
            Assert.IsNull(BuildVariants.Resolve(null, new List<string>(), out var e1));
            Assert.IsNull(e1);
            Assert.IsNull(BuildVariants.Resolve("  ", null, out var e2));
            Assert.IsNull(e2);
        }

        // Once variants exist every build names one; nothing is picked for it.
        [Test]
        public void Resolve_NoName_WithVariants_IsAnError_ListingTheVariants()
        {
            Assert.IsNull(BuildVariants.Resolve(null, Variants, out var e1));
            StringAssert.Contains("No build variant given", e1);
            StringAssert.Contains("'Internal'", e1);
            StringAssert.Contains("'QA Build'", e1);
            Assert.IsNull(BuildVariants.Resolve("  ", Variants, out var e2));
            Assert.AreEqual(e1, e2);
        }

        [Test]
        public void Resolve_KnownName_ReturnsIt_TrimmingWhitespace()
        {
            Assert.AreEqual("Internal", BuildVariants.Resolve(" Internal ", Variants, out var error));
            Assert.IsNull(error);
        }

        [Test]
        public void Resolve_Default_IsAnOrdinaryName()
        {
            Assert.IsNull(BuildVariants.Resolve("Default", Variants, out var unknown));
            StringAssert.Contains("Unknown build variant 'Default'", unknown);
            var withDefault = new List<string> { "Default", "Internal" };
            Assert.AreEqual("Default", BuildVariants.Resolve("Default", withDefault, out var error));
            Assert.IsNull(error);
            Assert.IsNull(BuildVariants.Resolve("default", withDefault, out var casing), "Names match exactly, including case.");
            Assert.IsNotNull(casing);
        }

        [Test]
        public void MarkedProductName_SuffixesVariantBuilds()
        {
            Assert.AreEqual("Game (Internal)", BuildVariants.MarkedProductName("Game", "Internal", true));
            Assert.AreEqual("Game (Default)", BuildVariants.MarkedProductName("Game", "Default", true), "Default is an ordinary variant.");
            Assert.AreEqual("Game", BuildVariants.MarkedProductName("Game", null, true), "No variant, in a project without variants.");
            Assert.AreEqual("Game", BuildVariants.MarkedProductName("Game", "Internal", false), "Marking off.");
        }

        [Test]
        public void ExpectedDefine_IsTheVariantsDefine_NoneWithoutAVariant()
        {
            Assert.AreEqual("BUILD_VARIANT_INTERNAL", BuildVariants.ExpectedDefine("Internal"));
            Assert.AreEqual("BUILD_VARIANT_DEFAULT", BuildVariants.ExpectedDefine("Default"));
            Assert.IsNull(BuildVariants.ExpectedDefine(null));
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
        public void Resolve_AName_WithNoVariantsDefined_SaysSo()
        {
            Assert.IsNull(BuildVariants.Resolve("Internal", new List<string>(), out var error));
            StringAssert.Contains("no build variants", error);
        }

        [Test]
        public void ListWarning_FlagsEmpty_Duplicate_AndCollidingNames()
        {
            Assert.IsNull(BuildVariants.ListWarning(null));
            Assert.IsNull(BuildVariants.ListWarning(Variants));
            Assert.IsNull(BuildVariants.ListWarning(new List<string> { "Default", "Internal" }), "Default is an ordinary name.");
            StringAssert.Contains("empty", BuildVariants.ListWarning(new List<string> { "Internal", " " }));
            StringAssert.Contains("more than once", BuildVariants.ListWarning(new List<string> { "Internal", "Internal" }));
            var collision = BuildVariants.ListWarning(new List<string> { "QA Build", "QA-Build" });
            StringAssert.Contains("BUILD_VARIANT_QA_BUILD", collision);
        }

        // A variant name is a folder in output paths and an argument on the command line.
        [TestCase("Internal QA")] [TestCase("internal-v2")] [TestCase("Dev_2")] [TestCase("2D")]
        [TestCase("Übung")] [TestCase("内部")] [TestCase("COM10")] [TestCase("Console")]
        public void ListWarning_AcceptsLettersDigitsSpacesHyphensAndUnderscores(string name)
            => Assert.IsNull(BuildVariants.ListWarning(new List<string> { name }));

        [TestCase("QA/Test", "/")] [TestCase(@"QA\Test", @"\")] [TestCase("QA:1", ":")] [TestCase("Beta?", "?")]
        [TestCase("All*", "*")] [TestCase("A|B", "|")] [TestCase("<Beta>", "<")] [TestCase("Say \"hi\"", "\"")]
        [TestCase("v1.2", ".")] [TestCase("$HOME", "$")] [TestCase("100%", "%")] [TestCase("A&B", "&")]
        [TestCase("Q\tA", "\t")] [TestCase("!!!", "!")] [TestCase("COM¹", "¹")]
        public void ListWarning_RefusesOtherCharacters_NamingTheFirst(string name, string shown)
            => StringAssert.Contains($"contains '{shown}'", BuildVariants.ListWarning(new List<string> { "Internal", name }));

        [TestCase("_QA")] [TestCase("-QA")]
        public void ListWarning_RefusesANameThatDoesNotStartWithALetterOrDigit(string name)
            => StringAssert.Contains("must start with a letter or digit", BuildVariants.ListWarning(new List<string> { name }));

        [TestCase("CON")] [TestCase("nul")] [TestCase("Aux")] [TestCase("PRN")] [TestCase("COM0")] [TestCase("com9")] [TestCase("LPT1")]
        public void ListWarning_RefusesTheDeviceNamesWindowsReserves(string name)
            => StringAssert.Contains("device name Windows reserves", BuildVariants.ListWarning(new List<string> { name }));

        [Test]
        public void WithVariantDefine_Adds_Replaces_Removes_KeepsOtherDefines()
        {
            var added = BuildVariants.WithVariantDefine(new[] { "USER", "BUILD_PROFILE_QUEST" }, "Internal", out var c1);
            Assert.IsTrue(c1);
            CollectionAssert.AreEqual(new[] { "USER", "BUILD_PROFILE_QUEST", "BUILD_VARIANT_INTERNAL" }, added);

            var replaced = BuildVariants.WithVariantDefine(added, "QA Build", out var c2);
            Assert.IsTrue(c2);
            CollectionAssert.AreEqual(new[] { "USER", "BUILD_PROFILE_QUEST", "BUILD_VARIANT_QA_BUILD" }, replaced);

            var stripped = BuildVariants.WithVariantDefine(replaced, null, out var c3);
            Assert.IsTrue(c3);
            CollectionAssert.AreEqual(new[] { "USER", "BUILD_PROFILE_QUEST" }, stripped);

            BuildVariants.WithVariantDefine(stripped, null, out var c4);
            Assert.IsFalse(c4, "Nothing to strip means no change.");
            BuildVariants.WithVariantDefine(added, "Internal", out var c5);
            Assert.IsFalse(c5, "Already present means no change.");
        }

        [Test]
        public void WithVariantDefine_NullList_IsJustTheDefine()
        {
            CollectionAssert.AreEqual(new[] { "BUILD_VARIANT_INTERNAL" },
                BuildVariants.WithVariantDefine(null, "Internal", out _));
            CollectionAssert.IsEmpty(BuildVariants.WithVariantDefine(null, null, out _));
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
