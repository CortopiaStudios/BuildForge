using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Core;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    public class BuildEnvironmentTests
    {
        [TestCase(".", "Assets/Profiles/Release.asset", null)]
        [TestCase("./Apps/My Game", "Assets/Profiles/Steam Frame.asset", "Development")]
        [TestCase("C:/Outside Repo/My Game", "Assets/Profiles/Pico.asset", "ChinaDevelopment")]
        public void CICommand_ForwardsForgeArgumentsWithoutCliOwnedFlags(string project, string profile, string variant)
        {
            var command = ForgeBuildRunner.FormatCICommand(project, profile, variant);
            StringAssert.StartsWith($"unity run \"{project}\" --non-interactive -- ", command);
            var forwarded = command.Substring(command.IndexOf(" -- ", System.StringComparison.Ordinal) + 4);
            StringAssert.Contains("-executeMethod BuildForge.CommandLine.Build", forwarded);
            StringAssert.Contains($"-activeBuildProfile \"{profile}\"", forwarded);
            StringAssert.Contains("-cacheServerWaitForUploadCompletion", forwarded);
            foreach (var reserved in new[] { "-batchmode", "-quit", "-projectPath" })
                StringAssert.DoesNotContain(reserved, forwarded);
            if (variant == null) StringAssert.DoesNotContain("-forgeVariant", forwarded);
            else StringAssert.EndsWith($"-forgeVariant \"{variant}\"", forwarded);
        }

        [Test]
        public void ForwardedCIArguments_KeepTheirValue()
        {
            // A public constant, compiled into callers.
            Assert.AreEqual("-nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion " +
                            "-executeMethod BuildForge.CommandLine.Build", ForgeBuildRunner.ForwardedCIArguments);
        }

        [TestCase(".", "Assets/Profiles/Release.asset")]
        [TestCase("./Apps/My Game", "Assets/Profiles/Steam Frame.asset")]
        public void ActivateCommand_ForwardsActivateArgumentsWithoutBuildOrCliOwnedFlags(string project, string profile)
        {
            var command = ForgeBuildRunner.FormatActivateCommand(project, profile);
            StringAssert.StartsWith($"unity run \"{project}\" --non-interactive -- ", command);
            var forwarded = command.Substring(command.IndexOf(" -- ", System.StringComparison.Ordinal) + 4);
            StringAssert.Contains("-executeMethod BuildForge.CommandLine.Activate", forwarded);
            StringAssert.EndsWith($"-forgeBuildProfile \"{profile}\"", forwarded);
            StringAssert.Contains("-cacheServerWaitForUploadCompletion", forwarded);
            foreach (var other in new[] { "-batchmode", "-quit", "-projectPath", "-activeBuildProfile", "-forgeVariant" })
                StringAssert.DoesNotContain(other, forwarded);
        }

        [Test]
        public void SharedWorkspaceCICommand_IsActivateThenTheBuild()
        {
            var lines = ForgeBuildRunner.FormatSharedWorkspaceCICommand(".", "Assets/Quest.asset", "Internal").Split('\n');
            CollectionAssert.AreEqual(new[]
            {
                ForgeBuildRunner.FormatActivateCommand(".", "Assets/Quest.asset"),
                ForgeBuildRunner.FormatCICommand(".", "Assets/Quest.asset", "Internal"),
            }, lines);
        }

        static readonly string[] NoArgs = { "Unity", "-batchmode" };

        [Test]
        public void ResolveIsCI_NoSignals_FallsBackToBatchMode()
        {
            Assert.IsTrue(BuildEnvironment.ResolveIsCI(NoArgs, null, isBatchMode: true));
            Assert.IsFalse(BuildEnvironment.ResolveIsCI(NoArgs, null, isBatchMode: false));
        }

        [Test]
        public void ResolveIsCI_EnvVar_OverridesBatchMode()
        {
            Assert.IsTrue(BuildEnvironment.ResolveIsCI(NoArgs, "1", isBatchMode: false));
            Assert.IsTrue(BuildEnvironment.ResolveIsCI(NoArgs, "true", isBatchMode: false));
            Assert.IsFalse(BuildEnvironment.ResolveIsCI(NoArgs, "0", isBatchMode: true));
            Assert.IsFalse(BuildEnvironment.ResolveIsCI(NoArgs, "false", isBatchMode: true));
        }

        [Test]
        public void ResolveIsCI_UnparseableEnvVar_IsIgnored()
        {
            Assert.IsTrue(BuildEnvironment.ResolveIsCI(NoArgs, "maybe", isBatchMode: true));
            Assert.IsFalse(BuildEnvironment.ResolveIsCI(NoArgs, "", isBatchMode: false));
        }

        [Test]
        public void ResolveIsCI_BareArg_MeansTrue()
        {
            var args = new[] { "Unity", "-forgeCI" };
            Assert.IsTrue(BuildEnvironment.ResolveIsCI(args, "0", isBatchMode: false));

            var followedByFlag = new[] { "Unity", "-forgeCI", "-quit" };
            Assert.IsTrue(BuildEnvironment.ResolveIsCI(followedByFlag, "0", isBatchMode: false));
        }

        [Test]
        public void ResolveIsCI_ArgWithValue_OverridesEverything()
        {
            var off = new[] { "Unity", "-batchmode", "-forgeCI", "false" };
            Assert.IsFalse(BuildEnvironment.ResolveIsCI(off, "1", isBatchMode: true));

            var on = new[] { "Unity", "-forgeCI", "true" };
            Assert.IsTrue(BuildEnvironment.ResolveIsCI(on, "0", isBatchMode: false));
        }

        [Test]
        public void ResolveIsCI_ArgIsCaseInsensitive()
        {
            var args = new[] { "Unity", "-forgeci", "1" };
            Assert.IsTrue(BuildEnvironment.ResolveIsCI(args, null, isBatchMode: false));
        }

        [Test]
        public void ResolveIsCI_NullArgs_DoesNotThrow()
        {
            Assert.IsFalse(BuildEnvironment.ResolveIsCI(null, null, isBatchMode: false));
        }

        [Test]
        public void ValueError_UnrecognizedForgeCIValue_IsAnError()
        {
            StringAssert.Contains("'flase'", BuildEnvironment.ValueError(new[] { "Unity", "-forgeCI", "flase" }, null));
            StringAssert.Contains("''", BuildEnvironment.ValueError(new[] { "Unity", "-forgeCI", "" }, null));
        }

        [Test]
        public void ValueError_UnrecognizedEnvVar_IsAnError()
        {
            StringAssert.Contains("'off'", BuildEnvironment.ValueError(NoArgs, "off"));
        }

        [Test]
        public void ValueError_ReadableOrAbsentSignals_AreFine()
        {
            Assert.IsNull(BuildEnvironment.ValueError(new[] { "Unity", "-forgeCI" }, null));
            Assert.IsNull(BuildEnvironment.ValueError(new[] { "Unity", "-forgeCI", "-quit" }, ""));
            Assert.IsNull(BuildEnvironment.ValueError(new[] { "Unity", "-forgeci", "No" }, "1"));
            Assert.IsNull(BuildEnvironment.ValueError(null, "  "));
        }

        static readonly string[] CliPrefix =
            { "Unity", "-logFile", "-", "-executeMethod", "BuildForge.CommandLine.Build", "-activeBuildProfile", "Assets/Quest.asset" };

        static Dictionary<string, string> Parse(out string error, params string[] args) =>
            ForgeBuildRunner.ParseCommandLine(CliPrefix.Concat(args).ToArray(), out error);

        [Test]
        public void ParseCommandLine_ReadsValues_NamesAreCaseInsensitive()
        {
            var values = Parse(out var error, "-forgevariant", "Internal", "-FORGEVERSION", "1.2.3", "-forgeversioncode", "42");
            Assert.IsNull(error);
            Assert.AreEqual("Internal", values["-forgeVariant"]);
            Assert.AreEqual("1.2.3", values["-forgeVersion"]);
            Assert.AreEqual("42", values["-forgeVersionCode"]);
        }

        [Test]
        public void ParseCommandLine_OtherArguments_AreIgnored()
        {
            var values = Parse(out var error, "-forgeCI", "-nographics", "-forgeVariant", "Internal", "-silent-crashes");
            Assert.IsNull(error);
            CollectionAssert.AreEquivalent(new[] { "-forgeVariant" }, values.Keys);
            CollectionAssert.IsEmpty(ForgeBuildRunner.ParseCommandLine(null, out error));
            Assert.IsNull(error);
        }

        [Test]
        public void ParseCommandLine_ArgumentWithoutValue_IsAnError()
        {
            // Last on the line (an unquoted, unset CI variable), followed by
            // another flag, or empty or blank (a quoted, unset CI variable).
            AssertNeedsValue("-forgeVariant");
            AssertNeedsValue("-forgeVersion", "-forgeVersionCode", "42");
            AssertNeedsValue("-forgeVersionCode", "");
            AssertNeedsValue("-forgeVariant", "  ");
        }

        static void AssertNeedsValue(params string[] args)
        {
            Assert.IsNull(Parse(out var error, args), string.Join(" ", args));
            StringAssert.StartsWith($"{args[0]} needs a value", error);
        }

        [Test]
        public void ParseCommandLine_EmptyVariant_PointsToTheDefaultBuild()
        {
            Parse(out var error, "-forgeVariant", "");
            StringAssert.Contains("pass -forgeVariant Default", error);
        }

        [Test]
        public void ParseCommandLine_FormerVersionCodeName_IsAnError()
        {
            Assert.IsNull(Parse(out var error, "-versionCode", "42"));
            Assert.AreEqual("-versionCode was renamed to -forgeVersionCode.", error);
        }

        [TestCase("-forgeVarient")]
        [TestCase("-forgeProfile")] // removed; the profile now comes from -activeBuildProfile
        public void ParseCommandLine_UnknownForgeArgument_IsAnError(string argument)
        {
            Assert.IsNull(Parse(out var error, argument, "Internal"));
            StringAssert.StartsWith($"Unknown argument '{argument}'", error);
        }

        [Test]
        public void ParseCommandLine_ActivateArgument_PointsToActivate()
        {
            Assert.IsNull(Parse(out var error, "-forgeBuildProfile", "Assets/Quest.asset"));
            StringAssert.StartsWith("-forgeBuildProfile is BuildForge.CommandLine.Activate's argument.", error);
        }

        static readonly string[] ActivatePrefix =
            { "Unity", "-logFile", "-", "-executeMethod", "BuildForge.CommandLine.Activate" };

        static string ParseActivate(out string error, params string[] args) =>
            ForgeEditorState.ParseActivateCommandLine(ActivatePrefix.Concat(args).ToArray(), out error);

        [Test]
        public void ParseActivateCommandLine_ReadsThePath_NameIsCaseInsensitive()
        {
            Assert.AreEqual("Assets/Settings/Build Profiles/Steam Frame.asset",
                ParseActivate(out var error, "-forgebuildprofile", "Assets/Settings/Build Profiles/Steam Frame.asset", "-nographics"));
            Assert.IsNull(error);
        }

        [Test]
        public void ParseActivateCommandLine_UnityArguments_AreLeftAlone()
        {
            Assert.AreEqual("Assets/Pico.asset", ParseActivate(out var error,
                "-activeBuildProfile", "Assets/Quest.asset", "-forgeBuildProfile", "Assets/Pico.asset", "-silent-crashes"));
            Assert.IsNull(error);
        }

        [Test]
        public void ParseActivateCommandLine_MissingArgument_IsAnError()
        {
            Assert.IsNull(ParseActivate(out var error, "-nographics"));
            Assert.AreEqual("Pass -forgeBuildProfile \"<path to the Unity Build Profile asset>\".", error);
            Assert.IsNull(ForgeEditorState.ParseActivateCommandLine(null, out error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void ParseActivateCommandLine_ArgumentWithoutValue_IsAnError()
        {
            AssertActivateNeedsValue("-forgeBuildProfile");
            AssertActivateNeedsValue("-forgeBuildProfile", "-nographics");
            AssertActivateNeedsValue("-forgeBuildProfile", "");
            AssertActivateNeedsValue("-forgeBuildProfile", "  ");
        }

        static void AssertActivateNeedsValue(params string[] args)
        {
            Assert.IsNull(ParseActivate(out var error, args), string.Join(" ", args));
            StringAssert.StartsWith("-forgeBuildProfile needs a value", error);
        }

        // Activate takes no build arguments; a run that ignored one would still succeed.
        [TestCase("-forgeVariant")]
        [TestCase("-forgeCI")]
        [TestCase("-forgeBuildProfil")]
        public void ParseActivateCommandLine_OtherForgeArgument_IsAnError(string argument)
        {
            Assert.IsNull(ParseActivate(out var error, "-forgeBuildProfile", "Assets/Quest.asset", argument, "Internal"));
            Assert.AreEqual($"Unknown argument '{argument}'. Activate takes only -forgeBuildProfile.", error);
        }

        [Test]
        public void RelativeProjectPath_RootItself_IsDot()
        {
            Assert.AreEqual(".", ForgeBuildRunner.RelativeProjectPath(@"C:\repo", @"C:\repo"));
            Assert.AreEqual(".", ForgeBuildRunner.RelativeProjectPath(@"C:\repo\", "C:/repo"));
        }

        [Test]
        public void RelativeProjectPath_Subfolder_IsRelativeWithForwardSlashes()
        {
            Assert.AreEqual("./Client", ForgeBuildRunner.RelativeProjectPath(@"C:\repo\Client", @"C:\repo"));
            Assert.AreEqual("./Apps/Client", ForgeBuildRunner.RelativeProjectPath(@"C:\repo\Apps\Client", @"C:\repo"));
        }

        [Test]
        public void RelativeProjectPath_OutsideRepository_OrUnknown_IsNull()
        {
            Assert.IsNull(ForgeBuildRunner.RelativeProjectPath(@"C:\elsewhere\Client", @"C:\repo"));
            Assert.IsNull(ForgeBuildRunner.RelativeProjectPath(@"C:\repo2", @"C:\repo"), "A sibling with the root as prefix is not inside it.");
            Assert.IsNull(ForgeBuildRunner.RelativeProjectPath(@"C:\repo\Client", null));
            Assert.IsNull(ForgeBuildRunner.RelativeProjectPath(null, @"C:\repo"));
        }
    }
}
