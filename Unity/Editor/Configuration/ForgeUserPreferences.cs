using UnityEditor;
using UnityEngine;

namespace BuildForge.Editor.Configuration
{
    /// <summary>
    /// Per-user editor preferences for this project that do not affect builds,
    /// kept in UserSettings/ beside Build Forge's other per-user state (EditorPrefs
    /// would share them across every project on the machine). Today: whether the
    /// Build Profile and apply dropdowns show in Unity's main toolbar. Saved
    /// only when a value changes, so a project nobody changed has no file.
    /// </summary>
    [FilePath("UserSettings/ForgeUserPreferences.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ForgeUserPreferences : ScriptableSingleton<ForgeUserPreferences>
    {
        [Tooltip("Show Build Forge's Build Profile and apply dropdowns in Unity's main toolbar. Unity's toolbar menu (right-click > Hide, or the ⋮ menu) changes it.")]
        [SerializeField] bool showMainToolbar = true;

        internal bool ShowMainToolbar
        {
            get => showMainToolbar;
            set
            {
                if (showMainToolbar == value)
                    return;
                showMainToolbar = value;
                Save(true);
            }
        }
    }
}
