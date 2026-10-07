using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace BuildForge.Editor.Core
{
    internal static class PlayerSettingsDiffComputer
    {
        public static List<Configuration.DiffEntry> ComputeDiff(
            Dictionary<string, string> profileMap,
            Dictionary<string, string> platformMap,
            BuildTarget buildTarget,
            bool dedicatedServer = false)
        {
            var diffs = new List<Configuration.DiffEntry>();
            var targetKeys = GetTargetKeys(buildTarget, dedicatedServer);
            profileMap = ExpandPerPlatformArrays(profileMap, targetKeys);
            platformMap = ExpandPerPlatformArrays(platformMap, targetKeys);

            foreach (var kvp in profileMap)
            {
                var key = kvp.Key;
                var profileValue = kvp.Value;

                if (IsNoisyProperty(key))
                    continue;

                if (!platformMap.TryGetValue(key, out var platformValue))
                    continue;

                if (IsPerPlatformProperty(platformValue) || IsPerPlatformProperty(profileValue))
                {
                    profileValue = SelectPlatformValue(profileValue, targetKeys);
                    platformValue = SelectPlatformValue(platformValue, targetKeys);
                }

                var normalizedProfile = NormalizeValue(profileValue);
                var normalizedPlatform = NormalizeValue(platformValue);

                if (normalizedProfile != normalizedPlatform)
                {
                    diffs.Add(new Configuration.DiffEntry(
                        key,
                        FormatDisplayName(key),
                        FormatYamlValue(platformValue),
                        FormatYamlValue(profileValue),
                        Configuration.DiffType.Modified));
                }
            }

            return diffs;
        }

        /// <summary>
        /// The value of <paramref name="key"/> for the build target as the diff
        /// shows it: a per-platform value narrowed to the target's entries,
        /// formatted for display. Null when the map lacks the key.
        /// </summary>
        public static string DisplayValue(Dictionary<string, string> map, string key, BuildTarget buildTarget, bool dedicatedServer = false)
        {
            var targetKeys = GetTargetKeys(buildTarget, dedicatedServer);
            if (!map.TryGetValue(key, out var value) && !ExpandPerPlatformArrays(map, targetKeys).TryGetValue(key, out value))
                return null;
            if (IsPerPlatformProperty(value))
                value = SelectPlatformValue(value, targetKeys);
            return FormatYamlValue(value);
        }

        /// <summary>
        /// Identifies the platform entries Unity keys a target's settings by: two
        /// profiles with the same key are compared against the same values.
        /// </summary>
        public static string PlatformKey(BuildTarget buildTarget, bool dedicatedServer = false)
            => string.Join(",", GetTargetKeys(buildTarget, dedicatedServer).OrderBy(k => k, System.StringComparer.Ordinal));

        public static string NormalizeValue(string value)
        {
            if (value == "{fileID: 0}" || value == "{instanceID: 0}")
                return "__null_ref__";

            var trimmed = value.Trim();
            if (trimmed == "[]" || trimmed == "")
                return "__empty__";

            var collapsed = Regex.Replace(trimmed, @"\s+", " ");

            // Null object references serialize as {fileID: 0} in ProjectSettings.asset
            // but {instanceID: 0} in Build Profile YAML. Same meaning — canonicalize so
            // values containing null refs (including nested in a struct) compare equal
            // regardless of which form Unity wrote.
            collapsed = Regex.Replace(collapsed, @"\{instanceID:\s*0\}", "{fileID: 0}");

            return collapsed;
        }

        // Every build target group's name, and the other keys Unity uses for a platform.
        static readonly HashSet<string> PlatformKeys = new(Enum.GetNames(typeof(BuildTargetGroup)).Concat(new[]
        {
            "iPhone", "iOSSupport", "AndroidPlayer",
            "LinuxStandaloneSupport", "WindowsStandaloneSupport",
            "MacStandaloneSupport",
            "AppleTVSupport", "WebGLSupport", "Server",
        }));

        public static bool IsPerPlatformProperty(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (var line in value.Split('\n'))
            {
                if (TryGetPlatformKey(line, out _))
                    return true;
            }

            return false;
        }

        static bool TryGetPlatformKey(string line, out string key)
        {
            var match = Regex.Match(line.Trim(),
                @"^(?:-\s*)?(?:(?:m_BuildTarget|first):\s*(\w+)\s*$|(\w+):)");
            key = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return match.Success && PlatformKeys.Contains(key);
        }

        static HashSet<string> GetTargetKeys(BuildTarget target, bool dedicatedServer)
        {
            // Unity keys a Dedicated Server's settings by NamedBuildTarget.Server,
            // not by the Standalone group its build target belongs to.
            if (dedicatedServer)
                return new HashSet<string> { "Server" };
            var keys = new HashSet<string> { BuildPipeline.GetBuildTargetGroup(target).ToString() };
            switch (target)
            {
                case BuildTarget.Android:
                    keys.Add("AndroidPlayer");
                    break;
                case BuildTarget.iOS:
                    keys.Add("iPhone");
                    keys.Add("iOSSupport");
                    break;
                case BuildTarget.tvOS:
                    keys.Add("AppleTVSupport");
                    break;
                case BuildTarget.WebGL:
                    keys.Add("WebGLSupport");
                    break;
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    keys.Add("WindowsStandaloneSupport");
                    break;
                case BuildTarget.StandaloneLinux64:
                    keys.Add("LinuxStandaloneSupport");
                    break;
                case BuildTarget.StandaloneOSX:
                    keys.Add("MacStandaloneSupport");
                    break;
            }
            return keys;
        }

        static string SelectPlatformValue(string value, HashSet<string> targetKeys)
        {
            // Unity uses both dictionaries (Standalone: value) and sequences
            // (m_BuildTarget / first). Keep complete target blocks, including
            // nested icons and entries whose serializedVersion precedes the key.
            var lines = value.Split('\n');
            var indent = int.MaxValue;
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    indent = System.Math.Min(indent, line.Length - line.TrimStart().Length);
            }

            var sequence = value.TrimStart().StartsWith("-");
            var selected = new List<string>();
            var block = new List<string>();
            var matchesTarget = false;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var trimmed = line.TrimStart();
                var startsBlock = line.Length - trimmed.Length == indent
                    && (!sequence || trimmed.StartsWith("-"));
                if (startsBlock && block.Count > 0)
                {
                    if (matchesTarget)
                        selected.AddRange(block);
                    block.Clear();
                    matchesTarget = false;
                }
                block.Add(line);
                if (TryGetPlatformKey(line, out var key) && targetKeys.Contains(key))
                    matchesTarget = true;
            }
            if (matchesTarget)
                selected.AddRange(block);
            return selected.Count == 0 ? "[]" : string.Join("\n", selected);
        }

        public static bool IsNoisyProperty(string path)
        {
            if (path == "productGUID") return true;
            if (path.StartsWith("clonedFromGUID")) return true;
            if (path == "m_ObjectHideFlags") return true;
            if (path == "serializedVersion") return true;
            if (path == "m_Script") return true;

            return false;
        }

        public static string FormatYamlValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "(empty)";

            if (value == "[]")
                return "(default)";

            if (!value.Contains("\n"))
                return value;

            var lines = value.Split('\n');
            var parts = new List<string>();
            foreach (var line in lines)
            {
                var kvMatch = Regex.Match(line.Trim(), @"^-?\s*(\w[\w]*)\s*:\s*(.+)$");
                if (kvMatch.Success)
                {
                    var k = kvMatch.Groups[1].Value;
                    var v = kvMatch.Groups[2].Value.Trim();
                    if (k.StartsWith("m_")) k = k.Substring(2);
                    k = Regex.Replace(k, @"([a-z])([A-Z])", "$1 $2");
                    parts.Add($"{k}: {v}");
                }
            }

            if (parts.Count > 0)
                return string.Join(", ", parts);

            var firstLine = lines[0].Trim();
            return $"{firstLine} (+{lines.Length - 1} lines)";
        }

        static readonly Dictionary<string, string> DisplayNameOverrides = new()
        {
            ["meshDeformation"] = "GPU Skinning",
        };

        public static string FormatDisplayName(string propertyPath)
        {
            if (DisplayNameOverrides.TryGetValue(propertyPath, out var overrideName))
                return overrideName;

            var name = propertyPath;
            if (name.StartsWith("m_")) name = name.Substring(2);

            name = Regex.Replace(name, @"([a-z])([A-Z])", "$1 $2");
            name = Regex.Replace(name, @"([A-Z]+)([A-Z][a-z])", "$1 $2");

            name = name.Replace(".", " > ").Replace("_", " ");

            if (name.Length > 0)
                name = char.ToUpper(name[0]) + name.Substring(1);

            return name;
        }

        static readonly HashSet<string> ExpandableArrays = new()
        {
            "m_BuildTargetBatching",
        };

        /// <summary>
        /// A copy of the map in which per-platform arrays whose entries hold several
        /// settings (static and dynamic batching per build target) become one key
        /// per setting, read from the entry for the target: flattened across all
        /// entries, the comparison would use whichever platform came last.
        /// </summary>
        static Dictionary<string, string> ExpandPerPlatformArrays(Dictionary<string, string> map, HashSet<string> targetKeys)
        {
            var result = new Dictionary<string, string>(map);
            foreach (var kvp in map)
            {
                if (!ExpandableArrays.Contains(kvp.Key))
                    continue;

                var value = kvp.Value;
                if (string.IsNullOrEmpty(value) || value == "[]")
                    continue;

                var children = new Dictionary<string, string>();
                var childMatches = Regex.Matches(SelectPlatformValue(value, targetKeys), @"(\w[\w]*)\s*:\s*(\S+)");

                foreach (Match m in childMatches)
                {
                    var childKey = m.Groups[1].Value;
                    var childValue = m.Groups[2].Value;

                    if (childKey != "m_BuildTarget")
                        children[childKey] = childValue;
                }

                if (children.Count > 0)
                {
                    result.Remove(kvp.Key);
                    foreach (var child in children)
                        result[child.Key] = child.Value;
                }
            }
            return result;
        }
    }
}
