using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.UI
{
    /// <summary>
    /// Standalone window for inspecting Player Settings across Unity Build
    /// Profiles. Matrix mode (<see cref="PlayerSettingsMatrixView"/>) compares
    /// every profile that has Player Settings of its own with Project Settings
    /// at once. Single Profile mode shows which Player Settings one Unity Build
    /// Profile overrides; its diff rendering is shared with the Build Forge
    /// Profile inspector via <see cref="PlayerSettingsDiffView"/>, and the
    /// window adds the profile dropdown for Build Profiles not tied to a Build
    /// Forge Profile.
    /// </summary>
    internal class PlayerSettingsDiffWindow : EditorWindow
    {
        [SerializeField] BuildProfile buildProfile;
        [SerializeField] bool singleProfileMode;
        List<BuildProfile> detectedProfiles = new();
        string[] profileDisplayNames = System.Array.Empty<string>();
        int selectedProfileIndex = -1;
        readonly PlayerSettingsDiffView view = new();
        readonly PlayerSettingsMatrixView matrixView = new();

        static readonly GUIContent[] ModeLabels =
        {
            new("Matrix", "Every Unity Build Profile with Player Settings of its own, side by side."),
            new("Single Profile", "The Player Settings one Unity Build Profile overrides."),
        };

        [MenuItem("Window/Build Forge/Player Overrides", priority = 200)]
        static void ShowWindow()
        {
            var window = GetWindow<PlayerSettingsDiffWindow>();
            window.titleContent = new GUIContent("Player Overrides");
            window.minSize = new Vector2(600, 400);
            window.RefreshProfileList();
            window.Show();
        }

        void OnEnable()
        {
            RefreshProfileList();
            if (buildProfile != null)
                view.SetProfile(buildProfile);
            if (!singleProfileMode)
                matrixView.Refresh(detectedProfiles);
        }

        void RefreshProfileList()
        {
            // Show all Build Profiles in the project. The diff view works on any
            // Build Profile, not just those referenced by a Build Forge Profile.
            var profileGuids = AssetDatabase.FindAssets($"t:{nameof(BuildProfile)}");
            var seen = new HashSet<BuildProfile>();
            detectedProfiles = new List<BuildProfile>();

            foreach (var guid in profileGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
                if (profile != null && seen.Add(profile))
                    detectedProfiles.Add(profile);
            }

            detectedProfiles = detectedProfiles.OrderBy(p => p.name).ToList();
            profileDisplayNames = detectedProfiles.Select(p => p.name).ToArray();

            selectedProfileIndex = buildProfile != null
                ? detectedProfiles.IndexOf(buildProfile)
                : -1;
        }

        void SelectProfile(BuildProfile profile)
        {
            buildProfile = profile;
            selectedProfileIndex = profile != null ? detectedProfiles.IndexOf(profile) : -1;
            view.SetProfile(profile);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Platform Player Overrides By Unity Build Profile",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(singleProfileMode ? PlayerSettingsDiffView.Description : PlayerSettingsMatrixView.Description,
                EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            DrawModeAndProfileSelector();
            EditorGUILayout.Space(4);

            if (singleProfileMode)
            {
                view.Draw(useScrollView: true, onRefresh: () =>
                {
                    RefreshProfileList();
                    view.ComputeDiff();
                });
            }
            else
            {
                matrixView.Draw(onRefresh: () =>
                {
                    RefreshProfileList();
                    matrixView.Refresh(detectedProfiles);
                    Repaint();
                });
            }

            // The rest of this event was laid out for the mode it started in;
            // switch afterwards (ARCHITECTURE.md: "GUIUtility.ExitGUI() and Layout Groups").
            if (pendingSingleProfileMode is bool single)
            {
                pendingSingleProfileMode = null;
                singleProfileMode = single;
                RefreshProfileList();
                if (singleProfileMode)
                    view.ComputeDiff();
                else
                    matrixView.Refresh(detectedProfiles);
                Repaint();
            }
        }

        bool? pendingSingleProfileMode;

        void DrawModeAndProfileSelector()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var mode = GUILayout.Toolbar(singleProfileMode ? 1 : 0, ModeLabels, EditorStyles.toolbarButton, GUILayout.Width(200));
            if ((mode == 1) != singleProfileMode)
                pendingSingleProfileMode = mode == 1;

            if (!singleProfileMode)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                return;
            }

            if (detectedProfiles.Count > 0)
            {
                var newIndex = EditorGUILayout.Popup(selectedProfileIndex,
                    profileDisplayNames, EditorStyles.toolbarPopup, GUILayout.Width(250));
                if (newIndex != selectedProfileIndex && newIndex >= 0 && newIndex < detectedProfiles.Count)
                    SelectProfile(detectedProfiles[newIndex]);
            }
            else
            {
                EditorGUILayout.LabelField("No Unity Build Profiles found",
                    EditorStyles.toolbarButton, GUILayout.Width(250));
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
    }
}
