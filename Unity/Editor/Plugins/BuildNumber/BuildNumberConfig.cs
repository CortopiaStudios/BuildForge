using System;
using UnityEngine;

namespace BuildForge.Editor.Plugins
{
    internal enum BuildNumberSource
    {
        GitCommitCount = 0,
        EnvironmentVariable = 1,
        // The variable on CI (ForgeBuildContext.IsCI), where the build machine
        // supplies a number; the commit count on developer machines, which
        // have none.
        [InspectorName("Env Var on CI, Git Count Locally")]
        EnvironmentVariableOnCI = 2,
    }

    [Serializable]
    internal class BuildNumberConfig
    {
        [SerializeField] bool enabled = false;
        [SerializeField] BuildNumberSource source = BuildNumberSource.GitCommitCount;
        // Stored only when the user typed a name; the getter supplies the
        // default, so the profile YAML shows an empty field until then.
        [SerializeField] string envVarName;
        [SerializeField] int offset = 0;

        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public BuildNumberSource Source
        {
            get => source;
            set => source = value;
        }

        public string EnvVarName
        {
            get => string.IsNullOrEmpty(envVarName) ? "BUILD_NUMBER" : envVarName;
            set => envVarName = value;
        }

        public int Offset
        {
            get => offset;
            set => offset = value;
        }
    }
}
