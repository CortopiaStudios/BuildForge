using System;
using UnityEngine;

namespace BuildForge.Editor.Plugins
{
    [Serializable]
    internal class GitMetadataConfig
    {
        [SerializeField] bool includeBranch = true;
        [SerializeField] bool includeCommit = true;
        [SerializeField] bool includeShortCommit = true;
        [SerializeField] bool includeDescribe;
        [SerializeField] bool includeExactTag;

        public bool IncludeBranch
        {
            get => includeBranch;
            set => includeBranch = value;
        }

        public bool IncludeCommit
        {
            get => includeCommit;
            set => includeCommit = value;
        }

        public bool IncludeShortCommit
        {
            get => includeShortCommit;
            set => includeShortCommit = value;
        }

        public bool IncludeDescribe
        {
            get => includeDescribe;
            set => includeDescribe = value;
        }

        public bool IncludeExactTag
        {
            get => includeExactTag;
            set => includeExactTag = value;
        }
    }
}
