using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Core;
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
            CollectionAssert.AreEqual(new[] { "  Android: 1", "  Android: 1", "  Standalone: 0", "  Standalone: 1" }, backend.Values);
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
            Assert.AreEqual("  Standalone: 0",
                PlayerSettingsDiffComputer.DisplayValue(Project, "scriptingBackend", BuildTarget.StandaloneWindows64));
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
    }
}
