using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Core;
using BuildForge.Editor.UI;
using NUnit.Framework;
using UnityEditor;
using ColumnKind = BuildForge.Editor.Core.PlayerSettingsMatrix.ColumnKind;

namespace BuildForge.Tests.Editor
{
    public class PlayerSettingsMatrixTests
    {
        static readonly Dictionary<string, string> Project = new()
        {
            ["productName"] = "My Game",
            ["bundleVersion"] = "1.12.0",
            ["AndroidMinSdkVersion"] = "32",
            ["androidSplitApplicationBinary"] = "0",
            ["scriptingBackend"] = "  Android: 1\n  Standalone: 0",
        };

        static Dictionary<string, string> OwnCopy(params (string key, string value)[] changes)
        {
            var map = new Dictionary<string, string>(Project);
            foreach (var (key, value) in changes)
                map[key] = value;
            return map;
        }

        static PlayerSettingsMatrix.Profile Profile(string name, BuildTarget target, Dictionary<string, string> playerSettings = null)
            => new PlayerSettingsMatrix.Profile { Name = name, Target = target, PlayerSettings = playerSettings };

        static string[] ColumnTitles(PlayerSettingsMatrix matrix)
            => matrix.Columns.Select(c => c.Kind == ColumnKind.ProjectSettings ? "Project Settings " + c.Platform : c.ProfileName).ToArray();

        // Two Android profiles have Player Settings of their own; the other two
        // build with Project Settings.
        [Test]
        public void RowsAreTheSettingsThatDifferAnywhere_InProjectSettingsOrder()
        {
            var matrix = PlayerSettingsMatrix.Build(new[]
            {
                Profile("Phone", BuildTarget.iOS),
                Profile("Quest", BuildTarget.Android, OwnCopy(("androidSplitApplicationBinary", "1"))),
                Profile("Steam Frame", BuildTarget.Android, OwnCopy(("AndroidMinSdkVersion", "30"))),
                Profile("SteamVR", BuildTarget.StandaloneWindows64),
            }, Project);

            CollectionAssert.AreEqual(new[] { "Project Settings Android", "Quest", "Steam Frame" }, ColumnTitles(matrix));
            CollectionAssert.AreEqual(new[] { 1, 1 }, matrix.Columns.Skip(1).Select(c => c.DiffCount));
            CollectionAssert.AreEqual(new[] { "Phone", "SteamVR" }, matrix.UsingProjectSettings);

            CollectionAssert.AreEqual(new[] { "AndroidMinSdkVersion", "androidSplitApplicationBinary" },
                matrix.Rows.Select(r => r.PropertyPath), "Project Settings order, not the order of the columns' differences.");
            CollectionAssert.AreEqual(new[] { "32", "32", "30" }, matrix.Rows[0].Values);
            CollectionAssert.AreEqual(new[] { false, false, true }, matrix.Rows[0].Differs);
            CollectionAssert.AreEqual(new[] { "0", "1", "0" }, matrix.Rows[1].Values);
            CollectionAssert.AreEqual(new[] { false, true, false }, matrix.Rows[1].Differs);
            Assert.IsFalse(matrix.Rows.Any(r => r.PossiblyStale));
        }

        [Test]
        public void ASettingEveryProfileHoldsUnlikeProjectSettings_IsPossiblyStale()
        {
            var matrix = PlayerSettingsMatrix.Build(new[]
            {
                Profile("Quest", BuildTarget.Android, OwnCopy(("bundleVersion", "1.11.0"), ("androidSplitApplicationBinary", "1"))),
                Profile("Steam Frame", BuildTarget.Android, OwnCopy(("bundleVersion", "1.11.0"))),
            }, Project);

            Assert.IsTrue(matrix.Rows.Single(r => r.PropertyPath == "bundleVersion").PossiblyStale);
            Assert.IsFalse(matrix.Rows.Single(r => r.PropertyPath == "androidSplitApplicationBinary").PossiblyStale,
                "Only Quest differs.");

            var alone = PlayerSettingsMatrix.Build(new[]
            {
                Profile("Quest", BuildTarget.Android, OwnCopy(("bundleVersion", "1.11.0"))),
            }, Project);
            Assert.IsFalse(alone.Rows.Single().PossiblyStale, "A single profile's difference is an override like any other.");

            var different = PlayerSettingsMatrix.Build(new[]
            {
                Profile("Quest", BuildTarget.Android, OwnCopy(("bundleVersion", "1.11.0"))),
                Profile("Steam Frame", BuildTarget.Android, OwnCopy(("bundleVersion", "1.10.0"))),
            }, Project);
            Assert.IsFalse(different.Rows.Single().PossiblyStale, "The profiles differ from each other too.");
        }

        [Test]
        public void ProfilesOfDifferentPlatforms_HoldingTheSameValue_ArePossiblyStale()
        {
            // Each profile differs from its own platform's Project Settings value, and both hold 2.
            var matrix = PlayerSettingsMatrix.Build(new[]
            {
                Profile("Quest", BuildTarget.Android, OwnCopy(("scriptingBackend", "  Android: 2\n  Standalone: 0"))),
                Profile("Windows", BuildTarget.StandaloneWindows64, OwnCopy(("scriptingBackend", "  Android: 1\n  Standalone: 2"))),
            }, Project);

            Assert.IsTrue(matrix.Rows.Single(r => r.PropertyPath == "scriptingBackend").PossiblyStale);
        }

        // A per-platform setting has one Project Settings value per platform, so
        // each platform's profiles get their own Project Settings column.
        [Test]
        public void EachPlatformHasItsOwnProjectSettingsColumn_SoEveryCellHoldsOneValue()
        {
            var matrix = PlayerSettingsMatrix.Build(new[]
            {
                Profile("Quest", BuildTarget.Android, OwnCopy(("bundleVersion", "1.11.0"))),
                Profile("Windows", BuildTarget.StandaloneWindows64, OwnCopy(("scriptingBackend", "  Android: 1\n  Standalone: 1"))),
                Profile("SteamVR", BuildTarget.StandaloneWindows64),
            }, Project);

            CollectionAssert.AreEqual(new[] { "Project Settings Android", "Quest", "Project Settings Windows", "Windows" },
                ColumnTitles(matrix));
            CollectionAssert.IsEmpty(matrix.Columns[0].AlsoUsedBy);
            CollectionAssert.AreEqual(new[] { "SteamVR" }, matrix.Columns[2].AlsoUsedBy,
                "SteamVR builds with the Windows Project Settings values.");
            CollectionAssert.IsEmpty(matrix.UsingProjectSettings);

            var version = matrix.Rows.Single(r => r.PropertyPath == "bundleVersion");
            CollectionAssert.AreEqual(new[] { "1.12.0", "1.11.0", "1.12.0", "1.12.0" }, version.Values);
            CollectionAssert.AreEqual(new[] { false, true, false, false }, version.Differs);
            Assert.IsFalse(version.PossiblyStale, "Windows holds the Project Settings value.");

            var backend = matrix.Rows.Single(r => r.PropertyPath == "scriptingBackend");
            CollectionAssert.AreEqual(new[] { "1", "1", "0", "1" }, backend.Values, "The column names the platform; the cells leave its key out.");
            CollectionAssert.AreEqual(new[] { false, false, false, true }, backend.Differs);
        }

        [Test]
        public void WithoutPlayerSettingsOfTheirOwn_ProfilesAreOnlyListedAsUsingProjectSettings()
        {
            var matrix = PlayerSettingsMatrix.Build(new[]
            {
                Profile("Windows", BuildTarget.StandaloneWindows64),
                Profile("Quest", BuildTarget.Android),
            }, Project);

            CollectionAssert.IsEmpty(matrix.Columns);
            CollectionAssert.IsEmpty(matrix.Rows);
            CollectionAssert.AreEqual(new[] { "Windows", "Quest" }, matrix.UsingProjectSettings);
        }

        // A cell's tooltip holds its whole value, which a narrow column cuts off,
        // without the column's name, which the header shows, or how to select the
        // profile, which the legend says.
        [Test]
        public void CellTooltip_IsTheCellsValue_AndAProfileCellAddsTheProjectSettingsValue()
        {
            var matrix = PlayerSettingsMatrix.Build(new[]
            {
                Profile("Quest", BuildTarget.Android, OwnCopy(("bundleVersion", "1.11.0"), ("androidSplitApplicationBinary", "1"))),
                Profile("Steam Frame", BuildTarget.Android, OwnCopy(("bundleVersion", "1.11.0"))),
            }, Project);
            var split = matrix.Rows.Single(r => r.PropertyPath == "androidSplitApplicationBinary");
            var version = matrix.Rows.Single(r => r.PropertyPath == "bundleVersion");

            Assert.AreEqual("0", PlayerSettingsMatrixView.CellTooltip(matrix, split, 0));
            Assert.AreEqual("1\nProject Settings (Android): 0", PlayerSettingsMatrixView.CellTooltip(matrix, split, 1));
            StringAssert.StartsWith("1.11.0\nProject Settings (Android): 1.12.0\nEvery profile with Player Settings of its own",
                PlayerSettingsMatrixView.CellTooltip(matrix, version, 2), "A possibly stale cell says why it is marked.");
        }

        // Windows and macOS share the Standalone group, but Unity keys some of
        // their settings (graphics APIs, graphics jobs) separately.
        [Test]
        public void PlatformKey_SeparatesWhatUnityKeysSeparately()
        {
            Assert.AreEqual(PlayerSettingsDiffComputer.PlatformKey(BuildTarget.StandaloneWindows),
                PlayerSettingsDiffComputer.PlatformKey(BuildTarget.StandaloneWindows64));
            Assert.AreNotEqual(PlayerSettingsDiffComputer.PlatformKey(BuildTarget.StandaloneWindows64),
                PlayerSettingsDiffComputer.PlatformKey(BuildTarget.StandaloneOSX));
            Assert.AreNotEqual(PlayerSettingsDiffComputer.PlatformKey(BuildTarget.StandaloneWindows64),
                PlayerSettingsDiffComputer.PlatformKey(BuildTarget.StandaloneWindows64, dedicatedServer: true));
            Assert.AreEqual("macOS", PlayerSettingsMatrix.PlatformName(BuildTarget.StandaloneOSX, false));
            Assert.AreEqual("Dedicated Server", PlayerSettingsMatrix.PlatformName(BuildTarget.StandaloneLinux64, true));
            Assert.AreEqual("Android", PlayerSettingsMatrix.PlatformName(BuildTarget.Android, false));
        }

        [Test]
        public void DisplayValue_IsTheValueTheDiffShowsForTheTarget()
        {
            Assert.AreEqual("0", PlayerSettingsDiffComputer.DisplayValue(Project, "scriptingBackend", BuildTarget.StandaloneWindows64));
            Assert.AreEqual("1.12.0", PlayerSettingsDiffComputer.DisplayValue(Project, "bundleVersion", BuildTarget.Android));
            Assert.IsNull(PlayerSettingsDiffComputer.DisplayValue(Project, "missing", BuildTarget.Android));

            var batching = UnityYamlParser.ParseToPropertyMap(
                "PlayerSettings:\n" +
                "  m_BuildTargetBatching:\n" +
                "  - m_BuildTarget: Standalone\n" +
                "    m_StaticBatching: 0\n" +
                "    m_DynamicBatching: 0\n" +
                "  - m_BuildTarget: Android\n" +
                "    m_StaticBatching: 1\n" +
                "    m_DynamicBatching: 0\n");
            Assert.AreEqual("1", PlayerSettingsDiffComputer.DisplayValue(batching, "m_StaticBatching", BuildTarget.Android),
                "Expanded batching entries resolve like the diff's rows.");
        }

        [Test]
        public void AProfileSharingNoSettingWithProjectSettings_IsUncompared_NotMatching()
        {
            var matrix = PlayerSettingsMatrix.Build(new[]
            {
                Profile("Quest", BuildTarget.Android, OwnCopy()),
                Profile("Misread", BuildTarget.Android, new Dictionary<string, string> { ["unrelated"] = "x" }),
            }, Project);

            CollectionAssert.AreEqual(new[] { "Misread" }, matrix.Uncompared);
            StringAssert.Contains("'Misread'", global::BuildForge.Editor.UI.PlayerSettingsDiffView.CouldNotCompareMessage(matrix.Uncompared));
        }

        // The uncompared profile keeps its column, so it can still be selected, but it shows
        // no value as matching and doesn't keep the others' shared value from being marked.
        [Test]
        public void AnUncomparedProfilesColumn_IsFlagged_AndLeftOutOfPossiblyStale()
        {
            var matrix = PlayerSettingsMatrix.Build(new[]
            {
                Profile("Quest", BuildTarget.Android, OwnCopy(("bundleVersion", "1.11.0"))),
                Profile("Steam Frame", BuildTarget.Android, OwnCopy(("bundleVersion", "1.11.0"))),
                Profile("Misread", BuildTarget.Android, new Dictionary<string, string> { ["unrelated"] = "x" }),
            }, Project);

            CollectionAssert.AreEqual(new[] { "Project Settings Android", "Quest", "Steam Frame", "Misread" }, ColumnTitles(matrix));
            CollectionAssert.AreEqual(new[] { false, false, false, true }, matrix.Columns.Select(c => c.Uncompared));
            var version = matrix.Rows.Single();
            Assert.AreEqual("bundleVersion", version.PropertyPath);
            Assert.IsTrue(version.PossiblyStale, "The uncompared profile neither matches nor differs.");
            Assert.IsNull(version.Values[3], "No Project Settings value shows as the uncompared profile's own.");
            Assert.IsFalse(version.Differs[3]);
            StringAssert.StartsWith("Not compared", PlayerSettingsMatrixView.CellTooltip(matrix, version, 3));
            StringAssert.StartsWith("Build Forge could not compare", PlayerSettingsMatrixView.ProfileTooltip(matrix.Columns[3]));
            StringAssert.StartsWith("1 Player Setting differs", PlayerSettingsMatrixView.ProfileTooltip(matrix.Columns[1]));
        }
    }
}
