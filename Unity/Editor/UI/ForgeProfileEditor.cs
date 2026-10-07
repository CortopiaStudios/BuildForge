using System.Collections.Generic;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.UI
{
    /// <summary>
    /// Custom inspector for ForgeProfile ScriptableObjects.
    /// </summary>
    [CustomEditor(typeof(ForgeProfile))]
    internal class ForgeProfileEditor : UnityEditor.Editor
    {
        SerializedProperty buildProfileProp;
        SerializedProperty outputPathProp;
        SerializedProperty removePreviousBuildOutputProp;

        /// <summary>
        /// Set when drawn inside Unity's Build Profile editor (the Build Profiles
        /// window, or a Build Profile asset in the Inspector), which already
        /// shows the Build Profile and its Player Settings: the Build Profile
        /// field and the Player Overrides diff are left out there.
        /// </summary>
        internal bool HostedInBuildProfileEditor { get; set; }

        readonly PlayerSettingsDiffView diffView = new();
        BuildProfile diffViewProfile;

        // Persisted via SessionState — survives domain reloads but not editor restarts
        const string ExpandedKeyPrefix = "BuildForge.PluginExpanded.";
        const string OverridesExpandedKey = "BuildForge.OverridesExpanded";

        static bool IsPluginExpanded(string pluginId)
        {
            return SessionState.GetBool(ExpandedKeyPrefix + pluginId, false);
        }

        static void SetPluginExpanded(string pluginId, bool expanded)
        {
            SessionState.SetBool(ExpandedKeyPrefix + pluginId, expanded);
        }

        void OnEnable()
        {
            buildProfileProp = serializedObject.FindProperty("buildProfile");
            outputPathProp = serializedObject.FindProperty("outputPath");
            removePreviousBuildOutputProp = serializedObject.FindProperty("removePreviousBuildOutput");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawCoreSettings();
            EditorGUILayout.Space(8);
            DrawPlugins();
            EditorGUILayout.Space(8);
            if (!HostedInBuildProfileEditor)
            {
                DrawPlayerOverrides();
                EditorGUILayout.Space(12);
            }
            DrawActions();

            serializedObject.ApplyModifiedProperties();

            // Unity keeps Inspector edits in memory until Save Project, a reload,
            // or quit, so the .asset file would lag behind what the Inspector
            // shows. Profiles are meant to be read and diffed, so flush the
            // change now (deferred out of the GUI pass; one write per frame at
            // most). Project Settings > Build Forge > Profiles turns this off.
            if (ForgeSettings.instance.SaveProfilesOnEdit && EditorUtility.IsDirty(target))
                ScheduleSave((ForgeProfile)target);

            // Keep the referenced Build Profile's BUILD_PROFILE_<NAME> define current.
            BuildProfileDefines.EnsureDeferred(((ForgeProfile)target).BuildProfile);
        }

        static readonly HashSet<ForgeProfile> pendingSaves = new();

        static void ScheduleSave(ForgeProfile profile)
        {
            if (!pendingSaves.Add(profile))
                return;
            EditorApplication.delayCall += () =>
            {
                pendingSaves.Remove(profile);
                if (profile != null)
                    AssetDatabase.SaveAssetIfDirty(profile);
            };
        }

        /// <summary>
        /// A profile belongs to the Build Profile that contains it, so the field
        /// is read-only with a way back to it. A profile that is not stored in a
        /// Build Profile (only made in code, as tests do) keeps the plain field.
        /// </summary>
        void DrawBuildProfileField(ForgeProfile profile)
        {
            if (!profile.IsEmbedded)
            {
                EditorGUILayout.PropertyField(buildProfileProp, new GUIContent("Unity Build Profile", buildProfileProp.tooltip));
                return;
            }
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(new GUIContent("Unity Build Profile", "The Build Profile these settings are stored in."),
                    profile.BuildProfile, typeof(BuildProfile), false);
            if (GUILayout.Button(new GUIContent("Select", "Select the Unity Build Profile."), EditorStyles.miniButton, GUILayout.Width(56f)))
                Selection.activeObject = profile.BuildProfile;
            EditorGUILayout.EndHorizontal();
        }

        void DrawCoreSettings()
        {
            EditorGUILayout.LabelField("Core Settings", EditorStyles.boldLabel);
            var restricted = serializedObject.FindProperty("restrictVariants");
            EditorGUILayout.PropertyField(restricted);
            if (restricted.boolValue)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("allowedVariants"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("variantRestrictionReason"));
            }
            if (!HostedInBuildProfileEditor)
                DrawBuildProfileField((ForgeProfile)target);
            var variantError = BuildVariants.ConfigurationError((ForgeProfile)target);
            if (variantError != null)
                EditorGUILayout.HelpBox(variantError, MessageType.Error);
            var allProfiles = ForgeProfileLookup.FindAll();
            var sharedError = ForgeProfileLookup.SharedBuildProfileError((ForgeProfile)target, allProfiles);
            if (sharedError != null)
                EditorGUILayout.HelpBox(sharedError, MessageType.Error);
            if (ForgeSettings.instance.MaintainBuildProfileDefines)
            {
                var collision = BuildProfileDefines.CollisionWarning((ForgeProfile)target, allProfiles);
                if (collision != null)
                    EditorGUILayout.HelpBox(collision, MessageType.Warning);
            }
            var stray = BuildVariants.StrayWarning((ForgeProfile)target, ForgeEditorState.IsApplied((ForgeProfile)target));
            if (stray != null)
                EditorGUILayout.HelpBox(stray, MessageType.Warning);
            // Empty stores nothing and means "follow the default template", so the
            // field always shows the effective template and stores only a
            // customized one (typing the default back clears it).
            var stored = outputPathProp.stringValue;
            var customized = !string.IsNullOrEmpty(stored);
            EditorGUILayout.BeginHorizontal();
            var shown = customized ? stored : ForgeProfile.DefaultOutputPath;
            var typed = EditorGUILayout.TextField(
                new GUIContent("Output Path", customized ? "Custom template. Clear it to follow the default." : "The default template; edit to customize."),
                shown);
            if (typed != shown)
                outputPathProp.stringValue = ForgeProfile.NormalizeOutputPathInput(typed);
            if (customized)
            {
                if (GUILayout.Button(new GUIContent("Reset", "Follow the default template again."), EditorStyles.miniButton, GUILayout.Width(48f)))
                    outputPathProp.stringValue = "";
            }
            else
            {
                GUILayout.Label("default", EditorStyles.miniLabel, GUILayout.Width(48f));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "Output path supports {ProjectName}, {ProfileName} (the Unity Build Profile's name), {Target} and {Variant} " +
                "(the variant name; Default for the default build once variants exist, collapsed when none are configured) placeholders. " +
                "Leave it at the default to follow future changes of the package default. " +
                "Use a dedicated directory per profile and variant.",
                MessageType.None);
            EditorGUILayout.PropertyField(removePreviousBuildOutputProp,
                new GUIContent("Remove Previous Build Output", removePreviousBuildOutputProp.tooltip));
            if (!removePreviousBuildOutputProp.boolValue)
                EditorGUILayout.HelpBox(
                    "Build Forge will leave the previous build's files in place. An old artifact can remain after a failed build. " +
                    "Publish only after a successful build.", MessageType.Warning);
        }

        void DrawPlugins()
        {
            var forgeProfile = (ForgeProfile)target;
            var plugins = ForgePluginRegistry.GetPlugins();

            if (plugins.Count == 0)
                return;

            var buildProfile = forgeProfile.BuildProfile;

            foreach (var plugin in plugins)
            {
                var pluginId = plugin.GetType().FullName;
                if (ForgeSettings.instance.IsPluginDisabledWithDefault(
                    pluginId, ForgePluginRegistry.IsSafeToDefaultEnable(plugin)))
                    continue;

                var applicable = buildProfile != null && plugin.IsApplicable(buildProfile);
                var enabled = plugin.IsEnabled(forgeProfile);

                // "(not applicable)" wins over "(disabled)" — it's the stronger
                // statement (the plugin couldn't run even if enabled).
                string label;
                if (!applicable)
                    label = $"{plugin.DisplayName} (not applicable)";
                else if (enabled == false)
                    label = $"{plugin.DisplayName} (disabled)";
                else
                    label = plugin.DisplayName;

                bool expanded = IsPluginExpanded(pluginId);
                var newExpanded = EditorGUILayout.Foldout(expanded,
                    new GUIContent(label, plugin.Description), true,
                    EditorStyles.foldoutHeader);

                if (newExpanded != expanded)
                    SetPluginExpanded(pluginId, newExpanded);

                if (newExpanded)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    if (!applicable)
                    {
                        var reason = buildProfile == null
                            ? "No Unity Build Profile is assigned."
                            : plugin.NotApplicableReason(buildProfile)
                                ?? "This plugin is not applicable to this profile.";
                        EditorGUILayout.HelpBox(
                            $"{reason} Plugin settings are preserved but cannot be edited here.",
                            MessageType.None);
                    }
                    else if (enabled != null)
                    {
                        var newEnabled = EditorGUILayout.Toggle(
                            new GUIContent("Enabled", "Run this plugin when building this profile."),
                            enabled.Value);
                        if (newEnabled != enabled.Value)
                        {
                            plugin.SetEnabled(forgeProfile, newEnabled);
                            enabled = newEnabled;
                        }
                    }
                    using (new EditorGUI.DisabledScope(!applicable || enabled == false))
                        plugin.OnDrawProfileGUI(forgeProfile);
                    EditorGUILayout.EndVertical();
                }
            }
        }

        void DrawPlayerOverrides()
        {
            var forgeProfile = (ForgeProfile)target;
            var buildProfile = forgeProfile.BuildProfile;
            if (buildProfile == null)
                return;

            // Keep the shared diff view pointed at the current Build Profile.
            if (diffViewProfile != buildProfile)
            {
                diffViewProfile = buildProfile;
                diffView.SetProfile(buildProfile);
            }

            var expanded = SessionState.GetBool(OverridesExpandedKey, false);
            var label = diffView.DiffCount > 0
                ? $"Player Overrides ({diffView.DiffCount})"
                : "Player Overrides";

            var newExpanded = EditorGUILayout.Foldout(expanded,
                new GUIContent(label, PlayerSettingsDiffView.Description), true,
                EditorStyles.foldoutHeader);
            if (newExpanded != expanded)
                SessionState.SetBool(OverridesExpandedKey, newExpanded);

            if (newExpanded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                diffView.Draw(useScrollView: false);
                EditorGUILayout.EndVertical();
            }
        }

        void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Open Build Window"))
            {
                var window = EditorWindow.GetWindow<ForgeBuildWindow>();
                window.titleContent = new GUIContent("Build Forge");
                window.Show();
                window.Focus();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
