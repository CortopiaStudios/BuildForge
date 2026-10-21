using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BuildForge.Editor.Configuration
{
    /// <summary>
    /// Per-user editor state: which Unity Build Profile's Build Forge configuration
    /// is applied to the editor and the baseline/expected snapshots for every
    /// setting group it wrote. Keyed on the Unity Build Profile (1:1 with Build
    /// Forge profiles) so "applied" is a property of Unity's profile and the
    /// ledger survives a future embedding of Build Forge data into it.
    /// Lives in UserSettings/ (in Unity's default .gitignore, survives Library/
    /// deletion) because it is the analog of Unity's own active-build-profile
    /// state, which is also local — a teammate's editor is on a different
    /// profile, so committing this would describe the wrong machine. No
    /// auto-save: the editor-state coordinator performs several mutations per
    /// operation and controls when the file is written (see ForgeEditorState).
    /// </summary>
    [FilePath("UserSettings/ForgeEditorState.asset", FilePathAttribute.Location.ProjectFolder)]
    internal class ForgeEditorStateStore : ScriptableSingleton<ForgeEditorStateStore>
    {
        [Tooltip("Asset GUID of the Unity Build Profile whose Build Forge configuration is applied to the editor, or empty.")]
        [SerializeField] string appliedBuildProfileGuid = "";

        [Tooltip("The build variant applied with the profile (its BUILD_VARIANT_ define is on the Unity Build Profile while applied), or empty.")]
        [SerializeField] string appliedVariant = "";

        [Tooltip("Baseline and expected snapshots for every plugin/target group written by the applied profile.")]
        [SerializeField] List<EditorStateEntry> editorState = new();

        internal string AppliedBuildProfileGuid
        {
            get => appliedBuildProfileGuid ?? "";
            set => appliedBuildProfileGuid = value ?? "";
        }

        internal string AppliedVariant
        {
            get => appliedVariant ?? "";
            set => appliedVariant = value ?? "";
        }

        internal List<EditorStateEntry> EditorState => editorState;

        /// <summary>Persists the state to disk. Public wrapper around the protected Save method.</summary>
        internal void SaveState()
        {
            Save(true);
        }
    }

    /// <summary>
    /// One plugin/target-group pair written by the applied profile: the state
    /// before Build Forge touched it (baseline) and the state right after the
    /// profile was applied (expected). Entries exist only while a profile is
    /// applied, so baselines are never stale.
    /// </summary>
    [System.Serializable]
    internal class EditorStateEntry
    {
        public string key;
        public string baselineJson;
        public string expectedJson;
        public EditorStatePhase phase;
    }

    /// <summary>
    /// How far the operation that last wrote an entry got. Only an
    /// <see cref="Applied"/> entry holds the state its profile left, so only there
    /// can a three-way merge tell later changes from the Apply's; the others are
    /// undone by restoring their baseline whole. Applied is 0, so ledgers written
    /// before the phase existed read as Applied.
    /// </summary>
    internal enum EditorStatePhase
    {
        /// <summary>The Apply finished and its result was captured as the expected state.</summary>
        Applied = 0,
        /// <summary>
        /// Recorded with the baseline before the plugin writes. Stays so when the
        /// Apply was stopped or the editor quit before the result was captured.
        /// </summary>
        Applying = 1,
        /// <summary>
        /// Restored to its baseline for a build. Stays so when the editor quit
        /// during the build, or the profile could not be applied again after it.
        /// </summary>
        Suspended = 2,
    }

    /// <summary>
    /// Pure key composition and list access for editor-state entries, split out
    /// of the ScriptableSingleton shell so it can be unit-tested on plain lists.
    /// </summary>
    internal static class EditorStateLedger
    {
        const char Separator = '|';

        public static string MakeKey(string pluginTypeName, BuildTargetGroup group)
            => pluginTypeName + Separator + group;

        public static bool TryParseKey(string key, out string pluginTypeName, out BuildTargetGroup group)
        {
            pluginTypeName = null;
            group = BuildTargetGroup.Unknown;
            if (string.IsNullOrEmpty(key))
                return false;

            var idx = key.LastIndexOf(Separator);
            if (idx <= 0 || idx == key.Length - 1)
                return false;

            if (!System.Enum.TryParse(key.Substring(idx + 1), out group))
                return false;

            pluginTypeName = key.Substring(0, idx);
            return true;
        }

        public static EditorStateEntry Find(List<EditorStateEntry> entries, string key)
            => entries.Find(e => e.key == key);

        public static void Set(List<EditorStateEntry> entries, string key, string baselineJson, string expectedJson,
            EditorStatePhase phase = EditorStatePhase.Applied)
        {
            var entry = Find(entries, key);
            if (entry == null)
            {
                entries.Add(new EditorStateEntry { key = key, baselineJson = baselineJson, expectedJson = expectedJson, phase = phase });
                return;
            }
            entry.baselineJson = baselineJson;
            entry.expectedJson = expectedJson;
            entry.phase = phase;
        }

        public static bool Remove(List<EditorStateEntry> entries, string key)
        {
            var entry = Find(entries, key);
            if (entry == null)
                return false;
            entries.Remove(entry);
            return true;
        }
    }
}
