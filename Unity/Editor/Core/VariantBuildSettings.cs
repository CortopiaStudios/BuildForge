using System;
using System.Collections.Generic;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>Profile-explicit Unity build flags, verified against Unity 6000.3.23f1.</summary>
    internal static class VariantBuildSettings
    {
        internal static readonly string[] Fields = {
            "m_Development", "m_ConnectProfiler", "m_BuildWithDeepProfilingSupport",
            "m_AllowDebugging", "m_WaitForManagedDebugger"
        };

        /// <summary>
        /// The enum settings a rule's Build Configuration can set, verified against
        /// Unity 6000.3.0f1: every platform has the compression, only Android the
        /// other two. A rule's value is written by the enum member's name.
        /// </summary>
        internal static readonly string[] EnumFields = { "m_CompressionType", "m_LinkTimeOptimization", "m_DebugSymbolLevel" };

        const string Prefix = "m_PlatformBuildProfile.";
        [Serializable] sealed class Snapshot { public bool[] values; public bool[] present; public int[] enumValues; public bool[] enumPresent; }

        internal static string Capture(BuildProfile profile)
        {
            if (profile == null) return null;
            var so = new SerializedObject(profile);
            if (so.FindProperty(Prefix + Fields[0]) == null && Array.TrueForAll(EnumFields, f => so.FindProperty(Prefix + f) == null))
                return null;
            var snapshot = new Snapshot
            {
                values = new bool[Fields.Length], present = new bool[Fields.Length],
                enumValues = new int[EnumFields.Length], enumPresent = new bool[EnumFields.Length]
            };
            for (var i = 0; i < Fields.Length; i++)
            {
                var p = so.FindProperty(Prefix + Fields[i]);
                snapshot.present[i] = p != null;
                snapshot.values[i] = p?.boolValue ?? false;
            }
            for (var i = 0; i < EnumFields.Length; i++)
            {
                var p = so.FindProperty(Prefix + EnumFields[i]);
                snapshot.enumPresent[i] = p != null && IsEnum(p);
                snapshot.enumValues[i] = snapshot.enumPresent[i] ? p.intValue : 0;
            }
            return JsonUtility.ToJson(snapshot);
        }

        internal static void Restore(BuildProfile profile, string json)
        {
            if (profile == null || string.IsNullOrEmpty(json)) return;
            var snapshot = JsonUtility.FromJson<Snapshot>(json);
            var so = new SerializedObject(profile);
            for (var i = 0; i < Fields.Length; i++)
            {
                if (!snapshot.present[i]) continue;
                var p = so.FindProperty(Prefix + Fields[i]);
                if (p == null) throw new InvalidOperationException($"Cannot restore {Fields[i]} on '{profile.name}'.");
                p.boolValue = snapshot.values[i];
            }
            // Snapshots recorded before the enum settings existed have none.
            for (var i = 0; snapshot.enumPresent != null && snapshot.enumValues != null
                            && i < snapshot.enumPresent.Length && i < snapshot.enumValues.Length; i++)
            {
                if (!snapshot.enumPresent[i]) continue;
                var p = so.FindProperty(Prefix + EnumFields[i]);
                if (p == null) throw new InvalidOperationException($"Cannot restore {EnumFields[i]} on '{profile.name}'.");
                p.intValue = snapshot.enumValues[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The snapshot that undoes an Apply, for <see cref="Restore"/>: the
        /// fields Build Forge changed (their value before Apply differs from the
        /// value it wrote) that still hold the written value, each set back to
        /// its value before Apply. A field the user has changed since is left
        /// out, so the user's value stays. Null when there is nothing to undo.
        /// </summary>
        internal static string Undo(string current, string original, string written)
        {
            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(original) || string.IsNullOrEmpty(written))
                return null;
            var now = JsonUtility.FromJson<Snapshot>(current);
            var before = JsonUtility.FromJson<Snapshot>(original);
            var after = JsonUtility.FromJson<Snapshot>(written);
            var undo = new Snapshot { values = new bool[Fields.Length], present = new bool[Fields.Length] };
            var any = false;
            for (var i = 0; i < Fields.Length; i++)
            {
                if (!Has(now, i) || !Has(before, i) || !Has(after, i))
                    continue;
                if (before.values[i] == after.values[i] || now.values[i] != after.values[i])
                    continue;
                undo.present[i] = true;
                undo.values[i] = before.values[i];
                any = true;
            }
            return any ? JsonUtility.ToJson(undo) : null;
        }

        static bool Has(Snapshot snapshot, int i)
            => snapshot?.present != null && snapshot.values != null
               && i < snapshot.present.Length && i < snapshot.values.Length && snapshot.present[i];

        static bool IsEnum(SerializedProperty property)
            => property.propertyType == SerializedPropertyType.Enum;

        /// <summary>The Build Profile flags a rule's Build Configuration sets, in <see cref="Fields"/> spelling.</summary>
        static IEnumerable<(string field, VariantOverride value)> Flags(VariantBuildConfiguration configuration)
        {
            yield return ("m_ConnectProfiler", configuration.AutoconnectProfiler);
            yield return ("m_BuildWithDeepProfilingSupport", configuration.DeepProfilingSupport);
            yield return ("m_AllowDebugging", configuration.ScriptDebugging);
            yield return ("m_WaitForManagedDebugger", configuration.WaitForManagedDebugger);
        }

        /// <summary>The enum members a rule's Build Configuration sets, by <see cref="EnumFields"/> field; null for Inherit.</summary>
        static IEnumerable<(string field, string member)> EnumValues(VariantBuildConfiguration configuration)
        {
            yield return ("m_CompressionType", configuration.Compression switch
            {
                VariantCompression.Default => "None",
                VariantCompression.LZ4 => "Lz4",
                VariantCompression.LZ4HC => "Lz4HC",
                _ => null
            });
            yield return ("m_LinkTimeOptimization", configuration.LinkTimeOptimization switch
            {
                VariantLinkTimeOptimization.None => "None",
                VariantLinkTimeOptimization.Thin => "Thin",
                _ => null
            });
            yield return ("m_DebugSymbolLevel", configuration.DebugSymbols switch
            {
                VariantDebugSymbols.None => "None",
                VariantDebugSymbols.SymbolTable => "SymbolTable",
                VariantDebugSymbols.Full => "Full",
                _ => null
            });
        }

        /// <summary>Whether the rule changes anything on a Build Profile.</summary>
        static bool SetsProfileSettings(BuildVariantRule rule)
        {
            if (rule.DevelopmentBuild != VariantOverride.Inherit) return true;
            foreach (var (_, value) in Flags(rule.BuildConfiguration))
                if (value != VariantOverride.Inherit) return true;
            foreach (var (_, member) in EnumValues(rule.BuildConfiguration))
                if (member != null) return true;
            return false;
        }

        /// <summary>
        /// Whether the profile has the Build Profile setting <paramref name="field"/>
        /// (for example "m_LinkTimeOptimization", Android only); rules skip a setting
        /// the platform doesn't have.
        /// </summary>
        internal static bool HasSetting(BuildProfile profile, string field)
            => profile != null && new SerializedObject(profile).FindProperty(Prefix + field) != null;

        internal static string ValidationError(BuildProfile profile, BuildVariantRule rule)
        {
            if (rule == null || !SetsProfileSettings(rule)) return null;
            if (profile == null) return "No Unity Build Profile is assigned.";
            var so = new SerializedObject(profile);
            var dev = so.FindProperty(Prefix + Fields[0]);
            if (dev == null && rule.DevelopmentBuild != VariantOverride.Inherit)
                return $"'{profile.name}' has no native development build setting. Check its platform module and Unity version ({Application.unityVersion}).";
            foreach (var field in Fields)
            {
                var property = so.FindProperty(Prefix + field);
                if (property != null && property.propertyType != SerializedPropertyType.Boolean)
                    return $"'{profile.name}' has an unsupported native build setting '{field}' on Unity {Application.unityVersion}. No variant settings were applied.";
            }
            foreach (var (field, member) in EnumValues(rule.BuildConfiguration))
            {
                var property = member != null ? so.FindProperty(Prefix + field) : null;
                if (property != null && (!IsEnum(property) || Array.IndexOf(property.enumNames, member) < 0))
                    return $"'{profile.name}' has an unsupported native build setting '{field}' on Unity {Application.unityVersion}. No variant settings were applied.";
            }
            return null;
        }

        internal static void Apply(BuildProfile profile, BuildVariantRule rule)
        {
            if (rule == null || !SetsProfileSettings(rule)) return;
            var error = ValidationError(profile, rule);
            if (error != null) throw new InvalidOperationException(error);
            var so = new SerializedObject(profile);
            if (rule.DevelopmentBuild != VariantOverride.Inherit)
            {
                var dev = so.FindProperty(Prefix + Fields[0]);
                dev.boolValue = rule.DevelopmentBuild == VariantOverride.Enabled;
                if (!dev.boolValue)
                    for (var i = 1; i < Fields.Length; i++)
                    {
                        var p = so.FindProperty(Prefix + Fields[i]);
                        if (p != null) p.boolValue = false;
                    }
            }
            // The rules' validation rejects turning an option on while turning
            // Development Build off, so the clearing above stays in effect.
            foreach (var (field, value) in Flags(rule.BuildConfiguration))
            {
                var p = value != VariantOverride.Inherit ? so.FindProperty(Prefix + field) : null;
                if (p != null) p.boolValue = value == VariantOverride.Enabled;
            }
            foreach (var (field, member) in EnumValues(rule.BuildConfiguration))
            {
                var p = member != null ? so.FindProperty(Prefix + field) : null;
                if (p != null) p.enumValueIndex = Array.IndexOf(p.enumNames, member);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static bool IsDevelopment(BuildProfile profile)
            => new SerializedObject(profile).FindProperty(Prefix + Fields[0])?.boolValue
                ?? EditorUserBuildSettings.development;

        /// <summary>
        /// The development options the rule turns on that a build of this profile
        /// would not use, because it is not a development build (Unity uses them
        /// only in development builds) or, for Wait For Managed Debugger, has no
        /// Script Debugging. Empty when there are none.
        /// </summary>
        internal static List<string> UnusedDevelopmentOptions(BuildProfile profile, BuildVariantRule rule)
        {
            var unused = new List<string>();
            if (rule == null || profile == null) return unused;
            var so = new SerializedObject(profile);
            var development = rule.DevelopmentBuild switch
            {
                VariantOverride.Enabled => true,
                VariantOverride.Disabled => false,
                _ => so.FindProperty(Prefix + Fields[0])?.boolValue ?? false
            };
            var configuration = rule.BuildConfiguration;
            var scriptDebugging = configuration.ScriptDebugging switch
            {
                VariantOverride.Enabled => true,
                VariantOverride.Disabled => false,
                _ => so.FindProperty(Prefix + "m_AllowDebugging")?.boolValue ?? false
            };
            foreach (var (name, value) in new[] {
                         ("Autoconnect Profiler", configuration.AutoconnectProfiler),
                         ("Deep Profiling Support", configuration.DeepProfilingSupport),
                         ("Script Debugging", configuration.ScriptDebugging),
                         ("Wait For Managed Debugger", configuration.WaitForManagedDebugger) })
            {
                if (value != VariantOverride.Enabled) continue;
                if (!development || (name == "Wait For Managed Debugger" && !scriptDebugging))
                    unused.Add(name);
            }
            return unused;
        }
    }
}
