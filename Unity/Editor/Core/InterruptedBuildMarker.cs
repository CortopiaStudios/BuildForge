using System;
using System.IO;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// A file a build writes before it changes anything and removes once it has put
    /// everything back. Left behind, it means a build did not finish: the editor quit or
    /// was stopped during it, or a step that puts settings back failed, so project files
    /// may still hold what that build changed. Builds stop until it is cleared, so the next
    /// one doesn't start from, and keep, what the last one left. Per user, in UserSettings,
    /// like the editor-state ledger; deleting Library doesn't clear it.
    /// </summary>
    internal static class InterruptedBuildMarker
    {
        internal const string FilePath = "UserSettings/ForgeBuildInProgress.json";

        [Serializable]
        internal sealed class Record
        {
            public string profile;
            public string variant;
            public string started;
        }

        // Begun in this editor and not yet finished: the command line begins it before its own
        // version overrides, so the build it runs neither stops on it nor removes it.
        static bool ownedHere;
        // A build inside such a span failed to put something back; its owner keeps the file.
        static bool keptByBuild;

        /// <summary>The marker on disk, or null; an unreadable one reads as a record without details.</summary>
        internal static Record Read()
        {
            if (!File.Exists(FilePath))
                return null;
            try
            {
                return JsonUtility.FromJson<Record>(File.ReadAllText(FilePath)) ?? new Record();
            }
            catch (Exception)
            {
                return new Record();
            }
        }

        /// <summary>Why builds stop, or null.</summary>
        internal static string Error()
        {
            if (ownedHere)
                return null;
            var record = Read();
            return record != null ? Message(record) : null;
        }

        internal static string Message(Record record)
        {
            var which = string.IsNullOrEmpty(record.profile) ? "" : $" of '{record.profile}'" +
                        (string.IsNullOrEmpty(record.variant) ? "" : $" (variant {record.variant})");
            var when = string.IsNullOrEmpty(record.started) ? "" : $", started {record.started},";
            return $"A Build Forge build{which}{when} did not finish: the editor quit or was stopped during it, or a step " +
                   "that puts settings back failed. Project files, such as ProjectSettings/ProjectSettings.asset, the XR " +
                   "settings under Assets/XR/, Build Profiles and the Addressables settings, may still hold what it " +
                   "changed, and a build now would start from them and keep them. Check git status and git diff, restore " +
                   "the files that build changed and keep your own edits, then clear this with Clear in the Build Forge " +
                   $"window, or by deleting {FilePath}. Builds stop until then.";
        }

        /// <returns>Whether this call began it, for <see cref="Finish"/>.</returns>
        internal static bool Begin(string profile, string variant)
        {
            if (ownedHere)
                return false;
            keptByBuild = false;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(new Record
            {
                profile = profile,
                variant = variant ?? "",
                started = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            }));
            ownedHere = true;
            return true;
        }

        /// <summary>
        /// The end of a build, or of the span that <paramref name="began"/> it. When every step
        /// put things back (<paramref name="restored"/>), and no build inside the span failed to,
        /// the file goes; otherwise it stays and stops the next build. A build that didn't begin
        /// it, inside the command line's span, only reports to it. Doesn't throw: a file that
        /// can't be deleted is logged and stays, so it stops the next build too.
        /// </summary>
        internal static void Finish(bool began, bool restored)
        {
            if (!began)
            {
                keptByBuild |= !restored;
                return;
            }
            ownedHere = false;
            var remove = restored && !keptByBuild;
            keptByBuild = false;
            if (!remove || !File.Exists(FilePath))
                return;
            try
            {
                File.Delete(FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Build Forge] The build put everything back, but {FilePath} could not be deleted: {e.Message} " +
                               "Builds stop until it is gone: delete it, or clear it with Clear in the Build Forge window.");
            }
        }

        /// <summary>The user's, after restoring what the build changed.</summary>
        internal static void Clear()
        {
            ownedHere = false;
            keptByBuild = false;
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
    }
}
