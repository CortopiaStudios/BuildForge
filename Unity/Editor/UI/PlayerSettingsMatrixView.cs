using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using ColumnKind = BuildForge.Editor.Core.PlayerSettingsMatrix.ColumnKind;

namespace BuildForge.Editor.UI
{
    /// <summary>
    /// The Player Overrides window's Matrix mode: one row per Player Setting
    /// that differs anywhere, one column per Unity Build Profile with Player
    /// Settings of its own, each platform's profiles led by that platform's
    /// Project Settings values. A profile cell shows the value the profile
    /// builds with; clicking it, or the profile's name, selects the profile.
    /// </summary>
    internal class PlayerSettingsMatrixView
    {
        internal const string Description =
            "Compares Project Settings with every Unity Build Profile that has Player Settings of its own.";

        List<BuildProfile> profiles = new();
        PlayerSettingsMatrix matrix;
        bool projectSettingsMissing;
        string searchFilter = "";
        bool showMatchingValues = true;
        Vector2 scrollPos;
        float propertyColumnWidth;

        public void Refresh(IReadOnlyList<BuildProfile> buildProfiles)
        {
            profiles = buildProfiles.Where(p => p != null).ToList();
            var inputs = new List<PlayerSettingsMatrix.Profile>();
            foreach (var profile in profiles)
            {
                AssetDatabase.SaveAssetIfDirty(profile);
                var lines = PlayerSettingsDiffView.ExtractPlayerSettingsYaml(profile);
                inputs.Add(new PlayerSettingsMatrix.Profile
                {
                    Name = profile.name,
                    Target = BuildProfileUtility.GetBuildTarget(profile),
                    DedicatedServer = BuildProfileUtility.IsDedicatedServer(profile),
                    PlayerSettings = lines != null && lines.Count > 0 ? UnityYamlParser.ParseToPropertyMap(lines) : null,
                });
            }

            var projectLines = PlayerSettingsDiffView.ExtractPlatformPlayerSettingsYaml();
            projectSettingsMissing = projectLines == null;
            matrix = projectSettingsMissing ? null : PlayerSettingsMatrix.Build(inputs, UnityYamlParser.ParseToPropertyMap(projectLines));
            propertyColumnWidth = 0f;
        }

        public void Draw(System.Action onRefresh)
        {
            // IMGUI lays out every event before handling it, so rows that change
            // mid-event no longer match their layout. A new filter applies from
            // the next event, a refresh after drawing (ARCHITECTURE.md:
            // "GUIUtility.ExitGUI() and Layout Groups").
            var filter = searchFilter;
            DrawToolbar();
            DrawMatrix(filter);
            if (refreshRequested)
            {
                refreshRequested = false;
                onRefresh?.Invoke();
            }
        }

        bool refreshRequested;

        void DrawMatrix(string filter)
        {
            if (projectSettingsMissing || matrix == null)
            {
                EditorGUILayout.HelpBox("ProjectSettings/ProjectSettings.asset could not be read.", MessageType.Warning);
                return;
            }
            if (profiles.Count == 0)
            {
                EditorGUILayout.HelpBox("No Unity Build Profiles found in the project.", MessageType.Info);
                return;
            }

            var profileColumns = matrix.ProfileColumnCount;
            if (profileColumns == 0)
            {
                EditorGUILayout.HelpBox(
                    "No Unity Build Profile has Player Settings of its own; all of them build with Project Settings.",
                    MessageType.Info);
                return;
            }

            var withOwn = $"{profileColumns} Unity Build Profile{(profileColumns == 1 ? "" : "s")} with Player Settings of " +
                          (profileColumns == 1 ? "its own" : "their own");
            if (matrix.Rows.Count == 0)
            {
                EditorGUILayout.HelpBox($"The {withOwn} match{(profileColumns == 1 ? "es" : "")} Project Settings.", MessageType.Info);
                DrawUsingProjectSettings();
                return;
            }

            EditorGUILayout.HelpBox($"{matrix.Rows.Count} Player Settings differ from Project Settings in the {withOwn}.",
                MessageType.None);

            var propertyWidth = GetPropertyColumnWidth();
            var valueWidth = Mathf.Clamp((EditorGUIUtility.currentViewWidth - propertyWidth - 40f) / matrix.Columns.Count, 100f, 220f);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            DrawHeader(propertyWidth, valueWidth);
            foreach (var row in matrix.Rows)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    !row.DisplayName.ToLower().Contains(filter.ToLower()) &&
                    !row.PropertyPath.ToLower().Contains(filter.ToLower()))
                    continue;
                DrawRow(row, propertyWidth, valueWidth);
            }
            EditorGUILayout.Space(6);
            DrawLegend(profileColumns);
            DrawUsingProjectSettings();
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            showMatchingValues = GUILayout.Toggle(showMatchingValues, new GUIContent("Show Matching Values",
                    "Show, dimmed, the values of profiles that match Project Settings, instead of leaving their cells empty."),
                EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();

            searchFilter = EditorGUILayout.TextField(searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200));

            if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(50)))
                CopyToClipboard();

            if (GUILayout.Button(EditorGUIUtility.TrTextContentWithIcon("Refresh", "Recompute the matrix", "Refresh"),
                    EditorStyles.toolbarButton, GUILayout.Width(80)))
                refreshRequested = true;

            EditorGUILayout.EndHorizontal();
        }

        float GetPropertyColumnWidth()
        {
            if (propertyColumnWidth > 0f)
                return propertyColumnWidth;
            var max = EditorStyles.boldLabel.CalcSize(new GUIContent("Property")).x;
            foreach (var row in matrix.Rows)
                max = Mathf.Max(max, EditorStyles.label.CalcSize(new GUIContent(row.DisplayName)).x);
            propertyColumnWidth = Mathf.Clamp(max + 14f, 120f, 300f);
            return propertyColumnWidth;
        }

        void DrawHeader(float propertyWidth, float valueWidth)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Property", EditorStyles.boldLabel, GUILayout.Width(propertyWidth));
            foreach (var column in matrix.Columns)
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(valueWidth));
                if (column.Kind == ColumnKind.ProjectSettings)
                {
                    var tooltip = $"The {column.Platform} values in Project Settings." + (column.AlsoUsedBy.Count > 0
                        ? $" {JoinNames(column.AlsoUsedBy)} {(column.AlsoUsedBy.Count == 1 ? "has" : "have")} no Player Settings " +
                          $"of {(column.AlsoUsedBy.Count == 1 ? "its" : "their")} own and build{(column.AlsoUsedBy.Count == 1 ? "s" : "")} with these."
                        : "");
                    // The same rect and style as a profile's clickable name, so the titles line up.
                    var content = new GUIContent("Project Settings", tooltip);
                    GUI.Label(GUILayoutUtility.GetRect(content, EditorStyles.boldLabel, GUILayout.Width(valueWidth)),
                        content, EditorStyles.boldLabel);
                    var subtitle = column.AlsoUsedBy.Count > 0
                        ? $"{column.Platform}, used by {string.Join(", ", column.AlsoUsedBy)}"
                        : column.Platform;
                    EditorGUILayout.LabelField(new GUIContent(subtitle, tooltip), EditorStyles.miniLabel, GUILayout.Width(valueWidth));
                }
                else
                {
                    var profile = profiles[column.ProfileIndex];
                    var tooltip = $"{column.DiffCount} Player Setting{(column.DiffCount == 1 ? "" : "s")} differ" +
                                  $"{(column.DiffCount == 1 ? "s" : "")} from Project Settings. Click to select the Unity Build Profile.";
                    var content = new GUIContent(column.ProfileName, tooltip);
                    var rect = GUILayoutUtility.GetRect(content, EditorStyles.boldLabel, GUILayout.Width(valueWidth));
                    EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
                    if (GUI.Button(rect, content, EditorStyles.boldLabel))
                        Select(profile);
                    EditorGUILayout.LabelField(new GUIContent($"{column.Platform}, own Player Settings", tooltip),
                        EditorStyles.miniLabel, GUILayout.Width(valueWidth));
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
            DrawSeparator(GUILayoutUtility.GetLastRect());
        }

        void DrawRow(PlayerSettingsMatrix.Row row, float propertyWidth, float valueWidth)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent(row.DisplayName, row.PropertyPath), GUILayout.Width(propertyWidth));
            for (var c = 0; c < matrix.Columns.Count; c++)
                DrawCell(row, c, valueWidth);
            EditorGUILayout.EndHorizontal();
            DrawSeparator(GUILayoutUtility.GetLastRect());
        }

        void DrawCell(PlayerSettingsMatrix.Row row, int c, float width)
        {
            var column = matrix.Columns[c];
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.Width(width));
            var isProfile = column.Kind == ColumnKind.Profile;
            if (isProfile)
            {
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
                var e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    Select(profiles[column.ProfileIndex]);
                    e.Use();
                }
            }

            if (row.Differs[c] && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, row.PossiblyStale ? StaleColor : DiffersColor);

            var matching = isProfile && !row.Differs[c];
            if (!matching || showMatchingValues)
            {
                var previous = GUI.contentColor;
                if (matching)
                    GUI.contentColor = MatchingTextColor;
                PlayerSettingsDiffView.DrawValue(rect, row.PropertyPath, row.Values[c]);
                GUI.contentColor = previous;
            }
            GUI.Label(rect, new GUIContent(string.Empty, CellTooltip(row, c)));
        }

        string CellTooltip(PlayerSettingsMatrix.Row row, int c)
        {
            var column = matrix.Columns[c];
            var shared = c;
            while (matrix.Columns[shared].Kind != ColumnKind.ProjectSettings)
                shared--;
            var projectValue = $"Project Settings ({column.Platform}): {row.Values[shared]}";
            if (column.Kind == ColumnKind.ProjectSettings)
                return projectValue;
            var tooltip = $"{column.ProfileName}: {row.Values[c]}\n{projectValue}";
            if (row.PossiblyStale)
                tooltip += "\nEvery profile with Player Settings of its own has this value, unlike Project Settings: possibly " +
                           "a Project Settings change that was not copied into them.";
            return tooltip + "\nClick to select the Unity Build Profile.";
        }

        void DrawLegend(int profileColumns)
        {
            EditorGUILayout.BeginHorizontal();
            DrawSwatch(DiffersColor);
            GUILayout.Label("Differs from Project Settings", EditorStyles.miniLabel);
            if (profileColumns >= 2)
            {
                GUILayout.Space(12);
                DrawSwatch(StaleColor);
                GUILayout.Label("Every profile differs the same way: possibly stale", EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("Click a profile's name or cell to select it", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        void DrawUsingProjectSettings()
        {
            var names = matrix.UsingProjectSettings;
            if (names.Count == 0)
                return;
            var one = names.Count == 1;
            EditorGUILayout.LabelField(
                $"{JoinNames(names)} {(one ? "has" : "have")} no Player Settings of {(one ? "its" : "their")} own and " +
                $"build{(one ? "s" : "")} with Project Settings.",
                EditorStyles.wordWrappedMiniLabel);
        }

        void CopyToClipboard()
        {
            if (matrix == null || matrix.Rows.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.Append("Property");
            foreach (var column in matrix.Columns)
                sb.Append(" | ").Append(column.Kind == ColumnKind.ProjectSettings
                    ? $"Project Settings ({column.Platform})"
                    : Escape(column.ProfileName));
            sb.AppendLine();
            sb.Append("---");
            foreach (var unused in matrix.Columns)
                sb.Append(" | ---");
            sb.AppendLine();
            foreach (var row in matrix.Rows)
            {
                sb.Append(row.PropertyPath);
                foreach (var value in row.Values)
                    sb.Append(" | ").Append(Escape(value));
                sb.AppendLine();
            }

            var names = matrix.UsingProjectSettings;
            if (names.Count > 0)
                sb.AppendLine().AppendLine($"{JoinNames(names)} build{(names.Count == 1 ? "s" : "")} with Project Settings.");

            GUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log($"[Build Forge] Copied the Player Settings matrix ({matrix.Rows.Count} rows) to the clipboard.");
        }

        static string Escape(string value) => (value ?? "").Replace("|", "\\|").Replace("\n", " ");

        static string JoinNames(IReadOnlyList<string> names)
            => names.Count <= 1 ? string.Join("", names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];

        static void Select(BuildProfile profile)
        {
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        static void DrawSwatch(Color color)
        {
            var rect = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.Height(10f));
            rect.y += 3f;
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, color);
        }

        static void DrawSeparator(Rect rowRect)
        {
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax, rowRect.width, 1f), SeparatorColor);
        }

        static Color DiffersColor => EditorGUIUtility.isProSkin ? new Color(0.3f, 0.55f, 1f, 0.3f) : new Color(0.2f, 0.45f, 1f, 0.22f);
        static Color StaleColor => EditorGUIUtility.isProSkin ? new Color(1f, 0.7f, 0.15f, 0.32f) : new Color(1f, 0.65f, 0f, 0.32f);
        static Color SeparatorColor => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.09f);
        static readonly Color MatchingTextColor = new(1f, 1f, 1f, 0.45f);
    }
}
