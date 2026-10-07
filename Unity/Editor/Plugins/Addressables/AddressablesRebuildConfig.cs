using System;
using UnityEngine;

namespace BuildForge.Editor.Addressables
{
    [Serializable]
    internal class AddressablesRebuildConfig
    {
        [Tooltip("Rebuild Addressables content before building.")]
        [SerializeField] bool rebuildBeforeBuild;

        [Tooltip("Clean the Addressables build cache before rebuilding.")]
        [SerializeField] bool cleanBeforeRebuild;

        public bool RebuildBeforeBuild
        {
            get => rebuildBeforeBuild;
            set => rebuildBeforeBuild = value;
        }

        public bool CleanBeforeRebuild
        {
            get => cleanBeforeRebuild;
            set => cleanBeforeRebuild = value;
        }
    }
}
