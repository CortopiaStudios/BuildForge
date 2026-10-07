using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.UI
{
    /// <summary>
    /// Reusable Player Settings diff UI: computes and renders which Player Settings a
    /// Build Profile overrides compared to the platform defaults. Used both by the
    /// standalone Player Overrides window and inline in the Build Forge Profile
    /// inspector. The owning UI supplies the profile and chrome (dropdown, foldout).
    /// </summary>
    internal class PlayerSettingsDiffView
    {
        /// <summary>
        /// One-line summary of what the diff view shows; used by the standalone
        /// window's header and as the tooltip on the profile inspector's
        /// "Player Overrides" foldout (matching the plugin Description tooltips).
        /// </summary>
        internal const string Description =
            "Shows which Player Settings a Unity Build Profile overrides compared to the platform defaults.";

        BuildProfile buildProfile;
        List<DiffEntry> diffs;
        bool hasPlayerSettings;
        string searchFilter = "";
        Vector2 scrollPos;

        public BuildProfile Profile => buildProfile;
        public bool HasPlayerSettings => hasPlayerSettings;
        public int DiffCount => diffs?.Count ?? 0;

        public void SetProfile(BuildProfile profile)
        {
            buildProfile = profile;
            ComputeDiff();
        }

        public void ComputeDiff()
        {
            diffs = new List<DiffEntry>();
            hasPlayerSettings = false;

            if (buildProfile == null)
                return;

            AssetDatabase.SaveAssetIfDirty(buildProfile);

            var profileLines = ExtractPlayerSettingsYaml(buildProfile);
            hasPlayerSettings = profileLines != null && profileLines.Count > 0;
            if (!hasPlayerSettings)
                return;

            var platformLines = ExtractPlatformPlayerSettingsYaml();
            if (platformLines == null)
                return;

            var profileMap = ParseYamlToPropertyMap(profileLines);
            var platformMap = ParseYamlToPropertyMap(platformLines);

            diffs = PlayerSettingsDiffComputer.ComputeDiff(
                profileMap, platformMap, BuildProfileUtility.GetBuildTarget(buildProfile),
                BuildProfileUtility.IsDedicatedServer(buildProfile));
        }

        /// <summary>
        /// Draws the search/copy/refresh toolbar, a summary, and the diff table.
        /// </summary>
        /// <param name="useScrollView">Wrap the table in a scroll view (standalone
        /// window). Pass false when hosted in something that already scrolls.</param>
        /// <param name="onRefresh">Invoked by the Refresh button; defaults to
        /// recomputing this view's diff.</param>
        public void Draw(bool useScrollView, System.Action onRefresh = null)
        {
            if (buildProfile == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a Unity Build Profile to see which Player Settings it overrides " +
                    "compared to the platform's base settings.",
                    MessageType.Info);
                return;
            }

            DrawToolbar(onRefresh);

            if (!hasPlayerSettings)
            {
                EditorGUILayout.HelpBox(
                    "The Unity Build Profile has no Player Settings overrides. " +
                    "It uses the platform defaults as-is.",
                    MessageType.Info);
                return;
            }

            if (diffs == null || diffs.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "The Unity Build Profile's Player Settings are identical to the platform defaults.\n" +
                    "No overrides detected.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                $"{diffs.Count} Player Settings differ from the platform defaults.",
                MessageType.None);

            DrawDiffHeader();

            if (useScrollView)
            {
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
                DrawDiffEntries();
                EditorGUILayout.Space(20);
                EditorGUILayout.EndScrollView();
            }
            else
            {
                DrawDiffEntries();
            }
        }

        void DrawToolbar(System.Action onRefresh)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.FlexibleSpace();

            searchFilter = EditorGUILayout.TextField(searchFilter,
                EditorStyles.toolbarSearchField, GUILayout.Width(200));

            if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(50)))
                CopyDiffToClipboard();

            if (GUILayout.Button(EditorGUIUtility.TrTextContentWithIcon("Refresh", "Recompute diff", "Refresh"),
                    EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                if (onRefresh != null)
                    onRefresh();
                else
                    ComputeDiff();
            }

            EditorGUILayout.EndHorizontal();
        }

        void CopyDiffToClipboard()
        {
            if (diffs == null || diffs.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("Property | Platform Default | Unity Build Profile Override");
            sb.AppendLine("--- | --- | ---");

            foreach (var diff in diffs)
            {
                var b = (diff.BaseValue ?? "").Replace("|", "\\|").Replace("\n", " ");
                var p = (diff.ProfileValue ?? "").Replace("|", "\\|").Replace("\n", " ");
                sb.AppendLine($"{diff.PropertyPath} | {b} | {p}");
            }

            GUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log($"[Build Forge] Copied {diffs.Count} diff entries to clipboard.");
        }

        // Property column width is sized to the widest property name (clamped), so it
        // takes only the space it needs. Cached per diffs set.
        float propertyColumnWidth;
        List<DiffEntry> widthComputedFor;

        // Fixed (not min/max) width so every row resolves to the same column
        // width. Flexible widths resolve per horizontal group — each row would
        // size its columns by its own content, misaligning it against the
        // header and the other rows.
        GUILayoutOption GetValueColumnWidth()
        {
            // Rough allowance for helpBox padding, spacing, and scrollbar.
            const float margins = 60f;
            var value = Mathf.Clamp(
                (EditorGUIUtility.currentViewWidth - GetPropertyColumnWidth() - margins) / 2f,
                120f, 300f);
            return GUILayout.Width(value);
        }

        float GetPropertyColumnWidth()
        {
            if (widthComputedFor == diffs && propertyColumnWidth > 0f)
                return propertyColumnWidth;

            var max = EditorStyles.boldLabel.CalcSize(new GUIContent("Property")).x;
            foreach (var diff in diffs)
            {
                var w = EditorStyles.label.CalcSize(new GUIContent(diff.DisplayName)).x;
                if (w > max) max = w;
            }

            propertyColumnWidth = Mathf.Clamp(max + 14f, 120f, 400f);
            widthComputedFor = diffs;
            return propertyColumnWidth;
        }

        void DrawDiffHeader()
        {
            var propWidth = GUILayout.Width(GetPropertyColumnWidth());
            var valueWidth = GetValueColumnWidth();
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Property", EditorStyles.boldLabel, propWidth);
            EditorGUILayout.LabelField("Platform Default", EditorStyles.boldLabel, valueWidth);
            EditorGUILayout.LabelField("Unity Build Profile Override", EditorStyles.boldLabel, valueWidth);
            EditorGUILayout.EndHorizontal();
        }

        void DrawDiffEntries()
        {
            var propWidth = GUILayout.Width(GetPropertyColumnWidth());
            var valueWidth = GetValueColumnWidth();
            foreach (var diff in diffs)
            {
                if (!string.IsNullOrEmpty(searchFilter) &&
                    !diff.DisplayName.ToLower().Contains(searchFilter.ToLower()) &&
                    !diff.PropertyPath.ToLower().Contains(searchFilter.ToLower()))
                    continue;

                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.9f, 0.5f, 0.15f);

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                EditorGUILayout.LabelField(
                    new GUIContent(diff.DisplayName, diff.PropertyPath),
                    propWidth);

                DrawValueCell(diff.PropertyPath, diff.BaseValue, valueWidth);
                DrawValueCell(diff.PropertyPath, diff.ProfileValue, valueWidth);

                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = prevBg;
            }
        }

        static SerializedObject cachedPlayerSettingsSO;

        static SerializedObject GetPlayerSettingsSO()
        {
            if (cachedPlayerSettingsSO == null)
                cachedPlayerSettingsSO = new SerializedObject(
                    Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            cachedPlayerSettingsSO.Update();
            return cachedPlayerSettingsSO;
        }

        static readonly HashSet<string> KnownBooleanProperties = new()
        {
            "m_StaticBatching",
            "m_DynamicBatching",
        };

        /// <summary>
        /// Draws a value cell. The width goes on the cell's one control rect: an
        /// EditorGUILayout control without one reserves at least
        /// labelWidth + fieldWidth via GetControlRect, which would force the
        /// cell wider than the column and misalign it against the header.
        /// </summary>
        void DrawValueCell(string propertyPath, string value, GUILayoutOption width)
            => DrawValue(EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, width), propertyPath, value);

        /// <summary>
        /// Draws a value with rich rendering based on detected type: colors as
        /// swatches, object references as thumbnails, enums as names, etc.
        /// Shared with the Player Overrides window's matrix.
        /// </summary>
        internal static void DrawValue(Rect rect, string propertyPath, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                EditorGUI.LabelField(rect, "(none)");
                return;
            }

            if (int.TryParse(value, out var intVal))
            {
                var prop = GetPlayerSettingsSO().FindProperty(propertyPath);
                if (prop != null)
                {
                    if (prop.propertyType == SerializedPropertyType.Enum &&
                        intVal >= 0 && intVal < prop.enumDisplayNames.Length)
                    {
                        EditorGUI.LabelField(rect, new GUIContent(
                            prop.enumDisplayNames[intVal], $"{value} ({propertyPath})"));
                        return;
                    }

                    if (prop.propertyType == SerializedPropertyType.Boolean)
                    {
                        EditorGUI.LabelField(rect, new GUIContent(
                            intVal == 1 ? "Yes" : "No", $"{value} ({propertyPath})"));
                        return;
                    }

                    if (prop.propertyType == SerializedPropertyType.Integer)
                    {
                        var enumName = ResolveIntEnumName(propertyPath, intVal);
                        if (enumName != null)
                        {
                            EditorGUI.LabelField(rect, new GUIContent(
                                enumName, $"{value} ({propertyPath})"));
                            return;
                        }
                    }
                }
                else if (KnownBooleanProperties.Contains(propertyPath))
                {
                    EditorGUI.LabelField(rect, new GUIContent(
                        intVal == 1 ? "Yes" : "No", $"{value} ({propertyPath})"));
                    return;
                }
            }

            var colorMatch = Regex.Match(value, @"^\{r:\s*([\d.]+),\s*g:\s*([\d.]+),\s*b:\s*([\d.]+),\s*a:\s*([\d.]+)\}$");
            if (colorMatch.Success)
            {
                if (float.TryParse(colorMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) &&
                    float.TryParse(colorMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var g) &&
                    float.TryParse(colorMatch.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) &&
                    float.TryParse(colorMatch.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                {
                    var color = new Color(r, g, b, a);
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUI.ColorField(rect, GUIContent.none, color, false, true, false);
                    EditorGUI.EndDisabledGroup();
                    return;
                }
            }

            var fileRefMatch = Regex.Match(value, @"^\{fileID:\s*(\d+),\s*guid:\s*([0-9a-f]+)");
            if (fileRefMatch.Success)
            {
                var guid = fileRefMatch.Groups[2].Value;
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    long.TryParse(fileRefMatch.Groups[1].Value, out var fileId);
                    Object resolved = null;

                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                    {
                        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out long localId)
                            && localId == fileId)
                        {
                            resolved = asset;
                            break;
                        }
                    }

                    resolved ??= AssetDatabase.LoadMainAssetAtPath(assetPath);

                    if (resolved != null)
                    {
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUI.ObjectField(rect, resolved, resolved.GetType(), false);
                        EditorGUI.EndDisabledGroup();
                        return;
                    }
                }

                EditorGUI.LabelField(rect, new GUIContent(
                    string.IsNullOrEmpty(assetPath) ? value : Path.GetFileNameWithoutExtension(assetPath), value));
                return;
            }

            if (value == "{fileID: 0}" || value == "{instanceID: 0}")
            {
                EditorGUI.LabelField(rect, "(none)");
                return;
            }

            EditorGUI.LabelField(rect, new GUIContent(PlayerSettingsDiffComputer.FormatYamlValue(value), value));
        }

        static Dictionary<string, System.Type> cachedEnumTypes;

        static string ResolveIntEnumName(string propertyPath, int value)
        {
            if (cachedEnumTypes == null)
            {
                cachedEnumTypes = new Dictionary<string, System.Type>();
                var psType = typeof(PlayerSettings);
                var flags = System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Static;

                foreach (var prop in psType.GetProperties(flags))
                {
                    if (prop.PropertyType.IsEnum)
                        RegisterEnum(prop.Name, prop.PropertyType);
                }

                foreach (var nested in psType.GetNestedTypes(flags))
                {
                    foreach (var prop in nested.GetProperties(flags))
                    {
                        if (prop.PropertyType.IsEnum)
                        {
                            RegisterEnum(nested.Name + char.ToUpper(prop.Name[0]) + prop.Name.Substring(1), prop.PropertyType);
                            RegisterEnum(nested.Name + prop.Name, prop.PropertyType);
                        }
                    }
                }

                void RegisterEnum(string name, System.Type enumType)
                {
                    cachedEnumTypes.TryAdd(name, enumType);
                    cachedEnumTypes.TryAdd(char.ToLower(name[0]) + name.Substring(1), enumType);
                    cachedEnumTypes.TryAdd(name.ToLowerInvariant(), enumType);
                }
            }

            if (!cachedEnumTypes.TryGetValue(propertyPath, out var type))
                cachedEnumTypes.TryGetValue(propertyPath.ToLowerInvariant(), out type);

            if (type == null) return null;
            if (type.GetCustomAttributes(typeof(System.FlagsAttribute), false).Length > 0) return null;

            if (!System.Enum.IsDefined(type, value))
                return null;

            var name = System.Enum.GetName(type, value);
            name = Regex.Replace(name, @"([a-z])([A-Z])", "$1 $2");
            name = Regex.Replace(name, @"([A-Z]+)([A-Z][a-z])", "$1 $2");
            return name;
        }

        /// <summary>
        /// Extracts the inner YAML lines from a Build Profile's m_PlayerSettingsYaml.
        /// </summary>
        internal static List<string> ExtractPlayerSettingsYaml(BuildProfile profile)
        {
            var assetPath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
                return null;

            string[] fileLines;
            try
            {
                fileLines = File.ReadAllLines(assetPath);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Build Forge] Failed to read Unity Build Profile at {assetPath}: {e.Message}");
                return null;
            }

            var innerLines = new List<string>();
            bool inSection = false;

            for (int i = 0; i < fileLines.Length; i++)
            {
                var line = fileLines[i];
                var trimmed = line.TrimStart();

                if (!inSection)
                {
                    if (trimmed.StartsWith("m_PlayerSettingsYaml:"))
                        inSection = true;
                    continue;
                }

                var indent = line.Length - line.TrimStart().Length;
                if (indent <= 2 && !string.IsNullOrWhiteSpace(line)
                    && !trimmed.StartsWith("-") && !trimmed.StartsWith("m_Settings")
                    && trimmed.Contains(":"))
                {
                    break;
                }

                // Extract content from "- line: '|   content'" entries. Unity wraps
                // each line as a YAML single-quoted scalar, doubling inner quotes
                // ('' -> '); un-escape so values compare equal to ProjectSettings.asset.
                var match = Regex.Match(line, @"^\s*-\s+line:\s+'?\|?\s?(.+?)'?\s*$");
                if (match.Success)
                {
                    innerLines.Add(match.Groups[1].Value.Replace("''", "'"));
                }
                else if (trimmed.Length > 0 && !trimmed.StartsWith("m_Settings")
                         && !trimmed.StartsWith("- line") && innerLines.Count > 0)
                {
                    var cleanTrimmed = trimmed.TrimEnd('\'').Replace("''", "'");
                    innerLines[innerLines.Count - 1] += " " + cleanTrimmed;
                }
            }

            return innerLines.Count > 0 ? innerLines : null;
        }

        /// <summary>
        /// Extracts the PlayerSettings YAML from ProjectSettings/ProjectSettings.asset.
        /// </summary>
        internal static List<string> ExtractPlatformPlayerSettingsYaml()
        {
            const string path = "ProjectSettings/ProjectSettings.asset";
            if (!File.Exists(path))
                return null;

            string[] fileLines;
            try
            {
                fileLines = File.ReadAllLines(path);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Build Forge] Failed to read ProjectSettings: {e.Message}");
                return null;
            }

            var result = new List<string>();
            bool inPlayerSettings = false;

            for (int i = 0; i < fileLines.Length; i++)
            {
                var line = fileLines[i];

                if (!inPlayerSettings)
                {
                    if (line.StartsWith("PlayerSettings:"))
                    {
                        inPlayerSettings = true;
                        result.Add(line);
                    }
                    continue;
                }

                if (line.Length > 0 && line[0] != ' ' && line[0] != '\t')
                    break;

                result.Add(line);
            }

            return result.Count > 0 ? result : null;
        }

        static Dictionary<string, string> ParseYamlToPropertyMap(List<string> yamlLines)
        {
            return UnityYamlParser.ParseToPropertyMap(yamlLines);
        }
    }
}
