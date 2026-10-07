using System;
using System.Collections.Generic;
using BuildForge.Editor.Configuration;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// A variant rule's Player Settings for one build: IL2CPP and stripping. The
    /// static PlayerSettings API writes the active profile's settings (its own
    /// Player Settings when it has them), and a build's profile is the active
    /// one. <see cref="Apply"/> records each setting before changing it, so
    /// <see cref="Restore"/> also undoes an Apply that stopped part way.
    /// </summary>
    internal static class VariantPlayerSettings
    {
        /// <summary>
        /// What a build changed, as it was before. The per-platform settings are
        /// kept as Unity's serialized map, because setting a platform's default
        /// value through the API adds an entry the map didn't have: restoring the
        /// map leaves ProjectSettings.asset as it was.
        /// </summary>
        internal sealed class Snapshot
        {
            internal NamedBuildTarget Target;
            internal readonly Dictionary<string, List<(string key, int value)>> Maps = new();
            internal bool? StripEngineCode;

            internal bool Changed => Maps.Count > 0 || StripEngineCode != null;
        }

        /// <summary>Sets the rule's values that differ from the current ones for <paramref name="snapshot"/>'s target, recording each in it first.</summary>
        internal static void Apply(VariantBuildConfiguration configuration, Snapshot snapshot)
        {
            if (configuration == null) return;
            var target = snapshot.Target;

            var compiler = CompilerConfiguration(configuration.CppCompilerConfiguration);
            if (compiler != null && PlayerSettings.GetIl2CppCompilerConfiguration(target) != compiler)
            {
                Record(snapshot, "il2cppCompilerConfiguration");
                PlayerSettings.SetIl2CppCompilerConfiguration(target, compiler.Value);
            }
            var codeGeneration = CodeGeneration(configuration.Il2CppCodeGeneration);
            if (codeGeneration != null && PlayerSettings.GetIl2CppCodeGeneration(target) != codeGeneration)
            {
                Record(snapshot, "il2cppCodeGeneration");
                PlayerSettings.SetIl2CppCodeGeneration(target, codeGeneration.Value);
            }
            var stacktrace = StacktraceInformation(configuration.Il2CppStacktraceInformation);
            if (stacktrace != null && PlayerSettings.GetIl2CppStacktraceInformation(target) != stacktrace)
            {
                Record(snapshot, "il2cppStacktraceInformation");
                PlayerSettings.SetIl2CppStacktraceInformation(target, stacktrace.Value);
            }
            var stripping = StrippingLevel(configuration.ManagedStrippingLevel);
            if (stripping != null && PlayerSettings.GetManagedStrippingLevel(target) != stripping)
            {
                Record(snapshot, "managedStrippingLevel");
                PlayerSettings.SetManagedStrippingLevel(target, stripping.Value);
            }
            if (configuration.StripEngineCode != VariantOverride.Inherit
                && PlayerSettings.stripEngineCode != (configuration.StripEngineCode == VariantOverride.Enabled))
            {
                snapshot.StripEngineCode = PlayerSettings.stripEngineCode;
                PlayerSettings.stripEngineCode = configuration.StripEngineCode == VariantOverride.Enabled;
            }
        }

        internal static void Restore(Snapshot snapshot)
        {
            if (snapshot == null) return;
            if (snapshot.Maps.Count > 0)
            {
                var playerSettings = PlayerSettingsObject();
                foreach (var map in snapshot.Maps)
                    WriteMap(playerSettings, map.Key, map.Value);
                playerSettings.ApplyModifiedPropertiesWithoutUndo();
            }
            if (snapshot.StripEngineCode != null)
                PlayerSettings.stripEngineCode = snapshot.StripEngineCode.Value;
        }

        /// <summary>
        /// The object the static PlayerSettings API writes: the active Build
        /// Profile's own Player Settings, which falls back to the project's.
        /// Re-resolved each time: a build can unload loaded assets.
        /// </summary>
        static SerializedObject PlayerSettingsObject()
            => new(BuildProfile.GetActiveBuildProfile()?.GetComponent<PlayerSettings>()
                   ?? Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));

        static void Record(Snapshot snapshot, string map)
        {
            if (!snapshot.Maps.ContainsKey(map))
                snapshot.Maps[map] = ReadMap(PlayerSettingsObject(), map);
        }

        /// <summary>A per-platform Player Setting as Unity serializes it: (platform, value) pairs.</summary>
        internal static List<(string key, int value)> ReadMap(SerializedObject playerSettings, string name)
        {
            var map = playerSettings.FindProperty(name)
                      ?? throw new InvalidOperationException($"PlayerSettings has no {name} property on Unity {UnityEngine.Application.unityVersion}.");
            var pairs = new List<(string, int)>();
            for (var i = 0; i < map.arraySize; i++)
            {
                var pair = map.GetArrayElementAtIndex(i);
                pairs.Add((pair.FindPropertyRelative("first").stringValue, pair.FindPropertyRelative("second").intValue));
            }
            return pairs;
        }

        internal static void WriteMap(SerializedObject playerSettings, string name, List<(string key, int value)> pairs)
        {
            var map = playerSettings.FindProperty(name)
                      ?? throw new InvalidOperationException($"PlayerSettings has no {name} property on Unity {UnityEngine.Application.unityVersion}.");
            map.arraySize = pairs.Count;
            for (var i = 0; i < pairs.Count; i++)
            {
                var pair = map.GetArrayElementAtIndex(i);
                pair.FindPropertyRelative("first").stringValue = pairs[i].key;
                pair.FindPropertyRelative("second").intValue = pairs[i].value;
            }
        }

        static Il2CppCompilerConfiguration? CompilerConfiguration(VariantCppCompilerConfiguration value) => value switch
        {
            VariantCppCompilerConfiguration.Debug => Il2CppCompilerConfiguration.Debug,
            VariantCppCompilerConfiguration.Release => Il2CppCompilerConfiguration.Release,
            VariantCppCompilerConfiguration.Master => Il2CppCompilerConfiguration.Master,
            _ => null
        };

        static Il2CppCodeGeneration? CodeGeneration(VariantCodeGeneration value) => value switch
        {
            VariantCodeGeneration.OptimizeSpeed => Il2CppCodeGeneration.OptimizeSpeed,
            VariantCodeGeneration.OptimizeSize => Il2CppCodeGeneration.OptimizeSize,
            _ => null
        };

        static Il2CppStacktraceInformation? StacktraceInformation(VariantStacktraceInformation value) => value switch
        {
            VariantStacktraceInformation.MethodOnly => Il2CppStacktraceInformation.MethodOnly,
            VariantStacktraceInformation.MethodFileLineNumber => Il2CppStacktraceInformation.MethodFileLineNumber,
            _ => null
        };

        static ManagedStrippingLevel? StrippingLevel(VariantStrippingLevel value) => value switch
        {
            VariantStrippingLevel.Disabled => ManagedStrippingLevel.Disabled,
            VariantStrippingLevel.Minimal => ManagedStrippingLevel.Minimal,
            VariantStrippingLevel.Low => ManagedStrippingLevel.Low,
            VariantStrippingLevel.Medium => ManagedStrippingLevel.Medium,
            VariantStrippingLevel.High => ManagedStrippingLevel.High,
            _ => null
        };
    }
}
