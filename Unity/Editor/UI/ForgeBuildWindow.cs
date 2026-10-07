using System;
using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.UI
{
    /// <summary>
    /// Main Build Forge build window. Auto-discovers all Build Forge Profiles in the project
    /// and lets the user select one to build.
    /// </summary>
    internal class ForgeBuildWindow : EditorWindow
    {
        List<ForgeProfile> allProfiles = new();
        ForgeProfile selectedProfile;
        Dictionary<ForgeProfile, List<string>> validationWarnings = new();
        static GUIStyle cachedWordWrapStyle;
        static GUIStyle cachedTitleStyle;
        static GUIStyle cachedSectionStyle;
        static GUIStyle cachedNoticeTextStyle;
        Vector2 listScroll;
        Vector2 detailsScroll;
        string selectedVariant; // null = default build; remembered for the editor session, seeded from the applied variant in a fresh editor
        const string SelectedVariantKey = "BuildForge.SelectedVariant";

        const float LeftPaneWidth = 220f;
        // The Build Forge anvil, also the Build Forge Profile asset icon, in
        // Unity's naming: ForgeProfile.png for the light theme, d_ForgeProfile.png
        // for the dark theme.
        const string IconFolder = "Packages/com.cortopiastudios.buildforge/Editor/Icons/";
        bool titleIconDark;

        [MenuItem("Window/Build Forge/Build", priority = 100)]
        static void ShowWindow()
        {
            var window = GetWindow<ForgeBuildWindow>();
            window.RefreshProfiles();
            window.Show();
        }

        void OnEnable()
        {
            // Set here rather than only when opened from the menu, so a window
            // restored from a saved layout gets the icon too.
            SetTitle();
            // Four action slots, three gaps and the horizontal margins must fit
            // even when a saved window layout is restored after a reload.
            minSize = new Vector2(720, 300);
            // SessionState outlives domain reloads but not the editor: in a fresh
            // editor nothing is stored and the selection follows the applied variant.
            selectedVariant = BuildVariants.RestoreSelection(
                SessionState.GetString(SelectedVariantKey, ""), ForgeEditorState.AppliedVariant);
            RefreshProfiles();
        }

        /// <summary>The selected variant if it is still configured, else none.</summary>
        string CurrentVariant
        {
            get
            {
                if (selectedVariant == null || ForgeSettings.instance.BuildVariants.Contains(selectedVariant))
                    return selectedVariant;
                return null;
            }
        }

        void SelectVariant(string variant)
        {
            selectedVariant = variant;
            SessionState.SetString(SelectedVariantKey, BuildVariants.StoreSelection(variant));
            RefreshProfiles();
            Repaint();
        }

        /// <summary>Makes <paramref name="variant"/> the selection, in open windows and for ones opened later this session (the main toolbar does after applying it).</summary>
        internal static void SetSelectedVariant(string variant)
        {
            SessionState.SetString(SelectedVariantKey, BuildVariants.StoreSelection(variant));
            foreach (var window in Resources.FindObjectsOfTypeAll<ForgeBuildWindow>())
                window.SelectVariant(variant);
        }

        static string WithVariant(string name, string variant)
            => string.IsNullOrEmpty(variant) ? name : $"{name} ({variant})";

        void OnInspectorUpdate()
        {
            // The window displays values resolved from external, mutable editor
            // state (the active profile, its platform settings, product name).
            // OnGUI repaints are event-driven, so a change made in another window
            // (e.g. toggling Build App Bundle in the Build Profiles window) is not
            // reflected until an event reaches this window. OnInspectorUpdate runs
            // ~10x/s while the window is visible; repainting here keeps the
            // display live without per-frame cost.
            Repaint();
        }

        void RefreshProfiles()
        {
            allProfiles = ForgeProfileLookup.FindAll()
                // By platform, then name, with profiles that have no Unity Build
                // Profile last, as in the main toolbar's menu. Not with a U+FFFF
                // sort key: culture comparison ignores that character.
                .OrderBy(p => p.BuildProfile == null)
                .ThenBy(p => p.BuildProfile != null ? BuildProfileUtility.GetBuildTarget(p.BuildProfile).ToString() : "",
                    System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.DisplayName)
                .ToList();

            if (selectedProfile != null && !allProfiles.Contains(selectedProfile))
                selectedProfile = null;

            if (selectedProfile == null)
            {
                var activeBuildProfile = BuildProfile.GetActiveBuildProfile();
                if (activeBuildProfile != null)
                    selectedProfile = allProfiles.FirstOrDefault(p => p.BuildProfile == activeBuildProfile);
            }

            validationWarnings.Clear();
            var plugins = ForgePluginRegistry.GetPlugins();
            foreach (var profile in allProfiles)
            {
                if (profile.BuildProfile == null) continue;
                BuildProfileDefines.EnsureDeferred(profile.BuildProfile);
                var warnings = new List<string>();
                if (ForgeSettings.instance.BuildVariants.Count > 0 && !profile.OutputPath.Contains("{Variant}"))
                    warnings.Add("The output path has no {Variant} placeholder, so a variant build overwrites the plain build.");
                if (ForgeSettings.instance.MaintainBuildProfileDefines)
                {
                    var collision = BuildProfileDefines.CollisionWarning(profile, allProfiles);
                    if (collision != null)
                        warnings.Add(collision);
                }
                var stray = BuildVariants.StrayWarning(profile, ForgeEditorState.IsApplied(profile));
                if (stray != null)
                    warnings.Add(stray);
                foreach (var plugin in plugins)
                {
                    if (ForgeSettings.instance.IsPluginDisabledWithDefault(
                        plugin.GetType().FullName, ForgePluginRegistry.IsSafeToDefaultEnable(plugin))) continue;
                    if (!plugin.IsApplicable(profile.BuildProfile)) continue;
                    if (plugin.IsEnabled(profile) == false) continue;
                    warnings.AddRange(plugin.Validate(profile, CurrentVariant));
                }
                if (warnings.Count > 0)
                    validationWarnings[profile] = warnings;
            }
        }

        /// <summary>The tab's title, with the anvil for the current editor theme.</summary>
        void SetTitle()
        {
            titleIconDark = EditorGUIUtility.isProSkin;
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconFolder + (titleIconDark ? "d_ForgeProfile.png" : "ForgeProfile.png"));
            titleContent = new GUIContent("Build Forge", icon);
        }

        void OnGUI()
        {
            // The editor theme can change while the window is open.
            if (titleIconDark != EditorGUIUtility.isProSkin)
                SetTitle();

            if (Event.current.type == EventType.Layout && allProfiles.Any(p => p == null))
                RefreshProfiles();

            // Same shape as the Build Profiles window: a fixed-width list pane on
            // the left, details for the selection on the right, and the action
            // bar pinned across the bottom. Each pane scrolls on its own.
            DrawToolbar();

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginVertical(GUILayout.Width(LeftPaneWidth));
            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.ExpandHeight(true));
            DrawProfileList();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            var divider = GUILayoutUtility.GetRect(1f, 1f, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(divider, new Color(0f, 0f, 0f, 0.35f));

            EditorGUILayout.BeginVertical();
            detailsScroll = EditorGUILayout.BeginScrollView(detailsScroll, GUILayout.ExpandHeight(true));
            DrawEditorState(ForgeEditorState.AppliedProfile);
            DrawDetails();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            DrawActionBar();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(EditorGUIUtility.TrTextContentWithIcon("Refresh", "Refresh profiles", "Refresh"),
                    EditorStyles.toolbarButton, GUILayout.Width(80)))
                RefreshProfiles();
            EditorGUILayout.EndHorizontal();
        }

        const float RowHeight = 22f;
        const float RowIconSize = 16f;

        /// <summary>
        /// The profile list, laid out like the Build Profiles window: one row per
        /// profile with the platform icon and the Build Forge profile name, click
        /// to select, double-click to open the inspector. Status is on the right
        /// of the row: a warning/error icon with the full text as tooltip, "Active"
        /// (Unity's state: its active Build Profile) and "Applied" (Build Forge's
        /// state: the ledger). Everything else about a profile is in the details
        /// panel below.
        /// </summary>
        void DrawProfileList()
        {
            EditorGUILayout.Space(6);
            // Rows carry the Unity Build Profile's name (the identity under the 1:1
            // rule), so the section is titled as in Unity's window, with its
            // section label's look: normal size, dimmed.
            cachedSectionStyle ??= new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = EditorStyles.label.normal.textColor * new Color(1f, 1f, 1f, 0.7f) },
            };
            EditorGUILayout.LabelField("Build Profiles", cachedSectionStyle);

            if (allProfiles.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No Build Profiles with Build Forge settings found.\n" +
                    "Select a Unity Build Profile asset and use Assets > Build Forge > Add Build Forge Settings.",
                    MessageType.Info);
                return;
            }

            var activeBuildProfile = BuildProfile.GetActiveBuildProfile();
            var appliedProfile = ForgeEditorState.AppliedProfile;
            foreach (var profile in allProfiles)
                DrawProfileRow(profile, activeBuildProfile, appliedProfile);
        }

        void DrawProfileRow(ForgeProfile profile, BuildProfile activeBuildProfile, ForgeProfile appliedProfile)
        {
            var rect = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
            var e = Event.current;
            var isSelected = selectedProfile == profile;
            var hasBuildProfile = profile.BuildProfile != null;
            var isActive = hasBuildProfile && profile.BuildProfile == activeBuildProfile;
            var isApplied = appliedProfile == profile;
            var sharedError = ForgeProfileLookup.SharedBuildProfileError(profile, allProfiles);
            validationWarnings.TryGetValue(profile, out var warnings);

            // Selection and hover, drawn like a Unity list row.
            if (e.type == EventType.Repaint)
            {
                if (isSelected)
                    EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                        ? new Color(0.17f, 0.36f, 0.53f)
                        : new Color(0.23f, 0.45f, 0.69f));
                else if (rect.Contains(e.mousePosition))
                    EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.06f));
            }

            // Platform icon + Build Forge profile name.
            var iconRect = new Rect(rect.x + 6f, rect.y + (RowHeight - RowIconSize) / 2f, RowIconSize, RowIconSize);
            var icon = hasBuildProfile
                ? PlatformIcon(BuildProfileUtility.GetBuildTarget(profile.BuildProfile))
                : null;
            if (icon == null)
                icon = AssetPreview.GetMiniThumbnail(profile);
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

            // Status, right-aligned: tags first (rightmost), then the issue icon.
            // "Active" is drawn like the Build Profiles window's tag (green outline).
            var right = rect.xMax - 6f;
            void DrawTag(string text, bool outlined)
            {
                var size = EditorStyles.miniLabel.CalcSize(new GUIContent(text));
                var tagRect = new Rect(right - size.x - 6f, rect.y + 3f, size.x + 6f, RowHeight - 6f);
                var prev = GUI.contentColor;
                if (outlined)
                {
                    var green = new Color(0.45f, 0.85f, 0.45f);
                    EditorGUI.DrawRect(new Rect(tagRect.x, tagRect.y, tagRect.width, 1f), green);
                    EditorGUI.DrawRect(new Rect(tagRect.x, tagRect.yMax - 1f, tagRect.width, 1f), green);
                    EditorGUI.DrawRect(new Rect(tagRect.x, tagRect.y, 1f, tagRect.height), green);
                    EditorGUI.DrawRect(new Rect(tagRect.xMax - 1f, tagRect.y, 1f, tagRect.height), green);
                    GUI.contentColor = green;
                }
                GUI.Label(tagRect, text, TagStyle);
                GUI.contentColor = prev;
                right = tagRect.x - 6f;
            }
            // Active sits at the far right, as in the Build Profiles window.
            if (isActive) DrawTag("Active", outlined: true);
            if (isApplied) DrawTag("Applied", outlined: false);

            GUIContent issue = null;
            if (sharedError != null)
                issue = EditorGUIUtility.TrIconContent("console.erroricon.sml", sharedError);
            else if (!hasBuildProfile)
                issue = EditorGUIUtility.TrIconContent("console.warnicon.sml", "No Unity Build Profile is assigned.");
            else if (warnings != null)
                issue = EditorGUIUtility.TrIconContent("console.warnicon.sml", string.Join("\n", warnings));
            if (issue != null)
            {
                var issueRect = new Rect(right - RowIconSize, rect.y + (RowHeight - RowIconSize) / 2f, RowIconSize, RowIconSize);
                GUI.Label(issueRect, issue);
                right = issueRect.x - 8f;
            }

            var nameRect = new Rect(iconRect.xMax + 6f, rect.y, right - iconRect.xMax - 6f, RowHeight);
            GUI.Label(nameRect, profile.DisplayName, isApplied ? EditorStyles.boldLabel : EditorStyles.label);

            // Click selects, double-click opens the inspector.
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                if (e.clickCount == 2)
                    Selection.activeObject = InspectorTarget(profile);
                selectedProfile = profile;
                e.Use();
                Repaint();
            }
        }

        // What opening a profile in the Inspector selects: its Unity Build Profile,
        // whose Inspector has Unity's settings and the Build Forge section. A
        // profile made in code is not stored in one and shows its own.
        internal static UnityEngine.Object InspectorTarget(ForgeProfile profile) =>
            profile.IsEmbedded ? profile.BuildProfile : profile;

        static GUIStyle cachedTagStyle;
        static GUIStyle TagStyle => cachedTagStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
        };

        static readonly Dictionary<BuildTarget, Texture> platformIconCache = new();

        /// <summary>Unity's small platform icon for the target, or null when the editor has none.</summary>
        internal static Texture PlatformIcon(BuildTarget target)
        {
            if (platformIconCache.TryGetValue(target, out var cached))
                return cached;
            // Unity names most platform icons after the build target group, under one of
            // the group's names (some groups have an old and a new name), and a few others
            // differently. The desktop icon is the fallback.
            var names = new List<string>();
            switch (target)
            {
                case BuildTarget.VisionOS: names.Add("visionOS"); break;
                case BuildTarget.WSAPlayer: names.Add("Metro"); break;
            }
            var group = BuildPipeline.GetBuildTargetGroup(target);
            names.AddRange(Enum.GetNames(typeof(BuildTargetGroup))
                .Where(n => Equals(Enum.Parse(typeof(BuildTargetGroup), n), group)));
            names.Add("Standalone");
            // FindTexture returns null (no log) for names this editor does not ship.
            var texture = names
                .Select(n => EditorGUIUtility.FindTexture($"BuildSettings.{n}.Small") ?? EditorGUIUtility.FindTexture($"BuildSettings.{n}"))
                .FirstOrDefault(t => t != null);
            platformIconCache[target] = texture;
            return texture;
        }

        /// <summary>
        /// Details for the selected profile: the Unity Build Profile, platform,
        /// output path, scripting define, the full text of any issues, and the
        /// CI command.
        /// </summary>
        void DrawDetails()
        {
            if (selectedProfile == null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Select a profile to see its details.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var profile = selectedProfile;
            var buildProfile = profile.BuildProfile;
            var isActive = buildProfile != null && buildProfile == BuildProfile.GetActiveBuildProfile();
            var sharedError = ForgeProfileLookup.SharedBuildProfileError(profile, allProfiles);

            EditorGUILayout.Space(8);
            DrawDetailsHeader(profile);
            EditorGUILayout.Space(12);

            if (sharedError != null)
                EditorGUILayout.HelpBox(sharedError, MessageType.Error);

            if (buildProfile == null)
            {
                EditorGUILayout.HelpBox("No Unity Build Profile is assigned. Assign one in the inspector.", MessageType.Warning);
                return;
            }

            var target = BuildProfileUtility.GetBuildTarget(buildProfile);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Unity Build Profile: icon + name, "(active)" is Unity's state, click pings.
            var content = new GUIContent(isActive ? $"{buildProfile.name} (active)" : buildProfile.name,
                AssetPreview.GetMiniThumbnail(buildProfile),
                "Click to show the Unity Build Profile in the Project window.");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Unity Build Profile");
            if (GUILayout.Button(content, isActive ? EditorStyles.boldLabel : EditorStyles.label,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                EditorGUIUtility.PingObject(buildProfile);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Build Forge Settings");
            if (GUILayout.Button(new GUIContent("Stored in the Unity Build Profile", AssetPreview.GetMiniThumbnail(profile),
                        "Click to edit the Build Forge settings in the Inspector, in the Unity Build Profile's Build Forge section."),
                    EditorStyles.label, GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                Selection.activeObject = InspectorTarget(profile);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Platform", target.ToString());

            var variant = CurrentVariant;
            var markedName = BuildVariants.MarkedProductName(PlayerSettings.productName, variant, BuildVariants.ShouldMark(variant));
            if (markedName != PlayerSettings.productName)
                EditorGUILayout.LabelField(new GUIContent("Product Name"),
                    new GUIContent(markedName, "Variant builds are marked in the product name (Project Settings > Build Forge > Build Variants)."));
            if (isActive)
            {
                var resolved = profile.GetResolvedOutputPath(target, variant);
                EditorGUILayout.LabelField(new GUIContent("Output"), new GUIContent(resolved, resolved));
            }
            else
            {
                // Path resolution reads active-profile state (the product name via
                // the static PlayerSettings API, the global app-bundle toggle for
                // the Android extension), so a non-active profile's path cannot be
                // resolved truthfully; show the template and say so.
                EditorGUILayout.LabelField(new GUIContent("Output"),
                    new GUIContent($"{profile.OutputPath}  (template, resolves when active)", profile.OutputPath));
            }

            var defines = ForgeSettings.instance.MaintainBuildProfileDefines ? BuildProfileDefines.DefineFor(buildProfile.name) : "";
            var variantDefine = BuildVariants.ExpectedDefine(variant, ForgeSettings.instance.BuildVariants.Count > 0);
            if (variantDefine != null)
                defines += (defines.Length > 0 ? ", " : "") + variantDefine;
            if (defines.Length > 0)
                EditorGUILayout.LabelField(defines.Contains(",") ? "Scripting Defines" : "Scripting Define", defines);

            var versionRule = BuildVariants.RuleFor(variant)?.Version;
            if (!string.IsNullOrWhiteSpace(versionRule))
            {
                // {Version} reads the active profile's Player Settings; {BuildNumber} is known only in the build.
                var shown = isActive
                    ? versionRule.Replace("{Version}", PlayerSettings.bundleVersion)
                        .Replace("{Variant}", BuildVariants.EffectiveName(variant, ForgeSettings.instance.BuildVariants.Count > 0) ?? "")
                    : versionRule;
                EditorGUILayout.LabelField(new GUIContent("Version"), new GUIContent(shown, versionRule));
                if (versionRule.Contains("{BuildNumber}") && !HasBuildNumber(profile, target))
                    EditorGUILayout.HelpBox("The variant rule's Version uses {BuildNumber}, but this profile's builds have " +
                                            "no build number, so they keep the version. Enable Build Number for the profile.",
                        MessageType.Warning);
            }

            var variantRule = BuildVariants.RuleFor(variant);
            var buildConfiguration = BuildVariants.DescribeBuildConfiguration(variantRule, buildProfile);
            if (buildConfiguration.Count > 0)
                EditorGUILayout.LabelField(new GUIContent("Build Configuration"),
                    new GUIContent(string.Join(", ", buildConfiguration),
                        "From the variant rule, for this build only (Project Settings > Build Forge > Variant Rules):\n" +
                        string.Join("\n", buildConfiguration)),
                    EditorStyles.wordWrappedLabel);
            var unusedOptions = VariantBuildSettings.UnusedDevelopmentOptions(buildProfile, variantRule);
            if (unusedOptions.Count > 0)
                EditorGUILayout.HelpBox($"The variant rule turns on {string.Join(", ", unusedOptions)}, which this build doesn't use: " +
                                        "Unity uses them only in development builds, and Wait For Managed Debugger only with Script Debugging.",
                    MessageType.Warning);

            string editorStatus;
            var appliedVariant = ForgeEditorState.AppliedVariant;
            if (!ForgeEditorState.HasEditorApplicableSettings(profile) && ForgeSettings.instance.BuildVariants.Count == 0)
                editorStatus = "no plugin settings to apply";
            else if (ForgeEditorState.IsApplied(profile))
            {
                var applied = ForgeEditorState.IsPlayModeXRApplied()
                    ? "plugin settings and Play Mode XR applied"
                    : "plugin settings applied";
                editorStatus = (ForgeEditorState.GetDrift().Count > 0 ? applied + ", drifted" : applied)
                               + (appliedVariant != null ? $" (variant {appliedVariant})"
                                   : ForgeSettings.instance.BuildVariants.Count > 0 ? " (default variant)" : "");
            }
            else if (isActive)
                editorStatus = "plugin settings not applied (Apply to make Play Mode match)";
            else
                editorStatus = "activate, then apply, to iterate on this profile in the editor";
            EditorGUILayout.LabelField("Editor", editorStatus);

            if (validationWarnings.TryGetValue(profile, out var warnings))
            {
                foreach (var warning in warnings)
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            EditorGUILayout.Space(6);
            var variantError = BuildVariants.ProfileError(profile, variant);
            if (variantError != null) EditorGUILayout.HelpBox(variantError, MessageType.Error);
            DrawPluginRunList(profile, variant);

            EditorGUILayout.Space(6);
            DrawCICommand();

            EditorGUILayout.EndVertical();
        }

        /// <summary>Large platform icon, profile name, and a one-line subtitle, like the Build Profiles window's header.</summary>
        void DrawDetailsHeader(ForgeProfile profile)
        {
            const float IconBox = 48f;
            var buildProfile = profile.BuildProfile;
            var icon = buildProfile != null ? PlatformIcon(BuildProfileUtility.GetBuildTarget(buildProfile)) : null;
            if (icon == null)
                icon = AssetPreview.GetMiniThumbnail(profile);
            var subtitle = buildProfile != null
                ? $"{BuildProfileUtility.GetBuildTarget(buildProfile)} \u00b7 Build Forge settings stored in the Build Profile"
                : $"Build Forge settings '{profile.ProfileName}' \u00b7 no Unity Build Profile";

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            var iconRect = GUILayoutUtility.GetRect(IconBox, IconBox, GUILayout.Width(IconBox), GUILayout.Height(IconBox));
            EditorGUI.DrawRect(iconRect, new Color(1f, 1f, 1f, 0.06f));
            if (icon != null)
                GUI.DrawTexture(new Rect(iconRect.x + 10f, iconRect.y + 10f, IconBox - 20f, IconBox - 20f), icon, ScaleMode.ScaleToFit);
            GUILayout.Space(10);
            // The text block must expand, or its width is the title's and the
            // subtitle gets clipped.
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Space(6);
            cachedTitleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            EditorGUILayout.LabelField(profile.DisplayName, cachedTitleStyle, GUILayout.Height(20f), GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(subtitle, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical(GUILayout.Width(120f));
            GUILayout.Space(6);
            if (GUILayout.Button("Open in Inspector", EditorStyles.miniButton, GUILayout.Width(120f)))
                Selection.activeObject = InspectorTarget(profile);
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Applied-profile status under the list: missing asset, drift against
        /// the state as applied, and the reminder that an applied profile lives
        /// in version-controlled settings assets.
        /// </summary>
        void DrawEditorState(ForgeProfile appliedProfile)
        {
            if (!ForgeEditorState.HasAppliedGuid)
                return;

            // These boxes explain the editor state; the actions live in the action
            // bar (Apply / Re-apply / Revert to Baseline for the selected profile),
            // so the only button here is the one the bar does not have.
            var canMutate = ForgeEditorState.CanMutate(out var reason);

            if (appliedProfile == null)
            {
                EditorGUILayout.HelpBox(ForgeEditorState.UnresolvedAppliedWarning, MessageType.Warning);
                return;
            }

            var mismatch = ForgeEditorState.GetActiveMismatch(out _);
            if (mismatch != null)
                EditorGUILayout.HelpBox(mismatch, MessageType.Warning);

            var drift = ForgeEditorState.GetDrift();
            if (drift.Count > 0)
            {
                var where = selectedProfile == appliedProfile
                    ? ""
                    : $" Select '{appliedProfile.DisplayName}' for those actions.";
                var text = $"Editor settings differ from '{appliedProfile.DisplayName}' as applied: " +
                    string.Join(", ", drift.Select(DescribeDriftKey)) + ". " +
                    "Re-apply restores the profile's state; Revert to Baseline undoes it." + where;
                var dismiss = new GUIContent("Dismiss Drift",
                    canMutate
                        ? "Stop reporting the current settings as drift. The profile is unchanged, and the " +
                          "next Apply or build rewrites the settings from it."
                        : reason);
                if (DrawWarningBoxWithButton(text, dismiss, canMutate))
                    EditorApplication.delayCall += () =>
                    {
                        ForgeEditorState.DismissDrift();
                        if (this != null) Repaint();
                    };
            }
            else
            {
                EditorGUILayout.LabelField(
                    $"'{appliedProfile.DisplayName}' is applied to the editor. Its settings live in " +
                    "version-controlled assets — revert to baseline before committing them, or commit deliberately.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        /// <summary>
        /// A warning box with its action inside, like Unity's own notices with a
        /// Dismiss button: icon and wrapped text on the left, the button on the
        /// right. Returns true when the button was clicked.
        /// </summary>
        static bool DrawWarningBoxWithButton(string text, GUIContent button, bool enabled)
        {
            var clicked = false;
            // No FlexibleSpace anywhere in here: inside the scroll view it would
            // stretch the box to fill the pane.
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.ExpandHeight(false));
            var icon = EditorGUIUtility.IconContent("console.warnicon");
            GUILayout.Label(icon, GUILayout.Width(32f), GUILayout.Height(32f));
            cachedNoticeTextStyle ??= new GUIStyle(EditorStyles.wordWrappedLabel) { alignment = TextAnchor.MiddleLeft };
            EditorGUILayout.LabelField(text, cachedNoticeTextStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(32f));
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            GUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(!enabled))
                clicked = GUILayout.Button(button, GUILayout.Height(20f));
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            return clicked;
        }

        /// <summary>
        /// Whether a build of the profile has a build number: the Build Number
        /// plugin runs for it, or the platform has its own (Android, iOS).
        /// </summary>
        static bool HasBuildNumber(ForgeProfile profile, BuildTarget target)
        {
            if (target == BuildTarget.Android || target == BuildTarget.iOS)
                return true;
            var plugin = ForgePluginRegistry.GetPlugins().FirstOrDefault(p => p is BuildForge.Editor.Plugins.BuildNumberPlugin);
            return plugin != null
                   && !ForgeSettings.instance.IsPluginDisabledWithDefault(plugin.GetType().FullName, ForgePluginRegistry.IsSafeToDefaultEnable(plugin))
                   && plugin.IsApplicable(profile.BuildProfile)
                   && plugin.IsEnabled(profile) != false;
        }

        /// <summary>"OpenXR (Android)" for a ledger key, falling back to the raw key.</summary>
        internal static string DescribeDriftKey(string key)
        {
            if (key == ForgeEditorState.VariantDriftKey)
                return "variant define";
            if (!EditorStateLedger.TryParseKey(key, out var typeName, out var group))
                return key;
            var mirror = ForgeEditorState.PlayModeMirrors().FirstOrDefault(m => m.GetType().FullName == typeName);
            if (mirror != null)
                return mirror.DisplayName;
            var plugin = ForgePluginRegistry.GetPlugins().FirstOrDefault(p => p.GetType().FullName == typeName);
            return $"{(plugin != null ? plugin.DisplayName : typeName)} ({group})";
        }

        /// <summary>
        /// The build plan for the selected profile: every discovered plugin in
        /// execution order, with the ones the build runner will skip greyed out
        /// and the reason next to them. Same three filters as ForgeBuildRunner:
        /// disabled globally, not applicable to the Build Profile, disabled for
        /// this profile.
        /// </summary>
        static void DrawPluginRunList(ForgeProfile profile, string variant)
        {
            var buildProfile = profile.BuildProfile;
            var plugins = ForgePluginRegistry.GetPlugins();
            // The build the Build button makes: same CI detection as the runner.
            var isCI = BuildEnvironment.IsCI;

            EditorGUILayout.LabelField($"Plugins for this build ({(isCI ? "CI" : "local")} build)", EditorStyles.miniBoldLabel);
            if (plugins.Count == 0)
            {
                EditorGUILayout.LabelField("No plugins discovered.", EditorStyles.miniLabel);
                return;
            }

            var step = 0;
            foreach (var plugin in plugins)
            {
                string skipReason = null;
                if (ForgeSettings.instance.IsPluginDisabledWithDefault(
                        plugin.GetType().FullName, ForgePluginRegistry.IsSafeToDefaultEnable(plugin)))
                    skipReason = "disabled in Project Settings";
                else if (!plugin.IsApplicable(buildProfile))
                    skipReason = plugin.NotApplicableReason(buildProfile) ?? "not applicable to this Unity Build Profile";
                else if (plugin.IsEnabled(profile) == false)
                    skipReason = "disabled for this profile";

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(12);
                if (skipReason == null)
                {
                    step++;
                    var what = plugin.DescribeBuild(profile, isCI, variant);
                    if (string.IsNullOrEmpty(what))
                    {
                        EditorGUILayout.LabelField(new GUIContent($"{step}.  {plugin.DisplayName}", plugin.Description));
                    }
                    else
                    {
                        EditorGUILayout.LabelField(new GUIContent($"{step}.  {plugin.DisplayName}", plugin.Description),
                            GUILayout.Width(EditorGUIUtility.labelWidth));
                        // Wrapped: OpenXR's Only Listed Features names every feature it turns off.
                        EditorGUILayout.LabelField(new GUIContent(what, what), EditorStyles.wordWrappedMiniLabel);
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.LabelField(new GUIContent($"\u2013   {plugin.DisplayName}", plugin.Description),
                            GUILayout.Width(EditorGUIUtility.labelWidth));
                    EditorGUILayout.LabelField(skipReason, EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();
            }

            if (step == 0)
                EditorGUILayout.LabelField("No plugin runs for this profile; the build is a plain Unity build with the manifest.",
                    EditorStyles.wordWrappedMiniLabel);
        }

        void DrawCICommand()
        {
            if (selectedProfile == null || selectedProfile.BuildProfile == null)
                return;

            EditorGUILayout.LabelField("CI Command", EditorStyles.miniBoldLabel);
            var (oneWorkspace, sharedWorkspace) = ForgeBuildRunner.GetCICommands(selectedProfile, CurrentVariant);
            cachedWordWrapStyle ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            DrawCICommandRow(OneWorkspaceLabel, oneWorkspace, "CI command");
            DrawCICommandRow(SharedWorkspaceLabel, sharedWorkspace, "shared-workspace CI commands");
        }

        static readonly GUIContent OneWorkspaceLabel = new GUIContent("One workspace per job",
            "For a workspace that only builds profiles of this platform, such as a job of its own for this profile.");

        static readonly GUIContent SharedWorkspaceLabel = new GUIContent("Shared workspace",
            "For a workspace that also builds profiles of other platforms, such as a job that builds any profile: " +
            "Activate switches it to this profile in a run of its own, then the build runs. Unity 6000.3.23 needs " +
            "this; see the manual's Command line and CI page.");

        void DrawCICommandRow(GUIContent label, string command, string copied)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            // Copy-to-clipboard as a compact text button at the right end of the
            // row, like Unity's own windows: the built-in glyphs do not fit
            // ("Clipboard" is an old two-tone icon, "TreeEditor.Duplicate" is the
            // tree editor's branch symbol).
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextArea(command, cachedWordWrapStyle, GUILayout.ExpandWidth(true));
            var copy = new GUIContent("Copy", $"Copy the {copied} to the clipboard.");
            if (GUILayout.Button(copy, EditorStyles.miniButton, GUILayout.Width(44f), GUILayout.Height(20f)))
            {
                GUIUtility.systemCopyBuffer = command;
                Debug.Log($"[Build Forge] Copied the {copied} to the clipboard.");
            }
            EditorGUILayout.EndHorizontal();
        }

        const float ActionButtonHeight = 24f;
        const float ActionButtonWidth = 170f;
        const float ActionGap = 6f;

        /// <summary>
        /// Bottom action bar, right-aligned like the Build Profiles window. Every
        /// slot is always drawn (Variant when variants exist, Revert to Baseline,
        /// Activate/Apply, Build) and is disabled with a reason instead of
        /// disappearing, so nothing shifts as the state changes; all slots share
        /// one width and one gap.
        /// </summary>
        void DrawActionBar()
        {
            var separator = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(separator, new Color(0f, 0f, 0f, 0.35f));
            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.FlexibleSpace();

            var canMutate = ForgeEditorState.CanMutate(out var mutateReason);
            var variants = ForgeSettings.instance.BuildVariants;
            var variant = CurrentVariant;
            var selection = selectedProfile != null && selectedProfile.BuildProfile != null
                && ForgeProfileLookup.SharedBuildProfileError(selectedProfile, allProfiles) == null
                ? selectedProfile : null;
            var activeBuildProfile = BuildProfile.GetActiveBuildProfile();
            var isActive = selection != null && selection.BuildProfile == activeBuildProfile;
            var name = selection != null ? selection.DisplayName : "";

            // Slot 1: variant (only when the project defines any).
            if (variants.Count > 0)
            {
                DrawVariantDropdown(variant, variants, canMutate, mutateReason);
                GUILayout.Space(ActionGap);
            }

            // Slot 2: Revert to Baseline.
            var somethingApplied = ForgeEditorState.HasAppliedGuid;
            DrawActionButton("Revert to Baseline",
                !somethingApplied ? "Nothing is applied to the editor." : canMutate ? "Restore every plugin-managed setting to its pre-apply state." : mutateReason,
                somethingApplied && canMutate, StartRevert);
            GUILayout.Space(ActionGap);

            // Slot 3: Activate (non-active selection) or Apply / Re-apply (active selection).
            if (selection == null)
            {
                DrawActionButton("Activate", "Select a profile with a Unity Build Profile.", false, null);
            }
            else if (!isActive)
            {
                DrawActionButton($"Activate {name}",
                    !canMutate ? mutateReason
                    : ForgeEditorState.IsApplied(selection)
                        ? "Makes this the active Unity Build Profile again. It is still applied, so its plugin settings are kept."
                        : "Makes this the active Unity Build Profile. Another profile's applied plugin settings are " +
                          "reverted to baseline first. Apply the profile afterwards so Play Mode matches it.",
                    canMutate, () => StartActivate(selection));
            }
            else
            {
                var isApplied = ForgeEditorState.IsApplied(selection);
                // Re-apply also removes overrides that were deleted or disabled
                // since Apply. Drift only observes editor changes, not config edits.
                var hasSomethingToApply = isApplied || ForgeEditorState.HasEditorApplicableSettings(selection) || variants.Count > 0;
                var label = isApplied ? $"Re-apply {WithVariant(name, variant)}" : $"Apply {WithVariant(name, variant)}";
                string reason;
                bool enabled;
                if (!hasSomethingToApply) { reason = "This profile has no plugin settings to apply."; enabled = false; }
                else if (BuildVariants.ProfileError(selection, variant) is string variantError) { reason = variantError; enabled = false; }
                else if (!canMutate) { reason = mutateReason; enabled = false; }
                else
                {
                    reason = "Writes the profile's plugin settings (such as OpenXR features) to the editor so " +
                             "Play Mode matches this profile. The baseline is recorded for Revert to Baseline.";
                    enabled = true;
                }
                var capturedVariant = variant;
                DrawActionButton(label, reason, enabled, () => StartApply(selection, capturedVariant));
            }
            GUILayout.Space(ActionGap);

            // Slot 4: Build (the primary action, green).
            {
                var label = selection != null ? $"Build {WithVariant(name, variant)}" : "Build";
                string reason;
                bool enabled;
                if (selection == null) { reason = "Select a profile with a Unity Build Profile."; enabled = false; }
                else if (BuildVariants.ProfileError(selection, variant) is string variantError) { reason = variantError; enabled = false; }
                else if (!isActive) { reason = "Activate the profile first; builds run on the active Unity Build Profile."; enabled = false; }
                else if (!canMutate) { reason = mutateReason; enabled = false; }
                else { reason = "Build this profile through Build Forge."; enabled = true; }

                var prevColor = GUI.backgroundColor;
                if (enabled) GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                var content = EditorGUIUtility.TrTextContentWithIcon(label, reason, "PlayButton");
                var capturedVariant = variant;
                using (new EditorGUI.DisabledScope(!enabled))
                {
                    if (GUILayout.Button(content, GUILayout.Height(ActionButtonHeight), GUILayout.Width(ActionButtonWidth)))
                        StartBuild(selection, capturedVariant);
                }
                GUI.backgroundColor = prevColor;
            }

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8);
        }

        static void DrawActionButton(string label, string tooltip, bool enabled, System.Action onClick)
        {
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUILayout.Button(new GUIContent(label, tooltip),
                        GUILayout.Height(ActionButtonHeight), GUILayout.Width(ActionButtonWidth)) && onClick != null)
                    onClick();
            }
        }

        /// <summary>
        /// Unity's pattern for a menu-opening control: reserve the rect up front
        /// (valid in every event), EditorGUI.DropdownButton on it, drop the menu
        /// at that rect. Drawn with the button style so it matches its siblings.
        /// </summary>
        void DrawVariantDropdown(string variant, IReadOnlyList<string> variants, bool canMutate, string mutateReason)
        {
            var caption = new GUIContent($"Variant: {variant ?? BuildVariants.DefaultLabel}  \u25be",
                canMutate ? "The build variant for Build and Apply (Project Settings > Build Forge > Build Variants)." : mutateReason);
            var rect = GUILayoutUtility.GetRect(caption, GUI.skin.button,
                GUILayout.Height(ActionButtonHeight), GUILayout.Width(ActionButtonWidth));
            using (new EditorGUI.DisabledScope(!canMutate))
            {
                if (EditorGUI.DropdownButton(rect, caption, FocusType.Passive, GUI.skin.button))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent(BuildVariants.DefaultLabel), variant == null, () => SelectVariant(null));
                    foreach (var entry in variants)
                    {
                        var captured = entry;
                        var error = BuildVariants.ProfileError(selectedProfile, captured);
                        if (error != null)
                            menu.AddDisabledItem(new GUIContent(captured + " (unavailable for this profile)", error));
                        else
                            menu.AddItem(new GUIContent(captured), variant == captured, () => SelectVariant(captured));
                    }
                    menu.DropDown(rect);
                }
            }
        }

        void StartBuild(ForgeProfile profile, string variant)
        {
            EditorApplication.delayCall += () => ForgeBuildRunner.RunBuild(profile, variant);
        }

        // Activating switches platforms (domain reload) and applying saves
        // assets, so like Build they run outside OnGUI. The window may not
        // survive a reload; refresh only if it did.
        void StartActivate(ForgeProfile profile)
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    ForgeEditorState.Activate(profile);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Build Forge] Activating '{profile.DisplayName}' failed: {e.Message}\n{e.StackTrace}");
                }
                if (this != null)
                {
                    RefreshProfiles();
                    Repaint();
                }
            };
        }

        void StartApply(ForgeProfile profile, string variant)
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    ForgeEditorState.Apply(profile, variant);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Build Forge] Applying '{profile.DisplayName}' failed: {e.Message}\n{e.StackTrace}");
                }
                if (this != null)
                {
                    RefreshProfiles();
                    Repaint();
                }
            };
        }

        /// <summary>
        /// A failed revert keeps the entries it could not restore, and while they are
        /// tracked every Apply, Activate, revert and build stops on them. When the
        /// cause is permanent (a removed package, a deleted XR loader), stopping
        /// tracking is the only way on, so offer it with the reasons.
        /// </summary>
        internal static void OfferToStopTracking(System.Exception failure)
        {
            var unrestored = (failure as System.AggregateException)?.InnerExceptions
                .OfType<EditorStateRestoreException>().ToList();
            if (unrestored == null || unrestored.Count == 0 || ForgeEditorState.Entries.Count == 0)
                return;
            var lines = string.Join("\n", unrestored.Select(u => $"\u2022 {DescribeDriftKey(u.Key)}: {u.Reason}"));
            if (!EditorUtility.DisplayDialog("Build Forge could not restore some settings",
                    $"Revert to Baseline could not restore:\n\n{lines}\n\n" +
                    "While Build Forge tracks them, Apply, Activate, Revert to Baseline and builds stop here. " +
                    "If the cause can be fixed, for example by reinstalling a removed package, keep tracking them " +
                    "and revert again. Otherwise stop tracking them: they keep their current values, and Build " +
                    "Forge no longer restores them.",
                    "Stop Tracking", "Keep Tracking"))
                return;
            try
            {
                ForgeEditorState.StopTracking();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Build Forge] Stop Tracking failed: {e.Message}\n{e.StackTrace}");
            }
        }

        void StartRevert()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    ForgeEditorState.RevertToBaseline();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Build Forge] Revert to Baseline failed: {e.Message}\n{e.StackTrace}");
                    OfferToStopTracking(e);
                }
                if (this != null)
                {
                    RefreshProfiles();
                    Repaint();
                }
            };
        }
    }
}
