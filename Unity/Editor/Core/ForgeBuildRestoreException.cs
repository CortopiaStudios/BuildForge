using System;
using System.Collections.Generic;
using UnityEditor.Build.Reporting;

namespace BuildForge.Editor.Core
{
    /// <summary>
    /// Thrown by <see cref="ForgeBuildRunner.RunBuild(Configuration.ForgeProfile, string)"/> when the
    /// player was built but a step after the build failed, such as putting back a setting a plugin
    /// changed for it, so the project may be left modified. It is thrown once every other step has
    /// run. A player build that failed is reported by its <see cref="BuildReport"/> instead.
    /// </summary>
    public sealed class ForgeBuildRestoreException : Exception
    {
        /// <summary>Unity's report of the player build, which succeeded.</summary>
        public BuildReport Report { get; }

        /// <summary>One message per failed step after the build, as logged.</summary>
        public IReadOnlyList<string> Failures { get; }

        internal ForgeBuildRestoreException(BuildReport report, IReadOnlyList<string> failures)
            : base($"The player was built, but {failures.Count} restore or cleanup step(s) after the build failed, so the " +
                   "project may be left modified; check the changed files before committing. " + string.Join(" ", failures))
        {
            Report = report;
            Failures = failures;
        }
    }
}
