using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Configuration;
using UnityEditor;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Every Player Setting that differs between Project Settings and the Unity
    /// Build Profiles with Player Settings of their own, side by side. Profiles
    /// are grouped by the platform entries Unity keys their settings by, and each
    /// group is led by a column with that platform's Project Settings values, so
    /// every cell holds exactly one value. Pure so it can be tested.
    /// </summary>
    internal class PlayerSettingsMatrix
    {
        /// <summary>One Unity Build Profile to compare.</summary>
        internal class Profile
        {
            public string Name;
            public BuildTarget Target;
            public bool DedicatedServer;
            /// <summary>The profile's own Player Settings, or null when it builds with Project Settings.</summary>
            public Dictionary<string, string> PlayerSettings;
        }

        internal enum ColumnKind { ProjectSettings, Profile }

        internal class Column
        {
            public ColumnKind Kind;
            /// <summary>The platform name of the column's group.</summary>
            public string Platform;
            /// <summary>Profile columns: index into the compared profiles.</summary>
            public int ProfileIndex = -1;
            /// <summary>Profile columns: the profile's name.</summary>
            public string ProfileName;
            /// <summary>Profile columns: how many of its settings differ from Project Settings.</summary>
            public int DiffCount;
            /// <summary>Project Settings columns: the group's profiles without Player Settings of their own.</summary>
            public List<string> AlsoUsedBy = new();
        }

        internal class Row
        {
            public string PropertyPath;
            public string DisplayName;
            /// <summary>One value per column.</summary>
            public string[] Values;
            /// <summary>Per column: a profile value that differs from its platform's Project Settings value.</summary>
            public bool[] Differs;
            /// <summary>
            /// Every profile column holds the same value, and every one differs
            /// from Project Settings: possibly a Project Settings change that was
            /// not copied into the profiles. Only with two or more profile columns.
            /// </summary>
            public bool PossiblyStale;
        }

        public readonly List<Column> Columns = new();
        public readonly List<Row> Rows = new();
        /// <summary>Profiles on platforms where no profile has Player Settings of its own.</summary>
        public readonly List<string> UsingProjectSettings = new();

        public int ProfileColumnCount => Columns.Count(c => c.Kind == ColumnKind.Profile);

        public static PlayerSettingsMatrix Build(IReadOnlyList<Profile> profiles, Dictionary<string, string> projectSettings)
        {
            var matrix = new PlayerSettingsMatrix();

            // Profiles whose settings Unity keys by the same platform entries
            // share every Project Settings value.
            var groups = new List<(string key, string platform, Profile target, List<int> own, List<string> shared)>();
            for (var i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];
                var key = PlayerSettingsDiffComputer.PlatformKey(profile.Target, profile.DedicatedServer);
                var index = groups.FindIndex(g => g.key == key);
                if (index < 0)
                {
                    groups.Add((key, PlatformName(profile.Target, profile.DedicatedServer), profile, new List<int>(), new List<string>()));
                    index = groups.Count - 1;
                }
                if (profile.PlayerSettings != null)
                    groups[index].own.Add(i);
                else
                    groups[index].shared.Add(profile.Name);
            }

            var diffs = new Dictionary<int, Dictionary<string, DiffEntry>>();
            var columnTargets = new List<Profile>();
            foreach (var group in groups)
            {
                if (group.own.Count == 0)
                {
                    matrix.UsingProjectSettings.AddRange(group.shared);
                    continue;
                }
                matrix.Columns.Add(new Column { Kind = ColumnKind.ProjectSettings, Platform = group.platform, AlsoUsedBy = group.shared });
                columnTargets.Add(group.target);
                foreach (var i in group.own)
                {
                    var profile = profiles[i];
                    var entries = PlayerSettingsDiffComputer.ComputeDiff(
                        profile.PlayerSettings, projectSettings, profile.Target, profile.DedicatedServer);
                    diffs[i] = entries.GroupBy(e => e.PropertyPath).ToDictionary(g => g.Key, g => g.First());
                    matrix.Columns.Add(new Column
                    {
                        Kind = ColumnKind.Profile, Platform = group.platform, ProfileIndex = i,
                        ProfileName = profile.Name, DiffCount = entries.Count,
                    });
                    columnTargets.Add(profile);
                }
            }

            foreach (var key in OrderedKeys(diffs.Values.SelectMany(d => d.Keys), projectSettings))
            {
                var row = new Row
                {
                    PropertyPath = key,
                    DisplayName = PlayerSettingsDiffComputer.FormatDisplayName(key),
                    Values = new string[matrix.Columns.Count],
                    Differs = new bool[matrix.Columns.Count],
                };
                string shared = null;
                for (var c = 0; c < matrix.Columns.Count; c++)
                {
                    var column = matrix.Columns[c];
                    if (column.Kind == ColumnKind.ProjectSettings)
                    {
                        // A diff entry of the group carries the value exactly as
                        // the comparison saw it; the rest of the group matches it.
                        shared = null;
                        for (var g = c + 1; g < matrix.Columns.Count && matrix.Columns[g].Kind == ColumnKind.Profile; g++)
                            if (diffs[matrix.Columns[g].ProfileIndex].TryGetValue(key, out var groupEntry))
                            {
                                shared = groupEntry.BaseValue;
                                break;
                            }
                        var target = columnTargets[c];
                        shared ??= PlayerSettingsDiffComputer.DisplayValue(projectSettings, key, target.Target, target.DedicatedServer);
                        row.Values[c] = shared;
                    }
                    else if (diffs[column.ProfileIndex].TryGetValue(key, out var entry))
                    {
                        row.Values[c] = entry.ProfileValue;
                        row.Differs[c] = true;
                    }
                    else
                    {
                        row.Values[c] = shared;
                    }
                }

                var profileCells = Enumerable.Range(0, matrix.Columns.Count)
                    .Where(c => matrix.Columns[c].Kind == ColumnKind.Profile).ToList();
                row.PossiblyStale = profileCells.Count >= 2
                    && profileCells.All(c => row.Differs[c])
                    && profileCells.All(c => row.Values[c] == row.Values[profileCells[0]]);
                matrix.Rows.Add(row);
            }

            return matrix;
        }

        /// <summary>
        /// The keys in Project Settings order, which keeps related settings
        /// together; keys it lacks (expanded batching entries) follow in the
        /// order the profiles list them.
        /// </summary>
        static IEnumerable<string> OrderedKeys(IEnumerable<string> keys, Dictionary<string, string> projectSettings)
        {
            var position = new Dictionary<string, int>();
            foreach (var key in projectSettings.Keys)
                position[key] = position.Count;
            var unique = keys.Distinct().ToList();
            return unique
                .Select((key, index) => (key, index))
                .OrderBy(k => position.TryGetValue(k.key, out var p) ? p : int.MaxValue)
                .ThenBy(k => k.index)
                .Select(k => k.key);
        }

        /// <summary>The platform name a group of profiles is shown under.</summary>
        internal static string PlatformName(BuildTarget target, bool dedicatedServer)
        {
            if (dedicatedServer)
                return "Dedicated Server";
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "Windows";
                case BuildTarget.StandaloneOSX:
                    return "macOS";
                case BuildTarget.StandaloneLinux64:
                    return "Linux";
                default:
                    return BuildPipeline.GetBuildTargetGroup(target).ToString();
            }
        }
    }
}
