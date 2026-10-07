using System;
using System.Linq;
using System.Reflection;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildForge.Editor.UI
{
    /// <summary>
    /// A "Build Forge" section in Unity's Build Profile editor, after Unity's
    /// own settings sections, both in the Build Profiles window and when a Build
    /// Profile asset is shown in the Inspector, so its Build Forge settings are
    /// visible and editable where the profile is. Unity offers no extension
    /// point for packages outside the Unity registry
    /// (<c>[BuildProfileSettingsProvider]</c> is gated, see ARCHITECTURE), so the
    /// section is inserted into the editor's UI Toolkit tree before its "Add
    /// Settings" button (<c>bp-add-settings-button</c>). The profile comes from
    /// the Build Profiles window's <c>buildProfileEditor</c> field, or from the
    /// <c>editor</c> of the Inspector's <c>InspectorElement</c>. All three names
    /// are internal (verified on 6000.3.0f1 and 6000.6.3f1); when one is
    /// missing that place gets no section.
    /// </summary>
    [InitializeOnLoad]
    static class BuildProfileEditorSection
    {
        const string SectionName = "build-forge-section";
        const string AddSettingsButton = "bp-add-settings-button";
        const string ExpandedKey = "BuildForge.BuildProfileEditorSection.Expanded";

        static readonly Type ProfilesWindowType = Type.GetType("UnityEditor.Build.Profile.BuildProfileWindow, UnityEditor.BuildProfileModule");
        static readonly FieldInfo ProfilesWindowEditor = ProfilesWindowType?.GetField("buildProfileEditor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static readonly Type InspectorWindowType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.InspectorWindow");
        static readonly PropertyInfo InspectorElementEditor = typeof(InspectorElement).GetProperty("editor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        static double nextCheck;

        static BuildProfileEditorSection()
        {
            EditorApplication.update += Update;
        }

        static void Update()
        {
            // Both rebuild their content when the selection changes, which drops
            // the section; a few checks a second put it back.
            if (EditorApplication.timeSinceStartup < nextCheck)
                return;
            nextCheck = EditorApplication.timeSinceStartup + 0.25;

            if (ProfilesWindowType != null && ProfilesWindowEditor != null)
                foreach (EditorWindow window in Resources.FindObjectsOfTypeAll(ProfilesWindowType))
                    EnsureIn(window.rootVisualElement, _ => ProfileOf(ProfilesWindowEditor.GetValue(window) as UnityEditor.Editor));

            if (InspectorWindowType != null && InspectorElementEditor != null)
                foreach (EditorWindow window in Resources.FindObjectsOfTypeAll(InspectorWindowType))
                    EnsureIn(window.rootVisualElement, addButton =>
                        ProfileOf(InspectorElementEditor.GetValue(addButton.GetFirstAncestorOfType<InspectorElement>()) as UnityEditor.Editor));
        }

        static BuildProfile ProfileOf(UnityEditor.Editor editor) =>
            editor != null && editor.targets.Length == 1 ? editor.target as BuildProfile : null;

        static void EnsureIn(VisualElement root, Func<VisualElement, BuildProfile> resolve)
        {
            foreach (var addButton in root.Query<Button>(AddSettingsButton).ToList())
            {
                var container = addButton.parent;
                if (container == null)
                    continue;
                var section = container.Children().OfType<Foldout>().FirstOrDefault(f => f.name == SectionName);
                if (section == null)
                {
                    var button = addButton;
                    section = CreateSection(() => resolve(button));
                    container.Insert(container.IndexOf(addButton), section);
                }
                // Platform profiles (in Library/) cannot hold the settings, and the
                // scene list page has no profile: no section there.
                section.style.display = ForgeProfileEmbedding.CanHold(resolve(addButton)) ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        static Foldout CreateSection(Func<BuildProfile> selectedProfile)
        {
            var foldout = new Foldout { name = SectionName, text = "Build Forge", value = SessionState.GetBool(ExpandedKey, true) };
            // Unity's own section foldouts carry these classes.
            foldout.AddToClassList("rhs-foldout");
            foldout.AddToClassList("mb-medium");
            foldout.RegisterValueChangedCallback(e =>
            {
                if (e.target == foldout)
                    SessionState.SetBool(ExpandedKey, e.newValue);
            });

            ForgeProfileEditor editor = null;
            var content = new IMGUIContainer(() =>
            {
                var buildProfile = selectedProfile();
                if (!ForgeProfileEmbedding.CanHold(buildProfile))
                    return;
                var profile = ForgeProfileEmbedding.Get(buildProfile);
                if (profile == null)
                {
                    if (editor != null) { UnityEngine.Object.DestroyImmediate(editor); editor = null; }
                    EditorGUILayout.HelpBox("This Build Profile has no Build Forge settings.", MessageType.None);
                    if (GUILayout.Button("Add Build Forge Settings", GUILayout.ExpandWidth(false)))
                        EditorApplication.delayCall += () => ForgeProfileEmbedding.Add(buildProfile);
                    return;
                }
                if (editor == null || editor.target != profile)
                {
                    if (editor != null) UnityEngine.Object.DestroyImmediate(editor);
                    editor = (ForgeProfileEditor)UnityEditor.Editor.CreateEditor(profile, typeof(ForgeProfileEditor));
                    editor.HostedInBuildProfileEditor = true;
                }
                editor.OnInspectorGUI();

                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Remove Build Forge Settings", "Delete the Build Forge settings stored in this Build Profile."),
                        GUILayout.ExpandWidth(false)))
                    EditorApplication.delayCall += () => ForgeProfileMenus.ConfirmRemove(new[] { buildProfile });
                EditorGUILayout.EndHorizontal();
            });
            content.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (editor != null) { UnityEngine.Object.DestroyImmediate(editor); editor = null; }
            });
            foldout.Add(content);
            return foldout;
        }
    }
}
