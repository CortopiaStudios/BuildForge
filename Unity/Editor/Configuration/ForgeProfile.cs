using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.Serialization;

namespace BuildForge.Editor.Configuration
{
    /// <summary>
    /// Build Forge extension data for a Unity Build Profile.
    /// Stores plugin configurations and build output settings.
    /// Player Settings are managed by Build Profiles themselves — Build Forge provides
    /// a diff view to see what a Build Profile overrides from the platform defaults.
    /// Stored as a component of its Unity Build Profile (a sub-asset of the
    /// profile's .asset, see <see cref="Core.ForgeProfileEmbedding"/>).
    /// </summary>
    // The light theme icon; Unity loads d_ForgeProfile.png beside it in the dark theme.
    [Icon("Packages/com.cortopiastudios.buildforge/Editor/Icons/ForgeProfile.png")]
    public class ForgeProfile : ScriptableObject
    {
        [Tooltip("The Unity Build Profile these Build Forge settings extend, for settings not stored in a Build Profile (made in code, as tests do).")]
        [SerializeField] BuildProfile buildProfile;

        // The Build Profile whose asset contains this profile, when embedded.
        // Not serialized: the containing asset is the source of truth, so a
        // duplicated Build Profile's copy belongs to the duplicate.
        [NonSerialized] BuildProfile owner;

        [Tooltip("Output path for the build, relative to the project root. " +
                 "Supports {ProfileName} (the Unity Build Profile's name), {Target}, {ProjectName} and {Variant} " +
                 "(the variant name, Default for the default build once variants exist, empty and collapsed when none are configured) placeholders. " +
                 "Leave it empty to follow the default template. With Remove Previous Build Output on, the files the previous build " +
                 "of this profile and variant left there are removed before each build; use a dedicated directory per profile and variant.")]
        // Stored only when customized; the getter supplies the default, so a
        // profile that never set a path follows the package default (and its
        // YAML says so with an empty value).
        [SerializeField] string outputPath;

        [Tooltip("Before each build, remove the artifact file and the files the previous build of this profile and variant " +
                 "produced in its destination. Files that were already there are kept, and so are files changed since that build; " +
                 "files added to the destination while a build runs count as its output, so use a dedicated folder. " +
                 "Turn it off only when previous output must be retained; an old artifact can then remain after a failed build.")]
        // Named pruneBuildDestination before 1.0; the old name still loads.
        [FormerlySerializedAs("pruneBuildDestination")]
        [SerializeField] bool removePreviousBuildOutput = true;

        [Tooltip("Limit named variants on this profile. The default build is always available.")]
        [SerializeField] bool restrictVariants;
        [SerializeField] List<string> allowedVariants = new();
        [Tooltip("Optional explanation shown when a variant is unavailable on this profile.")]
        [SerializeField] string variantRestrictionReason;

        public string VariantRestrictionReason => variantRestrictionReason;
        internal bool RestrictVariants => restrictVariants;
        internal IReadOnlyList<string> AllowedVariants => allowedVariants;

        public bool SupportsVariant(string variant) => string.IsNullOrEmpty(variant)
            || string.Equals(variant, "Default", StringComparison.OrdinalIgnoreCase)
            || !restrictVariants || (allowedVariants?.Contains(variant) ?? false);

        [Tooltip("Per-plugin configuration objects keyed by plugin key, stored as managed references so they read as plain YAML.")]
        [SerializeField] List<PluginConfigSlot> pluginConfigs = new();

        /// <summary>
        /// The Unity Build Profile this profile extends: the one that contains
        /// it, or for a profile made in code (tests) the one it references.
        /// </summary>
        public BuildProfile BuildProfile
        {
            get
            {
                var containing = Owner;
                return containing != null ? containing : buildProfile;
            }
        }

        /// <summary>True when this profile is stored inside its Unity Build Profile's asset.</summary>
        public bool IsEmbedded => Owner != null;

        BuildProfile Owner
        {
            get
            {
                // Kept once found; a reimport that destroys the Build Profile
                // object reads as null and resolves again. Not
                // AssetDatabase.IsSubAsset: it is false for HideInHierarchy
                // objects, which Build Profile components are (verified on 6000.3.0).
                if (owner == null && EditorUtility.IsPersistent(this) && !AssetDatabase.IsMainAsset(this))
                    owner = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(this)) as BuildProfile;
                return owner;
            }
        }

        /// <summary>
        /// The name to use in messages about this profile itself: a stored
        /// profile has no asset name of its own, so it is its Build Profile's name;
        /// a profile made in code is its object name. For everything user-facing
        /// use <see cref="DisplayName"/>.
        /// </summary>
        public string ProfileName => IsEmbedded ? DisplayName : name;

        /// <summary>
        /// The name this profile is known by: its Unity Build Profile's name
        /// (the identity under the 1:1 rule, and what the scripting define and
        /// the manifest use), falling back to the asset name while none is
        /// assigned.
        /// </summary>
        public string DisplayName
        {
            get
            {
                var profile = BuildProfile;
                return profile != null ? profile.name : name;
            }
        }

        /// <summary>The template used when a profile stores no output path.</summary>
        public const string DefaultOutputPath = "Builds/{Target}/{ProfileName}/{Variant}/{ProjectName}";

        /// <summary>The effective output path template: the stored one, or <see cref="DefaultOutputPath"/>.</summary>
        public string OutputPath => string.IsNullOrEmpty(outputPath) ? DefaultOutputPath : outputPath;

        /// <summary>
        /// Whether to remove the previous build's output from the destination
        /// before editor and CI build preparation. On by default.
        /// </summary>
        public bool RemovePreviousBuildOutput => removePreviousBuildOutput;

        /// <summary>True when the profile stores its own template rather than following the default.</summary>
        public bool IsOutputPathCustomized => !string.IsNullOrEmpty(outputPath);

        /// <summary>
        /// What to store for text typed into the output path field: nothing when it
        /// is empty or equals the default (so the profile keeps following the
        /// default), otherwise the text as typed.
        /// </summary>
        public static string NormalizeOutputPathInput(string text)
        {
            var trimmed = text?.Trim() ?? "";
            return trimmed.Length == 0 || trimmed == DefaultOutputPath ? "" : text;
        }

        /// <summary>
        /// Resolves the output path template. Only meaningful when this profile's
        /// Unity Build Profile is ACTIVE: {ProjectName} reads Application.productName
        /// and the Android extension reads EditorUserBuildSettings.buildAppBundle,
        /// both of which track the active profile. RunBuild enforces the
        /// active-profile requirement; UI code must not display resolved paths for
        /// non-active profiles.
        /// </summary>
        public string GetResolvedOutputPath(BuildTarget target, string variant = null)
            => GetResolvedOutputPath(target, variant, ForgeSettings.instance.BuildVariants.Count > 0,
                Core.BuildVariants.MarkedProductName(PlayerSettings.productName, variant, Core.BuildVariants.ShouldMark(variant)));

        /// <summary>
        /// {Variant} is the variant name, "Default" for the default build once any
        /// variant is configured (siblings in the output tree, not parent and
        /// child), and empty (collapsed) when variants are not in use.
        /// </summary>
        internal string GetResolvedOutputPath(BuildTarget target, string variant, bool variantsConfigured)
            => GetResolvedOutputPath(target, variant, variantsConfigured, PlayerSettings.productName);

        internal string GetResolvedOutputPath(BuildTarget target, string variant, bool variantsConfigured, string rawProductName)
        {
            var productName = ForgeSettings.instance.MangleProductName
                ? ForgeSettings.MangleForFilename(rawProductName)
                : rawProductName;

            var path = OutputPath
                .Replace("{ProjectName}", productName)
                .Replace("{ProfileName}", DisplayName)
                .Replace("{Target}", target.ToString())
                .Replace("{Variant}", Core.BuildVariants.EffectiveName(variant, variantsConfigured) ?? "");
            path = CollapseEmptySegments(path);

            var extension = GetRequiredExtension(target);
            if (extension != null && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                path += extension;

            return path;
        }

        /// <summary>"Builds/Quest//Game" (an empty {Variant}) becomes "Builds/Quest/Game".</summary>
        internal static string CollapseEmptySegments(string path)
        {
            while (path.Contains("//")) path = path.Replace("//", "/");
            while (path.Contains("\\\\")) path = path.Replace("\\\\", "\\");
            return path.TrimEnd('/', '\\');
        }

        string GetRequiredExtension(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android:
                    // Verified (Unity 6000.3): this getter tracks the ACTIVE Build
                    // Profile — it follows a profile switch immediately, follows live edits
                    // of the active profile's checkbox, ignores edits to non-active
                    // profiles, and the build (gradle bundle*/assemble*) obeys the
                    // same value. Resolution only runs for the active profile (see
                    // GetResolvedOutputPath), so the getter is exact here — and
                    // consistent with the PlayerSettings-statics pattern used
                    // throughout the codebase.
                    return EditorUserBuildSettings.buildAppBundle ? ".aab" : ".apk";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return ".exe";
                case BuildTarget.StandaloneOSX:
                    return ".app";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Returns the stored configuration for <paramref name="key"/> when one
        /// exists and is of type <typeparamref name="T"/>; otherwise a fresh
        /// <c>new T()</c> that is <b>not</b> stored. Reading never dirties the asset.
        /// </summary>
        public T GetPluginConfig<T>(string key) where T : class, new()
            => PluginConfigSlot.Get<T>(pluginConfigs, key);

        /// <summary>
        /// Stores (or replaces) the configuration for <paramref name="key"/> and
        /// marks the asset dirty; null removes it. The instance is owned by this
        /// profile: after mutating it, call this again so the asset is dirtied,
        /// never share one instance between profiles, and re-read it with
        /// <see cref="GetPluginConfig{T}"/> rather than caching it. <typeparamref name="T"/>
        /// must be a <c>[Serializable]</c> class (not a UnityEngine.Object).
        /// </summary>
        public void SetPluginConfig<T>(string key, T config) where T : class
        {
            PluginConfigSlot.Set(pluginConfigs, key, config);
            EditorUtility.SetDirty(this);
        }
    }

    /// <summary>
    /// One plugin's configuration object, held as a managed reference so Unity
    /// serializes it as ordinary YAML fields (readable, line-diffable) instead of
    /// an opaque string. The pure Get/Set helpers are shared with ForgeSettings
    /// and testable on plain lists.
    /// </summary>
    [Serializable]
    internal class PluginConfigSlot
    {
        public string key;
        [SerializeReference] public object config;

        public static T Get<T>(List<PluginConfigSlot> slots, string key) where T : class, new()
        {
            var slot = slots.Find(s => s.key == key);
            return slot?.config as T ?? new T();
        }

        public static void Set(List<PluginConfigSlot> slots, string key, object config)
        {
            if (config != null)
            {
                // A non-serializable or UnityEngine.Object instance would be
                // written as a null reference and silently vanish on reload.
                var type = config.GetType();
                if (!type.IsSerializable || typeof(UnityEngine.Object).IsAssignableFrom(type))
                    throw new ArgumentException(
                        $"{type.FullName} must be a [Serializable] class that does not derive from UnityEngine.Object.");
            }

            var slot = slots.Find(s => s.key == key);
            if (config == null)
            {
                if (slot != null)
                    slots.Remove(slot);
                return;
            }

            if (slot != null)
                slot.config = config;
            else
                slots.Add(new PluginConfigSlot { key = key, config = config });
        }
    }
}
