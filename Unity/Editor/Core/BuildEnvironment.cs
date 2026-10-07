using System;
using UnityEngine;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Answers "is this build running in CI?" for plugins that behave differently
    /// on build machines than on developer machines (e.g. uploading symbols).
    /// Batch mode is the default signal; an explicit -forgeCI argument or FORGE_CI
    /// environment variable overrides it for the cases where the two differ
    /// (local scripted builds, interactive CI agents).
    /// </summary>
    internal static class BuildEnvironment
    {
        public const string CommandLineArg = "-forgeCI";
        public const string EnvVarName = "FORGE_CI";

        public static bool IsCI => ResolveIsCI(
            Environment.GetCommandLineArgs(),
            Environment.GetEnvironmentVariable(EnvVarName),
            Application.isBatchMode);

        /// <summary>
        /// Precedence: -forgeCI [true|false] argument, then FORGE_CI environment
        /// variable, then batch mode. A bare -forgeCI (no value, or followed by
        /// another -flag) means true.
        /// </summary>
        public static bool ResolveIsCI(string[] args, string envValue, bool isBatchMode)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (!string.Equals(args[i], CommandLineArg, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("-");
                    if (hasValue && TryParseBool(args[i + 1], out var argValue))
                        return argValue;
                    return true;
                }
            }

            if (TryParseBool(envValue, out var envBool))
                return envBool;

            return isBatchMode;
        }

        /// <summary>
        /// Why the CI signal cannot be read, or null. <see cref="ResolveIsCI"/>
        /// reads an unrecognized -forgeCI value as a bare -forgeCI (true) and
        /// ignores an unrecognized FORGE_CI, so a mistyped "false" would count as
        /// CI; the CLI build fails on both instead. An empty FORGE_CI counts as unset.
        /// </summary>
        public static string ValueError(string[] args, string envValue)
        {
            for (int i = 0; args != null && i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], CommandLineArg, StringComparison.OrdinalIgnoreCase) &&
                    !args[i + 1].StartsWith("-") && !TryParseBool(args[i + 1], out _))
                    return $"{CommandLineArg} takes true or false (or 1/0, yes/no), not '{args[i + 1]}'.";
            }

            if (!string.IsNullOrWhiteSpace(envValue) && !TryParseBool(envValue, out _))
                return $"The {EnvVarName} environment variable must be true or false (or 1/0, yes/no), not '{envValue}'.";
            return null;
        }

        static bool TryParseBool(string value, out bool result)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                    result = true;
                    return true;
                case "0":
                case "false":
                case "no":
                    result = false;
                    return true;
                default:
                    result = false;
                    return false;
            }
        }
    }
}
