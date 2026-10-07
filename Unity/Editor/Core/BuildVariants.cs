using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// A single project-wide variant selects its BUILD_VARIANT define and optional
    /// rules for development mode, aliases and product marking. Builds resolve
    /// these before plugin hooks and restore temporary settings in finally.
    /// Editor Apply persists the selected variant's defines for iteration, never
    /// its development flags; the per-user ledger records only what it changed,
    /// so Revert, variant switches and builds undo that change and keep edits
    /// made to the profile since.
    /// </summary>
    internal static class BuildVariants
    {
        public const string Prefix = "BUILD_VARIANT_";

        /// <summary>How the UI shows the default build (no variant); angle brackets are rejected in variant names so it cannot collide.</summary>
        public const string DefaultLabel = "<default>";

        /// <summary>Reserved name (case-insensitive): on the command line it means the default build; it cannot be a variant.</summary>
        public const string DefaultName = "default";

        /// <summary>
        /// The define the default build gets once any variant exists, so code can
        /// address it directly rather than by the absence of every other variant.
        /// With no variants configured no BUILD_VARIANT_ define is set at all.
        /// </summary>
        public const string DefaultDefine = Prefix + "DEFAULT";

        /// <summary>The default build's name where a variant name is needed (output path, manifest) once variants exist.</summary>
        public const string DefaultVariantName = "Default";

        /// <summary>
        /// The variant name a build is known by: the variant itself, "Default"
        /// for the default build once any variant exists (so default and variant
        /// builds are siblings, not parent and child, in the output tree), or
        /// null when variants are not in use.
        /// </summary>
        /// <summary>
        /// The build window's variant selection after a (re)load. The window
        /// remembers the selection for the editor session only; in a fresh editor
        /// nothing is stored and the selection follows what is applied, so the
        /// window never shows a pending variant switch the user did not ask for.
        /// <paramref name="stored"/> is empty when nothing was remembered,
        /// <see cref="DefaultLabel"/> for a remembered default build, else the name.
        /// </summary>
        public static string RestoreSelection(string stored, string appliedVariant)
        {
            if (string.IsNullOrEmpty(stored))
                return string.IsNullOrEmpty(appliedVariant) ? null : appliedVariant;
            return stored == DefaultLabel ? null : stored;
        }

        /// <summary>The value the window stores for a selection; the inverse of <see cref="RestoreSelection"/>.</summary>
        public static string StoreSelection(string variant)
            => variant ?? DefaultLabel;

        internal static BuildVariantRule RuleFor(string variant)
            => ForgeSettings.instance.VariantRules.FirstOrDefault(r => r != null &&
                string.Equals(r.Variant, variant ?? DefaultVariantName, StringComparison.Ordinal));

        internal static bool ShouldMark(string variant)
        {
            var rule = RuleFor(variant);
            return rule?.MarkProductName switch
            {
                VariantOverride.Enabled => true,
                VariantOverride.Disabled => false,
                _ => ForgeSettings.instance.MarkVariantBuilds
            };
        }

        internal static string ConfigurationError(ForgeProfile profile = null)
            => ListWarning(Configured)
                ?? RulesError(ForgeSettings.instance.VariantRules, Configured)
                ?? (profile != null && profile.RestrictVariants
                    ? AllowedVariantsError(profile.AllowedVariants, Configured) : null);

        internal static string ProfileError(ForgeProfile profile, string variant)
        {
            var error = ConfigurationError(profile);
            if (error != null) return error;
            Resolve(variant, Configured, out error);
            if (error != null) return error;
            if (profile != null && !profile.SupportsVariant(variant))
                return $"Variant '{variant}' is not enabled for '{profile.DisplayName}'. " +
                    (string.IsNullOrWhiteSpace(profile.VariantRestrictionReason)
                        ? "Configure Allowed Variants in its Build Forge settings." : profile.VariantRestrictionReason);
            return profile?.BuildProfile != null
                ? VariantBuildSettings.ValidationError(profile.BuildProfile, RuleFor(variant)) : null;
        }

        // Validate all rules, not only the selected one: every rule owns its aliases
        // and can remove them while a different variant is being applied.
        internal static string RulesError(IReadOnlyList<BuildVariantRule> rules, IReadOnlyList<string> variants)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in rules ?? Array.Empty<BuildVariantRule>())
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Variant))
                    return "A Variant Rule has an empty name. Choose a configured variant or Default.";
                if (rule.Variant != DefaultVariantName && !(variants?.Contains(rule.Variant) ?? false))
                    return $"Variant Rule '{rule.Variant}' does not match a configured variant. Update the rule after renaming or removing a variant, or use Default.";
                if (!names.Add(rule.Variant))
                    return $"Variant Rule '{rule.Variant}' is listed more than once. Keep one rule per variant.";
                if (!Enum.IsDefined(typeof(VariantOverride), rule.DevelopmentBuild)
                    || !Enum.IsDefined(typeof(VariantOverride), rule.MarkProductName)
                    || BuildConfigurationValues(rule.BuildConfiguration).Any(v => !Enum.IsDefined(v.GetType(), v)))
                    return $"Variant Rule '{rule.Variant}' contains an invalid override value.";
                var configuration = rule.BuildConfiguration;
                var developmentOptions = DevelopmentOptions(configuration)
                    .Where(o => o.value == VariantOverride.Enabled).Select(o => o.label).ToList();
                if (rule.DevelopmentBuild == VariantOverride.Disabled && developmentOptions.Count > 0)
                    return $"Variant Rule '{rule.Variant}' turns Development Build off but {string.Join(", ", developmentOptions)} on, " +
                           "which Unity uses only in development builds. Set Development Build to Enabled or Inherit, or turn the options off.";
                if (configuration.ScriptDebugging == VariantOverride.Disabled && configuration.WaitForManagedDebugger == VariantOverride.Enabled)
                    return $"Variant Rule '{rule.Variant}' turns Script Debugging off but Wait For Managed Debugger on, which needs Script Debugging.";
                var defines = new HashSet<string>(StringComparer.Ordinal);
                foreach (var define in rule.ScriptingDefines ?? new List<string>())
                {
                    if (string.IsNullOrEmpty(define) || define == "true" || define == "false"
                        || !Regex.IsMatch(define, @"\A[_\p{L}\p{Nl}][_\p{L}\p{Nl}\p{Nd}\p{Mn}\p{Mc}\p{Pc}]*\z"))
                        return $"Variant Rule '{rule.Variant}' has an invalid scripting define '{define}'. Enter one C# preprocessor identifier per element, without spaces or separators.";
                    if (IsVariantDefine(define) || define.StartsWith(BuildProfileDefines.Prefix, StringComparison.Ordinal))
                        return $"Variant Rule '{rule.Variant}' cannot own '{define}': BUILD_VARIANT_ and BUILD_PROFILE_ defines are generated by Build Forge.";
                    if (!defines.Add(define))
                        return $"Variant Rule '{rule.Variant}' lists scripting define '{define}' more than once.";
                }
                var unknown = UnknownVersionPlaceholder(rule.Version);
                if (unknown != null)
                    return $"Variant Rule '{rule.Variant}' has an unknown placeholder {unknown} in its Version. " +
                           "Use {Version}, {BuildNumber} or {Variant}.";
            }
            return null;
        }

        static IEnumerable<Enum> BuildConfigurationValues(VariantBuildConfiguration c) => new Enum[] {
            c.AutoconnectProfiler, c.DeepProfilingSupport, c.ScriptDebugging, c.WaitForManagedDebugger,
            c.CppCompilerConfiguration, c.Il2CppCodeGeneration, c.Il2CppStacktraceInformation,
            c.ManagedStrippingLevel, c.StripEngineCode, c.Compression, c.LinkTimeOptimization, c.DebugSymbols
        };

        static (string label, VariantOverride value)[] DevelopmentOptions(VariantBuildConfiguration c) => new[] {
            ("Autoconnect Profiler", c.AutoconnectProfiler), ("Deep Profiling Support", c.DeepProfilingSupport),
            ("Script Debugging", c.ScriptDebugging), ("Wait For Managed Debugger", c.WaitForManagedDebugger)
        };

        /// <summary>
        /// The rule's Build Configuration settings that builds of <paramref name="buildProfile"/>
        /// get, as "Label: Value" in the inspector's order: the ones not left to Inherit,
        /// without Build Profile settings the profile's platform doesn't have (Link Time
        /// Optimization and Debug Symbols outside Android). Empty without a rule.
        /// </summary>
        internal static List<string> DescribeBuildConfiguration(BuildVariantRule rule, BuildProfile buildProfile)
        {
            var shown = new List<string>();
            if (rule == null) return shown;
            var c = rule.BuildConfiguration;
            void Add(string label, Enum value, string profileField = null)
            {
                if (Convert.ToInt32(value) == 0 || (profileField != null && !VariantBuildSettings.HasSetting(buildProfile, profileField)))
                    return;
                var member = value.GetType().GetField(value.ToString());
                var name = member?.GetCustomAttributes(typeof(InspectorNameAttribute), false)
                    .OfType<InspectorNameAttribute>().FirstOrDefault()?.displayName ?? value.ToString();
                shown.Add($"{label}: {name}");
            }
            Add("Autoconnect Profiler", c.AutoconnectProfiler, "m_ConnectProfiler");
            Add("Deep Profiling Support", c.DeepProfilingSupport, "m_BuildWithDeepProfilingSupport");
            Add("Script Debugging", c.ScriptDebugging, "m_AllowDebugging");
            Add("Wait For Managed Debugger", c.WaitForManagedDebugger, "m_WaitForManagedDebugger");
            Add("C++ Compiler Configuration", c.CppCompilerConfiguration);
            Add("IL2CPP Code Generation", c.Il2CppCodeGeneration);
            Add("IL2CPP Stacktrace Information", c.Il2CppStacktraceInformation);
            Add("Managed Stripping Level", c.ManagedStrippingLevel);
            Add("Strip Engine Code", c.StripEngineCode);
            Add("Compression Method", c.Compression, "m_CompressionType");
            Add("Link Time Optimization", c.LinkTimeOptimization, "m_LinkTimeOptimization");
            Add("Debug Symbols", c.DebugSymbols, "m_DebugSymbolLevel");
            return shown;
        }

        static readonly string[] VersionPlaceholders = { "{Version}", "{BuildNumber}", "{Variant}" };

        /// <summary>The first placeholder in a Version template that Build Forge doesn't fill, or null.</summary>
        internal static string UnknownVersionPlaceholder(string template)
        {
            if (string.IsNullOrEmpty(template))
                return null;
            foreach (Match match in Regex.Matches(template, @"\{[^{}]*\}"))
                if (Array.IndexOf(VersionPlaceholders, match.Value) < 0)
                    return match.Value;
            return null;
        }

        /// <summary>
        /// The version a rule's Version template gives. Null, with <paramref name="error"/>
        /// set, when the template needs a build number the build doesn't have or
        /// gives an empty version.
        /// </summary>
        internal static string FormatVersion(string template, string version, string buildNumber, string variantName,
            out string error)
        {
            error = null;
            if (template.Contains("{BuildNumber}") && string.IsNullOrEmpty(buildNumber))
            {
                error = $"The Version '{template}' uses {{BuildNumber}}, but this build has no build number. " +
                        "Enable Build Number for the profile.";
                return null;
            }
            var formatted = template.Replace("{Version}", version ?? "")
                .Replace("{BuildNumber}", buildNumber ?? "")
                .Replace("{Variant}", variantName ?? "");
            if (string.IsNullOrWhiteSpace(formatted))
            {
                error = $"The Version '{template}' gives an empty version.";
                return null;
            }
            return formatted;
        }

        internal static string AllowedVariantsError(IReadOnlyList<string> allowed, IReadOnlyList<string> variants)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in allowed ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name))
                    return "Allowed Variants contains an empty name. Remove it or enter a configured variant.";
                if (IsDefaultName(name))
                    return "Default is always allowed. Remove it from Allowed Variants.";
                if (!(variants?.Contains(name) ?? false))
                    return $"Allowed Variants contains unknown variant '{name}'. Update the profile after renaming or removing a variant.";
                if (!names.Add(name))
                    return $"Allowed Variants lists '{name}' more than once.";
            }
            return null;
        }

        internal static string[] WithRuleDefines(IReadOnlyList<string> existing, string variant,
            IReadOnlyList<BuildVariantRule> rules, bool variantsConfigured)
        {
            var owned = new HashSet<string>((rules ?? Array.Empty<BuildVariantRule>())
                .Where(r => r != null).SelectMany(r => r.ScriptingDefines ?? new List<string>()));
            var filtered = (existing ?? Array.Empty<string>()).Where(d => !owned.Contains(d)).ToArray();
            var result = WithVariantDefine(filtered, variant, variantsConfigured, out _).ToList();
            var rule = rules?.FirstOrDefault(r => r != null && r.Variant == (variant ?? DefaultVariantName));
            if (rule?.ScriptingDefines != null)
                result.AddRange(rule.ScriptingDefines.Where(d => !string.IsNullOrWhiteSpace(d) && !result.Contains(d)));
            return result.ToArray();
        }

        public static string EffectiveName(string variant, bool variantsConfigured)
        {
            if (!string.IsNullOrEmpty(variant))
                return variant;
            return variantsConfigured ? DefaultVariantName : null;
        }

        /// <summary>
        /// The product name a build gets: "Game (Internal)" for a variant build
        /// when marking is on, so an internal build is recognizable on the device,
        /// in the window title and in the file name ({ProjectName} follows the
        /// product name). The default build is never marked: it is the build you
        /// ship and must look like one from a project without variants.
        /// </summary>
        public static string MarkedProductName(string productName, string variant, bool markVariantBuilds)
        {
            if (!markVariantBuilds || string.IsNullOrEmpty(variant) || variant == DefaultVariantName)
                return productName;
            return $"{productName} ({variant})";
        }

        /// <summary>The define a build with <paramref name="variant"/> carries, or null when variants are not in use.</summary>
        public static string ExpectedDefine(string variant, bool variantsConfigured)
        {
            if (!string.IsNullOrEmpty(variant))
                return DefineFor(variant);
            return variantsConfigured ? DefaultDefine : null;
        }

        /// <summary>The define for a variant name, sanitized like the profile define.</summary>
        public static string DefineFor(string variant) => BuildProfileDefines.MakeDefine(Prefix, variant);

        public static bool IsVariantDefine(string define)
            => define != null && define.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>
        /// The BUILD_VARIANT_ defines on a Unity Build Profile this machine has
        /// not applied. Apply writes one and every undo removes it, so on a
        /// profile that is not applied here one means the profile was saved while
        /// a variant was applied somewhere else: committed while applied, or saved
        /// by a build that did not finish. Empty when the profile is applied here.
        /// </summary>
        internal static string[] StrayDefines(IReadOnlyList<string> defines, bool appliedHere)
            => appliedHere
                ? Array.Empty<string>()
                : (defines ?? Array.Empty<string>()).Where(IsVariantDefine).ToArray();

        /// <summary>
        /// The defines without variant state this machine has no record of: every
        /// BUILD_VARIANT_ define and, when there was one, the aliases the variant
        /// rules own, which every build removes as well. A list without a variant
        /// define is the user's own and comes back unchanged.
        /// </summary>
        internal static string[] WithoutStrayVariantState(IReadOnlyList<string> defines, IReadOnlyList<BuildVariantRule> rules)
        {
            var list = defines ?? Array.Empty<string>();
            if (!list.Any(IsVariantDefine))
                return list.ToArray();
            var owned = new HashSet<string>((rules ?? Array.Empty<BuildVariantRule>())
                .Where(r => r != null).SelectMany(r => r.ScriptingDefines ?? new List<string>()));
            return list.Where(d => !IsVariantDefine(d) && !owned.Contains(d)).ToArray();
        }

        /// <summary>
        /// The warning for a Build Forge profile whose Unity Build Profile carries
        /// variant state this machine did not write, or null. Its defines compile
        /// into the editor here; builds recompute them. A Development Build flag
        /// that came with it (Apply wrote the rule's flags until it stopped, pre-1.0)
        /// would reach a Default build, so a flag the variant's rule sets is named too.
        /// </summary>
        internal static string StrayWarning(ForgeProfile profile, bool appliedHere)
        {
            var buildProfile = profile != null ? profile.BuildProfile : null;
            if (buildProfile == null) return null;
            var defines = buildProfile.scriptingDefines ?? Array.Empty<string>();
            var stray = StrayDefines(defines, appliedHere);
            if (stray.Length == 0) return null;

            var names = stray.Select(VariantNameFor).ToList();
            var rules = names.Where(n => n != null).Select(RuleFor).Where(r => r != null).ToList();
            var owned = new HashSet<string>(rules.SelectMany(r => r.ScriptingDefines ?? new List<string>()));
            var compiled = defines.Where(d => IsVariantDefine(d) || owned.Contains(d)).Distinct();
            var variants = string.Join(" and ", names.Select((n, i) =>
                n == null ? $"a variant that no longer exists ({stray[i]})"
                : n == DefaultVariantName ? "the default variant" : $"variant '{n}'"));
            var text = $"Unity Build Profile '{buildProfile.name}' carries {string.Join(", ", stray)}, but no variant is applied to it " +
                       $"on this machine: it was saved while {variants} was applied, most likely on another machine and committed. " +
                       $"Its defines {string.Join(", ", compiled)} compile into the editor here; builds recompute them.";
            var defaultRule = RuleFor(null);
            if ((defaultRule == null || defaultRule.DevelopmentBuild == VariantOverride.Inherit)
                && rules.Any(r => r.DevelopmentBuild != VariantOverride.Inherit) && VariantBuildSettings.IsDevelopment(buildProfile))
                text += " Development Build is on and the variant's rule sets it: unless that is the profile's own setting, a Default build is a development build.";
            return text + " Revert to Baseline where it was applied and commit the Build Profile, or restore it from version control; " +
                   "Activate removes the defines here.";
        }

        /// <summary>The configured variant a define belongs to ("Default" for BUILD_VARIANT_DEFAULT), or null.</summary>
        static string VariantNameFor(string define)
            => define == DefaultDefine ? DefaultVariantName : Configured.FirstOrDefault(v => DefineFor(v) == define);

        /// <summary>
        /// The variant a build should use: null for none (a null or empty name),
        /// the exact configured name, or null with <paramref name="error"/> set
        /// when the name is not in the list. Never a silent pick.
        /// </summary>
        public static string Resolve(string name, IReadOnlyList<string> variants, out string error)
        {
            error = ListWarning(variants);
            if (error != null) return null;
            if (string.IsNullOrWhiteSpace(name) || IsDefaultName(name))
                return null;
            var match = variants?.FirstOrDefault(v => v == name.Trim());
            if (match != null)
                return match;
            var available = variants == null || variants.Count == 0
                ? "no variants are defined"
                : "defined variants: " + string.Join(", ", variants.Select(v => $"'{v}'"));
            error = $"Unknown build variant '{name.Trim()}' ({available}; Project Settings > Build Forge > Build Variants).";
            return null;
        }

        static bool IsDefaultName(string name)
        {
            var trimmed = name.Trim();
            return string.Equals(trimmed, DefaultName, StringComparison.OrdinalIgnoreCase) || trimmed == DefaultLabel;
        }

        /// <summary>
        /// Problems in the variant list: empty or reserved names, duplicates, and
        /// two names that sanitize to the same define. Null when the list is fine.
        /// </summary>
        public static string ListWarning(IReadOnlyList<string> variants)
        {
            if (variants == null || variants.Count == 0)
                return null;
            if (variants.Any(string.IsNullOrWhiteSpace))
                return "A variant has an empty name.";
            var bracketed = variants.FirstOrDefault(v => v.Contains("<") || v.Contains(">"));
            if (bracketed != null)
                return $"Variant '{bracketed}' must not contain '<' or '>' (reserved for the '{DefaultLabel}' entry).";
            var reserved = variants.FirstOrDefault(IsDefaultName);
            if (reserved != null)
                return $"'{reserved}' is reserved for the default build (it gets {DefaultDefine}); pick another name.";
            var padded = variants.FirstOrDefault(v => v != v.Trim());
            if (padded != null)
                return $"Variant '{padded}' has leading or trailing whitespace. Remove the whitespace from its name.";
            var noDefine = variants.FirstOrDefault(v => DefineFor(v) == Prefix);
            if (noDefine != null)
                return $"Variant '{noDefine}' has no letters or digits, so it would produce no usable define.";
            var duplicate = variants.GroupBy(v => v).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                return $"Variant '{duplicate.Key}' is listed more than once.";
            var collision = variants.GroupBy(DefineFor).FirstOrDefault(g => g.Count() > 1);
            if (collision != null)
                return $"Variants {string.Join(" and ", collision.Select(v => $"'{v}'"))} sanitize to the same define " +
                       $"'{collision.Key}'; rename one so they differ in letters or digits.";
            return null;
        }

        /// <summary>
        /// The defines list with every variant define removed and the one for
        /// <paramref name="variant"/> appended: the variant's own define, or
        /// BUILD_VARIANT_DEFAULT for the default build when
        /// <paramref name="variantsConfigured"/>, or nothing.
        /// </summary>
        public static string[] WithVariantDefine(IReadOnlyList<string> existing, string variant, bool variantsConfigured, out bool changed)
        {
            var result = (existing ?? Array.Empty<string>()).Where(d => !IsVariantDefine(d)).ToList();
            var expected = ExpectedDefine(variant, variantsConfigured);
            if (expected != null)
                result.Add(expected);
            changed = existing == null || !result.SequenceEqual(existing);
            return result.ToArray();
        }

        /// <summary>True when the profile carries exactly the define a build with <paramref name="variant"/> expects.</summary>
        public static bool HasExpectedDefine(BuildProfile buildProfile, string variant, bool variantsConfigured)
        {
            if (buildProfile == null)
                return false;
            var defines = buildProfile.scriptingDefines ?? Array.Empty<string>();
            var expected = WithRuleDefines(defines, variant, ForgeSettings.instance.VariantRules, variantsConfigured);
            return defines.SequenceEqual(expected);
        }

        /// <summary>
        /// Editor iteration: puts the define for <paramref name="variant"/> on the
        /// Build Profile (BUILD_VARIANT_DEFAULT for the default build while
        /// variants are configured) and the rule's aliases, and saves it,
        /// requesting a script compilation when the profile is active so the
        /// editor domain follows. The rule's development flags are left to builds.
        /// Returns true when the asset changed.
        /// </summary>
        public static bool ApplyOnProfile(BuildProfile buildProfile, string variant)
        {
            if (buildProfile == null) return false;
            var error = ConfigurationError();
            var resolved = Resolve(variant, Configured, out var resolveError);
            error ??= resolveError ?? VariantBuildSettings.ValidationError(buildProfile, RuleFor(resolved));
            if (error != null) throw new InvalidOperationException(error);
            variant = resolved;
            VariantDefineLedger.RequireSaved(buildProfile);

            // Start from the user's own values: the profile as it is now with the
            // previously applied change undone, so edits made in Unity since then
            // are kept. The ledger then records only what this Apply changes.
            var ledger = VariantDefineLedger.instance;
            var definesBefore = buildProfile.scriptingDefines ?? Array.Empty<string>();
            var userDefines = ledger.UserDefines(buildProfile);
            if (ledger.HasEntry(buildProfile))
            {
                // Save the undone profile before the previous record is replaced,
                // so a crash in between never leaves a change nobody recorded.
                ledger.UndoBuildSettings(buildProfile);
                buildProfile.scriptingDefines = userDefines;
                EditorUtility.SetDirty(buildProfile);
                AssetDatabase.SaveAssetIfDirty(buildProfile);
            }

            // Only the defines are written. The rule's development flags never
            // reach editor compilation (the editor gets no DEVELOPMENT_BUILD), and
            // on a committed profile they would reach every machine without this
            // ledger, where a Default build inherits them. Builds apply the rule.
            var variantsConfigured = Configured.Count > 0;
            var defines = WithRuleDefines(userDefines, variant, ForgeSettings.instance.VariantRules, variantsConfigured);
            ledger.Record(buildProfile, userDefines, null, defines, null);

            buildProfile.scriptingDefines = defines;
            EditorUtility.SetDirty(buildProfile);
            AssetDatabase.SaveAssetIfDirty(buildProfile);

            // Compare with the list before the undo: re-applying the same variant
            // changes nothing and must not trigger a recompile.
            if (defines.SequenceEqual(definesBefore))
                return false;
            var expected = ExpectedDefine(variant, variantsConfigured);
            Debug.Log(expected == null
                ? $"[Build Forge] Removed the variant define from Unity Build Profile '{buildProfile.name}'."
                : $"[Build Forge] Scripting define '{expected}' written to Unity Build Profile '{buildProfile.name}'.");
            BuildProfileDefines.RequestScriptCompilation(buildProfile);
            return true;
        }

        /// <summary>
        /// Editor iteration: strips every variant define (Activate, Revert to
        /// Baseline). Without a record of an Apply here, a variant define takes the
        /// rules' aliases with it (see <see cref="WithoutStrayVariantState"/>).
        /// </summary>
        public static bool StripFromProfile(BuildProfile buildProfile)
        {
            if (buildProfile == null) return false;
            var previous = buildProfile.scriptingDefines;
            if (!VariantDefineLedger.instance.TryRestore(buildProfile, out var restored))
                return WriteDefines(buildProfile, WithoutStrayVariantState(previous, ForgeSettings.instance.VariantRules), null);
            var changed = !(previous ?? Array.Empty<string>()).SequenceEqual(restored);
            if (changed)
                BuildProfileDefines.RequestScriptCompilation(buildProfile);
            return changed;
        }

        /// <summary>Saves <paramref name="defines"/> on the profile when they differ; <paramref name="expected"/> is the variant define, for the log line.</summary>
        static bool WriteDefines(BuildProfile buildProfile, string[] defines, string expected)
        {
            var previous = buildProfile.scriptingDefines ?? Array.Empty<string>();
            var changed = !defines.SequenceEqual(previous);
            if (!changed)
                return false;

            buildProfile.scriptingDefines = defines;
            EditorUtility.SetDirty(buildProfile);
            AssetDatabase.SaveAssetIfDirty(buildProfile);
            Debug.Log(expected == null
                ? $"[Build Forge] Removed {string.Join(", ", previous.Except(defines))} from Unity Build Profile '{buildProfile.name}'."
                : $"[Build Forge] Scripting define '{expected}' written to Unity Build Profile '{buildProfile.name}'.");

            BuildProfileDefines.RequestScriptCompilation(buildProfile);
            return true;
        }

        /// <summary>
        /// Build time: resolves defines before plugins and returns the previous
        /// list for the caller's finally block. Plugins may save assets, so the
        /// runner also persists restored settings after all cleanup hooks.
        /// </summary>
        public static string[] BeginBuild(BuildProfile buildProfile, string variant)
        {
            var previous = buildProfile.scriptingDefines;
            buildProfile.scriptingDefines = WithRuleDefines(VariantDefineLedger.instance.UserDefines(buildProfile), variant,
                ForgeSettings.instance.VariantRules, Configured.Count > 0);
            return previous;
        }

        public static void EndBuild(BuildProfile buildProfile, string[] previous)
        {
            if (buildProfile != null && previous != null)
                buildProfile.scriptingDefines = previous;
        }

        public static IReadOnlyList<string> Configured => ForgeSettings.instance.BuildVariants;
    }
}
