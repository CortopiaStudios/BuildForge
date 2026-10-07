using System;
using UnityEngine;

namespace BuildForge.Editor.Plugins
{
    internal enum CrashReportingMode
    {
        Always = 0,
        CIOnly = 1,
        Never = 2,
    }

    [Serializable]
    internal class CloudDiagnosticsConfig
    {
        [SerializeField] bool enabled = false;
        [SerializeField] CrashReportingMode crashReportingMode = CrashReportingMode.CIOnly;

        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public CrashReportingMode CrashReportingMode
        {
            get => crashReportingMode;
            set => crashReportingMode = value;
        }
    }
}
