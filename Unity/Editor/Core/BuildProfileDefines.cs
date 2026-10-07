using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Compilation;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Maintains one scripting define per Unity Build Profile
    /// (<c>BUILD_PROFILE_&lt;NAME&gt;</c>) in the profile's own Scripting Defines
    /// list. Unity applies that list whenever the profile is active, in the
    /// editor and in the build, so under the 1:1 rule the define identifies the
    /// Build Forge configuration at compile time with no apply/revert machinery.
    /// Build Forge writes it whenever it touches a referenced Build Profile and
    /// replaces a stale one after a rename; <see cref="ForgeSettings.MaintainBuildProfileDefines"/>
    /// turns the writes off. The pure members are testable on plain arrays.
    /// </summary>
    internal static class BuildProfileDefines
    {
        public const string Prefix = "BUILD_PROFILE_";

        /// <summary>
        /// The define for a Build Profile name: prefix + upper-cased name, runs of
        /// non-alphanumerics collapsed to one underscore. Letters outside ASCII are
        /// kept: C# identifiers allow them, and a Build Profile with such a define
        /// was verified to save, activate and satisfy an #if on 6000.3.23 and
        /// 6000.6.0 (Unity writes it YAML-escaped, e.g. "BUILD_PROFILE_\xDCBUNG").
        /// The name is NFC-normalized first so a decomposed accent does not split
        /// a letter into letter + underscore.
        /// </summary>
        public static string DefineFor(string profileName) => MakeDefine(Prefix, profileName);

        /// <summary>The sanitizer shared with build variants: prefix + sanitized name.</summary>
        public static string MakeDefine(string prefix, string name)
        {
            var sb = new StringBuilder(prefix);
            var pendingUnderscore = false;
            foreach (var c in (name ?? "").Normalize(NormalizationForm.FormC))
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (pendingUnderscore && sb.Length > prefix.Length)
                        sb.Append('_');
                    pendingUnderscore = false;
                    sb.Append(char.ToUpperInvariant(c));
                }
                else
                {
                    pendingUnderscore = true;
                }
            }
            return sb.ToString();
        }

        public static bool IsBuildForgeDefine(string define)
            => define != null && define.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>
        /// The list with <paramref name="wanted"/> present and every other
        /// Build Forge define removed; user defines and their order are kept.
        /// </summary>
        public static string[] Compute(IReadOnlyList<string> existing, string wanted, out bool changed)
        {
            var result = new List<string>();
            var found = false;
            foreach (var define in existing ?? Array.Empty<string>())
            {
                if (define == wanted)
                {
                    if (!found) result.Add(define);
                    found = true;
                }
                else if (!IsBuildForgeDefine(define))
                {
                    result.Add(define);
                }
            }
            if (!found)
                result.Add(wanted);

            changed = existing == null || !result.SequenceEqual(existing);
            return result.ToArray();
        }

        /// <summary>
        /// Warning for a profile whose Build Profile name sanitizes to the same
        /// define as another referenced Build Profile ("Quest 3" and "Quest-3"),
        /// so an <c>#if</c> on it would be true for both; null when unique.
        /// Sharing the same Build Profile is the 1:1 error, not a collision.
        /// </summary>
        public static string CollisionWarning(ForgeProfile profile, IReadOnlyList<ForgeProfile> all)
        {
            if (profile == null || profile.BuildProfile == null)
                return null;
            var define = DefineFor(profile.BuildProfile.name);
            var others = all
                .Where(p => p != null && p != profile && p.BuildProfile != null
                            && p.BuildProfile != profile.BuildProfile
                            && DefineFor(p.BuildProfile.name) == define)
                .Select(p => $"'{p.BuildProfile.name}'")
                .Distinct()
                .ToList();
            if (others.Count == 0)
                return null;
            return $"Scripting define '{define}' is also the define of Unity Build Profile {string.Join(", ", others)}, " +
                   $"so '#if {define}' is true for both. Rename one so the names still differ after sanitizing " +
                   "(letters and digits are kept, everything else becomes an underscore).";
        }

        public static bool NeedsUpdate(BuildProfile buildProfile)
        {
            if (buildProfile == null)
                return false;
            Compute(buildProfile.scriptingDefines, DefineFor(buildProfile.name), out var changed);
            return changed;
        }

        /// <summary>
        /// Writes the define into the Build Profile when maintenance is on and
        /// the list is stale. Returns true when the asset was changed. A change
        /// to the active profile requests a script compilation so the editor
        /// domain sees the new define; the build pipeline reads the list itself.
        /// </summary>
        public static bool Ensure(BuildProfile buildProfile)
        {
            if (buildProfile == null || !ForgeSettings.instance.MaintainBuildProfileDefines)
                return false;

            var defines = Compute(buildProfile.scriptingDefines, DefineFor(buildProfile.name), out var changed);
            if (!changed)
                return false;

            buildProfile.scriptingDefines = defines;
            EditorUtility.SetDirty(buildProfile);
            AssetDatabase.SaveAssetIfDirty(buildProfile);
            Debug.Log($"[Build Forge] Scripting define '{DefineFor(buildProfile.name)}' written to Unity Build Profile '{buildProfile.name}'.");

            RequestScriptCompilation(buildProfile);
            return true;
        }

        // Unity's own request, which also records the defines as compiled
        // (internal; see RequestScriptCompilation), and whether this editor's
        // BuildProfile.scriptingDefines setter already makes it.
        static readonly System.Reflection.MethodInfo unityRequestScriptCompilation =
            typeof(BuildProfile).Assembly.GetType("UnityEditor.Build.Profile.BuildProfileModuleUtil")
                ?.GetMethod("RequestScriptCompilation", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(BuildProfile) }, null);
        internal static readonly bool SetterRecordsDefines = typeof(BuildProfile).GetMethod("SetAndApplyScriptingDefines",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) != null;
        internal static bool CanRecordDefines => unityRequestScriptCompilation != null;

        /// <summary>
        /// Recompiles the editor after Build Forge changed the scripting defines of
        /// the active Build Profile. On early 6000.3 patches the scriptingDefines
        /// setter does not update Unity's record of the defines it last compiled,
        /// and Unity's Build Profiles window, when it shows the profile, then asks
        /// whether to apply or revert them; Revert writes the old ones back and
        /// undoes Build Forge's change. Unity's own request updates that record,
        /// and later setters make it themselves (checked: 6000.3.0f1 does not,
        /// 6000.3.23f1 and 6000.6.0f1 do). It is internal, so it is looked up by
        /// reflection and left out when missing.
        /// </summary>
        internal static void RequestScriptCompilation(BuildProfile buildProfile)
        {
            if (buildProfile == null || BuildProfile.GetActiveBuildProfile() != buildProfile)
                return;
            if (!SetterRecordsDefines && unityRequestScriptCompilation != null)
                unityRequestScriptCompilation.Invoke(null, new object[] { buildProfile });
            CompilationPipeline.RequestScriptCompilation();
        }

        static readonly HashSet<BuildProfile> pending = new();

        /// <summary>Ensure() out of the current GUI pass; one call per profile per frame.</summary>
        public static void EnsureDeferred(BuildProfile buildProfile)
        {
            if (buildProfile == null || !NeedsUpdate(buildProfile) || !pending.Add(buildProfile))
                return;
            EditorApplication.delayCall += () =>
            {
                pending.Remove(buildProfile);
                Ensure(buildProfile);
            };
        }
    }
}
