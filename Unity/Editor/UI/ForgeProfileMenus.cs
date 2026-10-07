using System.Linq;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.UI
{
    /// <summary>
    /// Menu entry points for the Build Forge settings stored in Unity Build
    /// Profiles: the Assets menu (also the Project window's context menu), which
    /// works on several selected Build Profiles at once, and the Build Profile
    /// inspector's context menu. The settings themselves are edited in the
    /// Build Profile editor's "Build Forge" section (<see cref="BuildProfileEditorSection"/>);
    /// Edit selects the stored ForgeProfile, which the Inspector shows with
    /// <see cref="ForgeProfileEditor"/>.
    /// </summary>
    static class ForgeProfileMenus
    {
        const string Root = "Assets/Build Forge/";
        const string Context = "CONTEXT/BuildProfile/";

        static BuildProfile[] SelectedBuildProfiles() =>
            Selection.objects.OfType<BuildProfile>().Where(ForgeProfileEmbedding.CanHold).ToArray();

        static void Add(BuildProfile buildProfile) =>
            // Out of the GUI or menu pass: adding saves the Build Profile asset.
            EditorApplication.delayCall += () => Selection.activeObject = ForgeProfileEmbedding.Add(buildProfile);

        [MenuItem(Root + "Add Build Forge Settings", priority = 1000)]
        static void AddToSelection()
        {
            foreach (var buildProfile in SelectedBuildProfiles().Where(bp => ForgeProfileEmbedding.Get(bp) == null))
                Add(buildProfile);
        }

        [MenuItem(Root + "Add Build Forge Settings", true)]
        static bool CanAddToSelection() => SelectedBuildProfiles().Any(bp => ForgeProfileEmbedding.Get(bp) == null);

        [MenuItem(Root + "Edit Build Forge Settings", priority = 1001)]
        static void EditSelection() =>
            Selection.activeObject = SelectedBuildProfiles().Select(ForgeProfileEmbedding.Get).First(p => p != null);

        [MenuItem(Root + "Edit Build Forge Settings", true)]
        static bool CanEditSelection() => SelectedBuildProfiles().Any(bp => ForgeProfileEmbedding.Get(bp) != null);

        [MenuItem(Root + "Remove Build Forge Settings", priority = 1002)]
        static void RemoveFromSelection() =>
            ConfirmRemove(SelectedBuildProfiles().Where(bp => ForgeProfileEmbedding.Get(bp) != null).ToArray());

        [MenuItem(Root + "Remove Build Forge Settings", true)]
        static bool CanRemoveFromSelection() => CanEditSelection();

        [MenuItem(Context + "Add Build Forge Settings")]
        static void AddFromContext(MenuCommand command) => Add((BuildProfile)command.context);

        [MenuItem(Context + "Add Build Forge Settings", true)]
        static bool CanAddFromContext(MenuCommand command) =>
            command.context is BuildProfile bp && ForgeProfileEmbedding.CanHold(bp) && ForgeProfileEmbedding.Get(bp) == null;

        [MenuItem(Context + "Edit Build Forge Settings")]
        static void EditFromContext(MenuCommand command) =>
            Selection.activeObject = ForgeProfileEmbedding.Get((BuildProfile)command.context);

        [MenuItem(Context + "Edit Build Forge Settings", true)]
        static bool CanEditFromContext(MenuCommand command) =>
            command.context is BuildProfile bp && ForgeProfileEmbedding.Get(bp) != null;

        /// <summary>Asks, then removes the Build Forge settings stored in <paramref name="targets"/>.</summary>
        internal static void ConfirmRemove(BuildProfile[] targets)
        {
            var names = string.Join(", ", targets.Select(t => $"'{t.name}'"));
            if (!EditorUtility.DisplayDialog("Remove Build Forge Settings",
                    $"Remove the Build Forge settings stored in {names}? Their plugin settings, output path and variant " +
                    "restrictions are deleted with them; version control can bring them back.", "Remove", "Cancel"))
                return;
            foreach (var buildProfile in targets)
                ForgeProfileEmbedding.Remove(buildProfile);
        }
    }
}
