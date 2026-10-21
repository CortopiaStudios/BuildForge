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
        static GUIStyle cachedTitleSeparatorStyle;
        static GUIStyle cachedSubtitleLayoutStyle;
        static GUIStyle cachedSectionStyle;
        static GUIStyle cachedNoticeTextStyle;
        Vector2 listScroll;
        Vector2 detailsScroll;
        string selectedVariant; // null until one is picked; remembered for the editor session, seeded from the applied variant in a fresh editor
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
            // The details header with its buttons, and the action bar's three actions
            // with their gaps and margins, must fit even when a saved window layout is
            // restored after a reload.
            minSize = new Vector2(720, 300);
            // SessionState outlives domain reloads but not the editor: in a fresh
            // editor nothing is stored and the selection follows the applied variant.
            // Stored right away, so that the main toolbar's Build buttons, which read
            // it, keep building what the window shows when the applied variant changes.
            selectedVariant = BuildVariants.RestoreSelection(
                SessionState.GetString(SelectedVariantKey, ""), ForgeEditorState.AppliedVariant);
            SessionState.SetString(SelectedVariantKey, BuildVariants.StoreSelection(selectedVariant));
            RefreshProfiles();
        }

        /// <summary>The selected variant if it is still configured, else none: Apply and the Build buttons then wait for a pick.</summary>
        string CurrentVariant
        {
            get
            {
                if (selectedVariant == null || ForgeSettings.instance.BuildVariants.Contains(selectedVariant))
                    return selectedVariant;
                return null;
            }
        }

        /// <summary>True when the project has variants and none is picked, so nothing can be applied or built yet.</summary>
        static bool AwaitsVariant(string variant) => variant == null && ForgeSettings.instance.BuildVariants.Count > 0;

        const string PickVariantReason = "Pick a variant in the details header first: once a project has variants, every build names one.";

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

        /// <summary>
        /// The variant Apply and the Build buttons use, for callers without a window (the main
        /// toolbar's Build buttons): the selection stored for this editor session, which
        /// a window stores as it opens; until then, in a fresh editor, the applied
        /// variant, as the window starts with; none while neither names a configured
        /// variant, and the Build buttons then wait for a pick.
        /// </summary>
        internal static string SessionVariant
        {
            get
            {
                var variant = BuildVariants.RestoreSelection(
                    SessionState.GetString(SelectedVariantKey, ""), ForgeEditorState.AppliedVariant);
                return variant == null || ForgeSettings.instance.BuildVariants.Contains(variant) ? variant : null;
            }
        }

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
                .ThenBy(p => p.BuildProfile != null ? PlatformName(p.BuildProfile) : "", System.StringComparer.OrdinalIgnoreCase)
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
                var warnings = ForgeBuildRunner.ProfileWarnings(profile, allProfiles);
                var stray = BuildVariants.StrayWarning(profile, ForgeEditorState.IsApplied(profile));
                if (stray != null)
                    warnings.Add(stray);
                foreach (var plugin in plugins)
                {
                    if (ForgeSettings.instance.IsPluginDisabledWithDefault(
                        plugin.GetType().FullName, ForgePluginRegistry.IsEnabledByDefault(plugin))) continue;
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
            titleContent = new GUIContent("Build Forge", ForgeIcon());
        }

        /// <summary>The anvil in the current editor theme's version.</summary>
        static Texture2D ForgeIcon()
            => AssetDatabase.LoadAssetAtPath<Texture2D>(IconFolder + (EditorGUIUtility.isProSkin ? "d_ForgeProfile.png" : "ForgeProfile.png"));

        /// <summary>
        /// The Build buttons' icons, in the action bar and the main toolbar: the anvil
        /// on Build, Unity's Play icon on Build and Run.
        /// </summary>
        internal static Texture2D BuildIcon(bool run)
            => run ? EditorGUIUtility.IconContent("PlayButton").image as Texture2D : ForgeIcon();

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
            DrawUnfinishedBuild();
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
            var icon = hasBuildProfile ? PlatformIcon(profile.BuildProfile) : null;
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
            var blockingError = hasBuildProfile ? ForgePluginRegistry.BlockingError(profile) : null;
            if (sharedError != null)
                issue = EditorGUIUtility.TrIconContent("console.erroricon.sml", sharedError);
            else if (blockingError != null)
                issue = EditorGUIUtility.TrIconContent("console.erroricon.sml", blockingError);
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
            // FindTexture returns null (no log) for names this editor does not ship. IconContent
            // loads the name as Unity's windows do, as the @2x texture on a high-DPI display.
            var name = names.SelectMany(n => new[] { $"BuildSettings.{n}.Small", $"BuildSettings.{n}" })
                .FirstOrDefault(n => EditorGUIUtility.FindTexture(n) != null);
            var texture = name != null ? EditorGUIUtility.IconContent(name).image : null;
            platformIconCache[target] = texture;
            return texture;
        }

        /// <summary>
        /// Unity's small icon for the profile's own platform, as in the Build Profiles
        /// window's list: Meta Quest's for a Meta Quest profile, which builds for Android. The
        /// build target's icon when Unity's platform lookup is unavailable.
        /// </summary>
        internal static Texture PlatformIcon(BuildProfile profile)
            => BuildProfilePlatform.Icon(profile, small: true) ?? BuildProfilePlatform.Icon(profile, small: false)
                ?? PlatformIcon(BuildProfileUtility.GetBuildTarget(profile));

        /// <summary>
        /// Unity's large icon for the profile's own platform, as in the Build Profiles
        /// window's header; the small one when Unity's platform lookup is unavailable.
        /// </summary>
        internal static Texture LargePlatformIcon(BuildProfile profile)
            => BuildProfilePlatform.Icon(profile, small: false) ?? PlatformIcon(profile);

        /// <summary>
        /// The profile's own platform, as Unity's Build Profiles window names it ("Meta
        /// Quest", "Windows"); the build target when Unity's platform lookup is unavailable.
        /// </summary>
        internal static string PlatformName(BuildProfile profile)
            => BuildProfilePlatform.DisplayName(profile) ?? BuildProfileUtility.GetBuildTarget(profile).ToString();

        /// <summary>
        /// The Platform row: the platform, and the build target it builds for when that has
        /// another name, "Meta Quest (Android)" or "Windows (StandaloneWindows64)", but "Android™".
        /// </summary>
        internal static string PlatformLabel(string platform, BuildTarget target)
            => string.Equals(new string(platform.Where(char.IsLetterOrDigit).ToArray()), target.ToString(), StringComparison.OrdinalIgnoreCase)
                ? platform
                : $"{platform} ({target})";

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
            // Both rows' icons are measured and drawn at 16 px: the anvil is a 256 px
            // texture, and measured as such it set the details pane's minimum width, so
            // a narrow window scrolled the pane sideways, out of sight of the header's buttons.
            using (new EditorGUIUtility.IconSizeScope(new Vector2(16f, 16f)))
            {
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
            }

            EditorGUILayout.LabelField("Platform", PlatformLabel(PlatformName(buildProfile), target));

            var variant = CurrentVariant;
            var markedName = BuildVariants.MarkedProductName(PlayerSettings.productName, variant, BuildVariants.ShouldMark(variant));
            if (markedName != PlayerSettings.productName)
                EditorGUILayout.LabelField(new GUIContent("Product Name"),
                    new GUIContent(markedName, "Variant builds are marked in the product name (Project Settings > Build Forge > Build Variants)."));
            if (isActive && !AwaitsVariant(variant))
            {
                var resolved = profile.GetResolvedOutputPath(target, variant);
                EditorGUILayout.LabelField(new GUIContent("Output"), new GUIContent(resolved, resolved));
            }
            else if (isActive)
            {
                EditorGUILayout.LabelField(new GUIContent("Output"),
                    new GUIContent($"{profile.OutputPath}  (template, resolves once a variant is picked)", profile.OutputPath));
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
            var variantDefine = BuildVariants.ExpectedDefine(variant);
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
                        .Replace("{Variant}", variant ?? "")
                    : versionRule;
                EditorGUILayout.LabelField(new GUIContent("Version"), new GUIContent(shown, versionRule));
                if (versionRule.Contains("{BuildNumber}") && !ForgeBuildRunner.HasBuildNumber(profile, target))
                    EditorGUILayout.HelpBox("The variant rule's Version uses {BuildNumber}, but this profile's builds have " +
                                            "no build number, so a local build keeps the version and a CI build stops. " +
                                            "Enable Build Number for the profile.",
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
                                   : ForgeSettings.instance.BuildVariants.Count > 0 ? " (no variant)" : "");
            }
            else if (isActive)
                editorStatus = "plugin settings not applied (Apply to make Play Mode match)";
            else
                editorStatus = "activate, then apply, to iterate on this profile in the editor";
            EditorGUILayout.LabelField("Editor", editorStatus);

            // Live, not from the refresh: Apply and Build stop for it, and their buttons follow it.
            var blockingError = ForgePluginRegistry.BlockingError(profile);
            if (blockingError != null)
                EditorGUILayout.HelpBox(blockingError, MessageType.Error);
            if (validationWarnings.TryGetValue(profile, out var warnings))
            {
                foreach (var warning in warnings)
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            EditorGUILayout.Space(6);
            if (AwaitsVariant(variant))
            {
                // What a build does, and its command, depend on the variant it names.
                var configurationError = BuildVariants.ConfigurationError(profile);
                if (configurationError != null) EditorGUILayout.HelpBox(configurationError, MessageType.Error);
                EditorGUILayout.HelpBox("Pick a variant in the header to see the plugins its build runs and its CI command, " +
                                        "and to apply or build it. Once a project has variants, every build names one.", MessageType.Info);
            }
            else
            {
                var variantError = BuildVariants.ProfileError(profile, variant);
                if (variantError != null) EditorGUILayout.HelpBox(variantError, MessageType.Error);
                DrawPluginRunList(profile, variant);

                EditorGUILayout.Space(6);
                DrawCICommand();
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>Large platform icon, profile name, and a one-line subtitle, like the Build Profiles window's header.</summary>
        void DrawDetailsHeader(ForgeProfile profile)
        {
            const float IconBox = 48f;
            var buildProfile = profile.BuildProfile;
            var icon = buildProfile != null ? LargePlatformIcon(buildProfile) : null;
            if (icon == null)
                icon = AssetPreview.GetMiniThumbnail(profile);
            var subtitle = buildProfile != null
                ? $"{PlatformName(buildProfile)} \u00b7 Build Forge settings stored in the Build Profile"
                : $"Build Forge settings '{profile.ProfileName}' \u00b7 no Unity Build Profile";

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            var iconRect = GUILayoutUtility.GetRect(IconBox, IconBox, GUILayout.Width(IconBox), GUILayout.Height(IconBox));
            // The Build Profiles window's .platform-icon-large (6000.3 to 6000.6): corners
            // rounded by 5 points, --unity-theme-background-color-53 and the icon at its
            // 32 points, centered.
            var boxColor = EditorGUIUtility.isProSkin ? new Color32(42, 42, 42, 255) : new Color32(240, 240, 240, 255);
            GUI.DrawTexture(iconRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, boxColor, 0f, 5f);
            if (icon != null)
                GUI.DrawTexture(new Rect(iconRect.x + 8f, iconRect.y + 8f, IconBox - 16f, IconBox - 16f), icon, ScaleMode.ScaleToFit);
            GUILayout.Space(10);
            // The text block must expand, or its width is the title's and the
            // subtitle gets clipped.
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Space(6);
            // Width 0: the title line and the subtitle give way to the button beside them and
            // are cut, or a narrow window scrolls the details pane sideways, hiding the button.
            DrawTitleLine(GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true)), profile);
            // Laid out like the title line, so it starts where the title does. EditorGUILayout's
            // label field lays out its rect with the layerMaskField style, whatever style it
            // draws with, and that style's left margin set it right of the title; its top and
            // bottom margins keep the line's height.
            if (cachedSubtitleLayoutStyle == null)
            {
                var margin = EditorStyles.layerMaskField.margin;
                cachedSubtitleLayoutStyle = new GUIStyle { margin = new RectOffset(0, margin.right, margin.top, margin.bottom) };
            }
            GUI.Label(GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight, cachedSubtitleLayoutStyle, GUILayout.ExpandWidth(true)),
                subtitle, EditorStyles.miniLabel);
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
        /// <summary>
        /// A build that did not finish stops the next one until it is cleared here, once the
        /// files it changed are restored (see <see cref="InterruptedBuildMarker"/>).
        /// </summary>
        void DrawUnfinishedBuild()
        {
            var unfinished = InterruptedBuildMarker.Error();
            if (unfinished == null)
                return;
            var canMutate = ForgeEditorState.CanMutate(out var reason);
            DrawWarningBoxWithButtons(unfinished, (new GUIContent("Clear",
                    canMutate ? "Builds can start again. Clear only once the files that build changed are restored." : reason),
                canMutate, () =>
                {
                    if (EditorUtility.DisplayDialog("Clear the unfinished build?",
                            "Builds then start from the project files as they are. Clear only after restoring the files the " +
                            "build that did not finish changed (check git status and git diff).", "Clear", "Cancel"))
                    {
                        InterruptedBuildMarker.Clear();
                        Debug.Log("[Build Forge] Cleared the record of a build that did not finish.");
                    }
                }));
        }

        void DrawEditorState(ForgeProfile appliedProfile)
        {
            if (!ForgeEditorState.HasAppliedGuid)
                return;

            // These boxes explain the editor state. Whichever is shown last carries
            // Revert to Baseline, which the action bar has only for the active profile,
            // in the Apply button's menu; the drift box also has Dismiss Drift.
            var canMutate = ForgeEditorState.CanMutate(out var reason);
            (GUIContent content, bool enabled, System.Action onClick) revert = (new GUIContent("Revert to Baseline",
                canMutate ? "Restore every plugin-managed setting to its pre-apply state." : reason), canMutate, StartRevert);

            if (appliedProfile == null)
            {
                var unfinished = ForgeEditorState.InterruptedError();
                DrawWarningBoxWithButtons(unfinished != null
                    ? ForgeEditorState.UnresolvedAppliedWarning + " " + unfinished
                    : ForgeEditorState.UnresolvedAppliedWarning, revert);
                return;
            }

            var mismatch = ForgeEditorState.GetActiveMismatch(out _);
            if (mismatch != null)
                EditorGUILayout.HelpBox(mismatch, MessageType.Warning);

            var drift = ForgeEditorState.GetDrift();
            if (drift.Count > 0)
            {
                // The action bar has Re-apply only for the active profile.
                var where = appliedProfile.BuildProfile != BuildProfile.GetActiveBuildProfile()
                    ? $" Activate '{appliedProfile.DisplayName}' again, then re-apply it."
                    : selectedProfile == appliedProfile
                        ? ""
                        : $" Select '{appliedProfile.DisplayName}' to re-apply it.";
                // An Apply or build that did not finish has its own message; the rest is ordinary drift.
                var interrupted = ForgeEditorState.InterruptedError();
                var drifted = drift.Where(key => !IsUnfinishedDriftKey(key)).ToList();
                var parts = new List<string>();
                if (interrupted != null)
                    parts.Add(interrupted);
                if (drifted.Count > 0)
                    parts.Add($"Editor settings differ from '{appliedProfile.DisplayName}' as applied: " +
                              string.Join(", ", drifted.Select(DescribeDriftKey)) + ". " +
                              "Re-apply restores the profile's state; Revert to Baseline undoes it.");
                if (where.Length > 0)
                    parts.Add(where.Trim());
                var text = string.Join(" ", parts);
                (GUIContent content, bool enabled, System.Action onClick) dismiss = (new GUIContent("Dismiss Drift",
                    canMutate
                        ? "Stop reporting the current settings as drift. The profile is unchanged, and the " +
                          "next Apply or build rewrites the settings from it." +
                          (drift.Contains(ForgeEditorState.VariantDriftKey)
                              ? " The missing variant define stays reported; Re-apply restores it."
                              : "")
                        : reason), canMutate, () => EditorApplication.delayCall += () =>
                    {
                        ForgeEditorState.DismissDrift();
                        if (this != null) Repaint();
                    });
                if (OffersDismissDrift(drift))
                    DrawWarningBoxWithButtons(text, dismiss, revert);
                else
                    DrawWarningBoxWithButtons(text, revert);
            }
            else
            {
                // No FlexibleSpace: inside the scroll view it would stretch the row.
                EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(false));
                EditorGUILayout.LabelField(
                    $"'{appliedProfile.DisplayName}' is applied to the editor. Its settings live in " +
                    "version-controlled assets — revert to baseline before committing them, or commit deliberately.",
                    EditorStyles.wordWrappedMiniLabel, GUILayout.ExpandWidth(true));
                using (new EditorGUI.DisabledScope(!revert.enabled))
                {
                    if (GUILayout.Button(revert.content, GUILayout.Height(20f), GUILayout.ExpandWidth(false)))
                        revert.onClick();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// A warning box with its actions inside, like Unity's own notices with a
        /// Dismiss button: icon and wrapped text on the left, the buttons stacked on
        /// the right, each with its own enabled state.
        /// </summary>
        static void DrawWarningBoxWithButtons(string text, params (GUIContent content, bool enabled, System.Action onClick)[] buttons)
        {
            // No FlexibleSpace anywhere in here: inside the scroll view it would
            // stretch the box to fill the pane.
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.ExpandHeight(false));
            var icon = EditorGUIUtility.IconContent("console.warnicon");
            GUILayout.Label(icon, GUILayout.Width(32f), GUILayout.Height(32f));
            cachedNoticeTextStyle ??= new GUIStyle(EditorStyles.wordWrappedLabel) { alignment = TextAnchor.MiddleLeft };
            EditorGUILayout.LabelField(text, cachedNoticeTextStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(32f));
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            GUILayout.Space(6f);
            foreach (var (content, enabled, onClick) in buttons)
            {
                using (new EditorGUI.DisabledScope(!enabled))
                {
                    if (GUILayout.Button(content, GUILayout.Height(20f)) && onClick != null)
                        onClick();
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Whether a build of the profile has a build number: the Build Number
        /// plugin runs for it, or the platform has its own (Android, iOS).
        /// </summary>
        /// <summary>"OpenXR (Android)" for a ledger key, falling back to the raw key.</summary>
        internal static string DescribeDriftKey(string key) => ForgeEditorState.DescribeKey(key);

        /// <summary>
        /// Whether the drift notice offers Dismiss Drift: only while plugin settings
        /// have drifted, which it accepts. A missing variant define, and settings whose
        /// Apply or build did not finish, stay reported until Re-apply or Revert to Baseline.
        /// </summary>
        internal static bool OffersDismissDrift(IReadOnlyList<string> drift)
            => drift.Any(key => key != ForgeEditorState.VariantDriftKey && !IsUnfinishedDriftKey(key));

        /// <summary>A drift key of settings whose Apply or build did not finish.</summary>
        internal static bool IsUnfinishedDriftKey(string key)
            => key.EndsWith(EditorStateApplier.ApplyUnfinishedSuffix, System.StringComparison.Ordinal)
               || key.EndsWith(EditorStateApplier.BuildUnfinishedSuffix, System.StringComparison.Ordinal);

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
            // The name column fits the longest name, from the standard label width up
            // to 40% of the details pane, so long plugin names aren't cut off.
            var nameWidth = NameColumnWidth(plugins, (EditorGUIUtility.currentViewWidth - LeftPaneWidth) * 0.4f);
            foreach (var plugin in plugins)
            {
                string skipReason = null;
                if (ForgeSettings.instance.IsPluginDisabledWithDefault(
                        plugin.GetType().FullName, ForgePluginRegistry.IsEnabledByDefault(plugin)))
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
                        EditorGUILayout.LabelField(
                            FitContent($"{step}.  {plugin.DisplayName}", plugin.Description, null, EditorStyles.label, nameWidth),
                            GUILayout.Width(nameWidth));
                        // Wrapped: OpenXR's Only Listed Features names every feature it turns off.
                        EditorGUILayout.LabelField(new GUIContent(what, what), EditorStyles.wordWrappedMiniLabel);
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.LabelField(
                            FitContent($"\u2013   {plugin.DisplayName}", plugin.Description, null, EditorStyles.label, nameWidth),
                            GUILayout.Width(nameWidth));
                    EditorGUILayout.LabelField(skipReason, EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();
            }

            if (step == 0)
                EditorGUILayout.LabelField("No plugin runs for this profile; the build is a plain Unity build with the manifest.",
                    EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>The width of the plugin list's name column: its longest entry, at least the standard label width, at most <paramref name="cap"/>.</summary>
        internal static float NameColumnWidth(IReadOnlyList<IForgePlugin> plugins, float cap)
        {
            var prefix = $"{plugins.Count}.  ";
            var widest = 0f;
            foreach (var plugin in plugins)
                widest = Mathf.Max(widest, EditorStyles.label.CalcSize(new GUIContent(prefix + plugin.DisplayName)).x);
            var min = EditorGUIUtility.labelWidth;
            return Mathf.Clamp(widest, min, Mathf.Max(min, cap));
        }

        void DrawCICommand()
        {
            if (selectedProfile == null || selectedProfile.BuildProfile == null)
                return;

            EditorGUILayout.LabelField("CI Command", EditorStyles.miniBoldLabel);
            var (build, activate, shellWarning) = ForgeBuildRunner.GetCICommands(selectedProfile, CurrentVariant);
            cachedWordWrapStyle ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            if (shellWarning != null)
                EditorGUILayout.HelpBox(shellWarning, MessageType.Warning);
            DrawCICommandRow(OneWorkspaceLabel, build,
                (new GUIContent("Copy", "Copy the build command to the clipboard."), build, "build command"));
            // Two buttons, one per CI step: pasted as one script, the build would also run after a failed Activate.
            DrawCICommandRow(SharedWorkspaceLabel, activate + "\n" + build,
                (new GUIContent("Activate", "Copy the Activate command, for a CI step of its own before the build."), activate, "Activate command"),
                (new GUIContent("Build", "Copy the build command, for the next CI step, run only if Activate succeeded."), build, "build command"));
        }

        static readonly GUIContent OneWorkspaceLabel = new GUIContent("One workspace per job",
            "For a workspace that only builds profiles of this platform, such as a job of its own for this profile.");

        static readonly GUIContent SharedWorkspaceLabel = new GUIContent("Shared workspace",
            "For a workspace that also builds profiles of other platforms, such as a job that builds any profile: " +
            "Activate switches it to this profile in a CI step of its own, and the build runs in the next step, only " +
            "if Activate succeeded. Unity 6000.3.23 needs this; see the manual's Command line and CI page.");

        void DrawCICommandRow(GUIContent label, string command, params (GUIContent Button, string Text, string Copied)[] copies)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            // Copy-to-clipboard as compact text buttons at the right end of the
            // row, like Unity's own windows: the built-in glyphs do not fit
            // ("Clipboard" is an old two-tone icon, "TreeEditor.Duplicate" is the
            // tree editor's branch symbol).
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextArea(command, cachedWordWrapStyle, GUILayout.ExpandWidth(true));
            EditorGUILayout.BeginVertical(GUILayout.Width(60f));
            foreach (var (button, text, copied) in copies)
            {
                if (GUILayout.Button(button, EditorStyles.miniButton, GUILayout.Width(60f), GUILayout.Height(20f)))
                {
                    GUIUtility.systemCopyBuffer = text;
                    Debug.Log($"[Build Forge] Copied the {copied} to the clipboard.");
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        const float ActionButtonHeight = 24f;
        const float ActionButtonWidth = 170f;
        const float ActionGap = 6f;
        const float MenuArrowWidth = 20f;

        /// <summary>
        /// Bottom action bar, with the actions the selected profile has: Activate, on the
        /// left, for a profile that is not the active one; for the active one, on the
        /// right as in the Build Profiles window, Apply or Re-apply with Revert to
        /// Baseline in its menu, Build and Build and Run. Each side keeps to its edge, so
        /// no button moves when the selection changes, and a button the profile has is
        /// disabled with a reason rather than hidden. The variant is picked in the
        /// details header, since the details show it for any profile.
        /// </summary>
        void DrawActionBar()
        {
            var separator = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(separator, new Color(0f, 0f, 0f, 0.35f));
            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);

            var canMutate = ForgeEditorState.CanMutate(out var mutateReason);
            var variants = ForgeSettings.instance.BuildVariants;
            var variant = CurrentVariant;
            var selection = selectedProfile != null && selectedProfile.BuildProfile != null
                && ForgeProfileLookup.SharedBuildProfileError(selectedProfile, allProfiles) == null
                ? selectedProfile : null;
            var activeBuildProfile = BuildProfile.GetActiveBuildProfile();
            var isActive = selection != null && selection.BuildProfile == activeBuildProfile;
            var blockingError = selection != null ? ForgePluginRegistry.BlockingError(selection) : null;

            // The labels leave out the profile, which the details pane names above, and
            // the variant, which the details header shows, so they fit the fixed slots.

            if (!isActive)
            {
                // Activate, for a selected profile that is not the active one.
                if (selection == null)
                    DrawActionButton("Activate", "Select a profile with a Unity Build Profile.", false, null);
                else
                    DrawActionButton("Activate",
                        !canMutate ? mutateReason
                        : ForgeEditorState.IsApplied(selection)
                            ? "Makes this the active Unity Build Profile again. It is still applied, so its plugin settings are kept."
                            : "Makes this the active Unity Build Profile. Another profile's applied plugin settings are " +
                              "reverted to baseline first. Apply the profile afterwards so Play Mode matches it.",
                        canMutate, () => StartActivate(selection));
                GUILayout.FlexibleSpace();
            }
            else
            {
                GUILayout.FlexibleSpace();

                // Apply or Re-apply, with Revert to Baseline in its menu: the two directions
                // of one applied state. Revert is for whatever is applied, so its part has
                // its own enabled state.
                {
                    var isApplied = ForgeEditorState.IsApplied(selection);
                    // Re-apply also removes overrides that were deleted or disabled
                    // since Apply. Drift only observes editor changes, not config edits.
                    var hasSomethingToApply = isApplied || ForgeEditorState.HasEditorApplicableSettings(selection) || variants.Count > 0;
                    string reason;
                    bool enabled;
                    if (!hasSomethingToApply) { reason = "This profile has no plugin settings to apply."; enabled = false; }
                    else if (AwaitsVariant(variant)) { reason = PickVariantReason; enabled = false; }
                    else if (BuildVariants.ProfileError(selection, variant) is string variantError) { reason = variantError; enabled = false; }
                    else if (blockingError != null) { reason = blockingError; enabled = false; }
                    else if (!canMutate) { reason = mutateReason; enabled = false; }
                    else
                    {
                        reason = "Writes the profile's plugin settings (such as OpenXR features) to the editor so " +
                                 "Play Mode matches this profile. The baseline is recorded for Revert to Baseline.";
                        enabled = true;
                    }
                    var somethingApplied = ForgeEditorState.HasAppliedGuid;
                    var capturedVariant = variant;
                    DrawButtonWithMenu(isApplied ? "Re-apply" : "Apply", reason, enabled, () => StartApply(selection, capturedVariant),
                        !somethingApplied ? "Revert to Baseline: nothing is applied to the editor."
                        : canMutate ? "Revert to Baseline: restore every plugin-managed setting to its pre-apply state."
                        : mutateReason,
                        somethingApplied && canMutate,
                        menu => menu.AddItem(new GUIContent("Revert to Baseline"), false, StartRevert));
                }
                GUILayout.Space(ActionGap);

                // Build and Build and Run, the primary actions (green), enabled alike.
                {
                    string reason;
                    bool enabled;
                    if (AwaitsVariant(variant)) { reason = PickVariantReason; enabled = false; }
                    else if (BuildVariants.ProfileError(selection, variant) is string variantError) { reason = variantError; enabled = false; }
                    else if (blockingError != null) { reason = blockingError; enabled = false; }
                    else if (InterruptedBuildMarker.Error() is string unfinished) { reason = unfinished; enabled = false; }
                    else if (!canMutate) { reason = mutateReason; enabled = false; }
                    else { reason = null; enabled = true; }

                    var capturedVariant = variant;
                    var prevColor = GUI.backgroundColor;
                    if (enabled) GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                    DrawActionButton("Build", reason ?? "Build this profile through Build Forge.", enabled,
                        () => StartBuild(selection, capturedVariant, runPlayer: false), BuildIcon(false));
                    GUILayout.Space(ActionGap);
                    DrawActionButton("Build and Run", reason ?? "Build this profile through Build Forge, then run it.", enabled,
                        () => StartBuild(selection, capturedVariant, runPlayer: true), BuildIcon(true));
                    GUI.backgroundColor = prevColor;
                }
            }

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8);
        }

        /// <summary>
        /// A slot split into a button and a ▾ part that opens a menu, like Unity's
        /// Build button with its options. Each part has its own enabled state and
        /// tooltip; the menu drops below the whole slot.
        /// </summary>
        static void DrawButtonWithMenu(string label, string tooltip, bool enabled, System.Action onClick,
            string menuTooltip, bool menuEnabled, System.Action<GenericMenu> fillMenu)
        {
            var rect = GUILayoutUtility.GetRect(ActionButtonWidth, ActionButtonHeight, GUI.skin.button,
                GUILayout.Width(ActionButtonWidth), GUILayout.Height(ActionButtonHeight));
            var buttonRect = new Rect(rect.x, rect.y, rect.width - MenuArrowWidth, rect.height);
            var arrowRect = new Rect(buttonRect.xMax, rect.y, MenuArrowWidth, rect.height);
            // The editor skin's joined button halves, as in Unity's own split buttons.
            var left = GUI.skin.FindStyle("ButtonLeft") ?? GUI.skin.button;
            var right = GUI.skin.FindStyle("ButtonRight") ?? GUI.skin.button;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUI.Button(buttonRect, FitContent(label, tooltip, null, left, buttonRect.width), left) && onClick != null)
                    onClick();
            }
            using (new EditorGUI.DisabledScope(!menuEnabled))
            {
                if (EditorGUI.DropdownButton(arrowRect, new GUIContent("▾", menuTooltip), FocusType.Passive, right))
                {
                    var menu = new GenericMenu();
                    fillMenu(menu);
                    menu.DropDown(rect);
                }
            }
        }

        static void DrawActionButton(string label, string tooltip, bool enabled, System.Action onClick, Texture image = null)
        {
            using (new EditorGUI.DisabledScope(!enabled))
            {
                // Measured and drawn at 16 px: the anvil is a 256 px texture.
                bool clicked;
                using (new EditorGUIUtility.IconSizeScope(new Vector2(16f, 16f)))
                    clicked = GUILayout.Button(FitContent(label, tooltip, image, GUI.skin.button, ActionButtonWidth),
                        GUILayout.Height(ActionButtonHeight), GUILayout.Width(ActionButtonWidth));
                if (clicked && onClick != null)
                    onClick();
            }
        }

        /// <summary>
        /// <paramref name="label"/> for a control <paramref name="width"/> wide: a label
        /// that doesn't fit ends in "…", since Unity cuts centered text at both ends,
        /// and the tooltip then starts with the whole label. <paramref name="suffix"/>,
        /// such as a dropdown's arrow, stays after the shortened text.
        /// </summary>
        internal static GUIContent FitContent(string label, string tooltip, Texture image, GUIStyle style, float width,
            string suffix = "")
        {
            var content = new GUIContent(label + suffix, image, tooltip);
            if (style.CalcSize(content).x <= width)
                return content;
            for (var length = label.Length - 1; length > 0; length--)
            {
                content.text = label.Substring(0, length).TrimEnd() + "…" + suffix;
                if (style.CalcSize(content).x <= width)
                    break;
            }
            content.tooltip = string.IsNullOrEmpty(tooltip) ? label : label + "\n\n" + tooltip;
            return content;
        }

        // The variant dropdown grows with its label up to this width.
        const float VariantDropdownMaxWidth = 170f;

        /// <summary>
        /// The details header's title line: the profile's name and, once variants exist,
        /// the variant dropdown right after it, since the details below show this profile
        /// with that variant, for any selected profile. In a narrow window the name gives
        /// way and ends in "…"; the dropdown keeps its width.
        /// </summary>
        void DrawTitleLine(Rect line, ForgeProfile profile)
        {
            cachedTitleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            var variants = ForgeSettings.instance.BuildVariants;
            if (variants.Count == 0)
            {
                GUI.Label(line, FitContent(profile.DisplayName, null, null, cachedTitleStyle, line.width), cachedTitleStyle);
                return;
            }

            cachedTitleSeparatorStyle ??= new GUIStyle(cachedTitleStyle)
            {
                fontStyle = FontStyle.Normal,
                normal = { textColor = EditorStyles.centeredGreyMiniLabel.normal.textColor }
            };
            const float Gap = 4f;
            var separator = new GUIContent("\u00b7");
            var separatorWidth = cachedTitleSeparatorStyle.CalcSize(separator).x;
            var variant = CurrentVariant;
            var canMutate = ForgeEditorState.CanMutate(out var mutateReason);
            var style = EditorStyles.popup;
            var label = variant ?? "Pick a variant";
            var dropdownWidth = Mathf.Min(Mathf.Min(style.CalcSize(new GUIContent(label)).x, VariantDropdownMaxWidth), line.width);
            var caption = FitContent(label,
                canMutate
                    ? "The variant these details and the CI Command show, and the one Apply, Build and Build and Run use " +
                      "(Project Settings > Build Forge > Build Variants)."
                    : mutateReason,
                null, style, dropdownWidth);

            var nameSpace = Mathf.Max(0f, line.width - dropdownWidth - separatorWidth - 2 * Gap);
            var name = FitContent(profile.DisplayName, null, null, cachedTitleStyle, nameSpace);
            var nameWidth = Mathf.Min(cachedTitleStyle.CalcSize(name).x, nameSpace);
            GUI.Label(new Rect(line.x, line.y, nameWidth, line.height), name, cachedTitleStyle);
            var x = line.x + nameWidth + Gap;
            GUI.Label(new Rect(x, line.y, separatorWidth, line.height), separator, cachedTitleSeparatorStyle);
            x += separatorWidth + Gap;
            var height = Mathf.Min(style.fixedHeight > 0f ? style.fixedHeight : EditorGUIUtility.singleLineHeight, line.height);
            DrawVariantDropdown(new Rect(x, line.y + (line.height - height) / 2f, dropdownWidth, height), caption, style,
                variant, variants, canMutate);
        }

        /// <summary>
        /// The variant dropdown on the details header's title line. Unity's pattern for a
        /// menu-opening control: EditorGUI.DropdownButton on a rect that is valid in every
        /// event, and the menu dropped at that rect.
        /// </summary>
        void DrawVariantDropdown(Rect rect, GUIContent caption, GUIStyle style, string variant,
            IReadOnlyList<string> variants, bool canMutate)
        {
            using (new EditorGUI.DisabledScope(!canMutate))
            {
                if (EditorGUI.DropdownButton(rect, caption, FocusType.Passive, style))
                {
                    var menu = new GenericMenu();
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

        void StartBuild(ForgeProfile profile, string variant, bool runPlayer)
        {
            // Logged rather than thrown, like Activate and Apply: a throw out of a
            // delayCall also drops the other callbacks queued with it.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    ForgeBuildRunner.RunBuild(profile, variant, out _, runPlayer);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Build Forge] Building '{profile.DisplayName}' failed: {e.Message}\n{e.StackTrace}");
                }
            };
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
                    // The reverts that are part of Apply can fail like Revert to Baseline.
                    OfferToStopTracking(e);
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
                    $"These settings could not be restored:\n\n{lines}\n\n" +
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
