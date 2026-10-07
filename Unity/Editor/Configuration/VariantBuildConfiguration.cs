using System;
using UnityEngine;

namespace BuildForge.Editor.Configuration
{
    internal enum VariantCppCompilerConfiguration { Inherit, Debug, Release, Master }

    internal enum VariantCodeGeneration
    {
        Inherit,
        [InspectorName("Faster runtime")] OptimizeSpeed,
        [InspectorName("Faster (smaller) builds")] OptimizeSize
    }

    internal enum VariantStacktraceInformation
    {
        Inherit,
        [InspectorName("Method Name")] MethodOnly,
        [InspectorName("Method Name, File Name, and Line Number")] MethodFileLineNumber
    }

    internal enum VariantStrippingLevel { Inherit, Disabled, Minimal, Low, Medium, High }

    internal enum VariantCompression { Inherit, Default, LZ4, LZ4HC }

    internal enum VariantLinkTimeOptimization { Inherit, None, Thin }

    internal enum VariantDebugSymbols { Inherit, None, [InspectorName("Symbol Table")] SymbolTable, Full }

    /// <summary>
    /// A variant rule's Unity build settings. Each one is set for the variant's
    /// builds only, restored afterwards, and never written by Apply; Inherit
    /// keeps the Build Profile's or Player Settings' own value.
    /// </summary>
    [Serializable]
    internal sealed class VariantBuildConfiguration
    {
        [Header("Development")]
        [Tooltip("Build Profile > Autoconnect Profiler. Only used in development builds.")]
        [SerializeField] VariantOverride autoconnectProfiler;
        [Tooltip("Build Profile > Deep Profiling Support. Only used in development builds.")]
        [SerializeField] VariantOverride deepProfilingSupport;
        [Tooltip("Build Profile > Script Debugging. Only used in development builds.")]
        [SerializeField] VariantOverride scriptDebugging;
        [Tooltip("Build Profile > Wait For Managed Debugger. Only used in development builds with Script Debugging.")]
        [SerializeField] VariantOverride waitForManagedDebugger;

        [Header("IL2CPP")]
        [Tooltip("Player Settings > C++ Compiler Configuration.")]
        [SerializeField] VariantCppCompilerConfiguration cppCompilerConfiguration;
        [Tooltip("Player Settings > IL2CPP Code Generation.")]
        [SerializeField] VariantCodeGeneration il2CppCodeGeneration;
        [Tooltip("Player Settings > IL2CPP Stacktrace Information.")]
        [SerializeField] VariantStacktraceInformation il2CppStacktraceInformation;

        [Header("Stripping")]
        [Tooltip("Player Settings > Managed Stripping Level.")]
        [SerializeField] VariantStrippingLevel managedStrippingLevel;
        [Tooltip("Player Settings > Strip Engine Code (IL2CPP only).")]
        [SerializeField] VariantOverride stripEngineCode;

        [Header("Build Output")]
        [Tooltip("Build Profile > Compression Method.")]
        [SerializeField] VariantCompression compression;
        [Tooltip("Android Build Profile > Link Time Optimization. Other platforms keep their own settings.")]
        [SerializeField] VariantLinkTimeOptimization linkTimeOptimization;
        [Tooltip("Android Build Profile > Debug Symbols > Level. Other platforms keep their own settings.")]
        [SerializeField] VariantDebugSymbols debugSymbols;

        public VariantOverride AutoconnectProfiler { get => autoconnectProfiler; set => autoconnectProfiler = value; }
        public VariantOverride DeepProfilingSupport { get => deepProfilingSupport; set => deepProfilingSupport = value; }
        public VariantOverride ScriptDebugging { get => scriptDebugging; set => scriptDebugging = value; }
        public VariantOverride WaitForManagedDebugger { get => waitForManagedDebugger; set => waitForManagedDebugger = value; }
        public VariantCppCompilerConfiguration CppCompilerConfiguration { get => cppCompilerConfiguration; set => cppCompilerConfiguration = value; }
        public VariantCodeGeneration Il2CppCodeGeneration { get => il2CppCodeGeneration; set => il2CppCodeGeneration = value; }
        public VariantStacktraceInformation Il2CppStacktraceInformation { get => il2CppStacktraceInformation; set => il2CppStacktraceInformation = value; }
        public VariantStrippingLevel ManagedStrippingLevel { get => managedStrippingLevel; set => managedStrippingLevel = value; }
        public VariantOverride StripEngineCode { get => stripEngineCode; set => stripEngineCode = value; }
        public VariantCompression Compression { get => compression; set => compression = value; }
        public VariantLinkTimeOptimization LinkTimeOptimization { get => linkTimeOptimization; set => linkTimeOptimization = value; }
        public VariantDebugSymbols DebugSymbols { get => debugSymbols; set => debugSymbols = value; }
    }
}
