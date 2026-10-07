using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using BuildForge.Runtime;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Editor.Plugins
{
    /// <summary>
    /// Build Forge plugin that adds git metadata (branch name and commit hash)
    /// to the build manifest. Configurable per profile to include/exclude
    /// individual fields.
    /// </summary>
    [ForgePlugin]
    internal class GitMetadataPlugin : IForgePlugin, IForgeManifestContributor
    {
        const string ConfigKey = "Git";

        public string DisplayName => "Git Metadata";
        public string Description => "Add git branch, commit, and tag metadata to the build manifest.";
        public int Order => 300;

        public bool IsApplicable(BuildProfile profile) => true;

        public void OnPreBuild(ForgeBuildContext context) { }
        public void OnPostBuild(ForgeBuildContext context) { }

        public void ContributeToManifest(BuildManifest manifest, ForgeBuildContext context)
        {
            var config = GetConfig(context.ForgeProfile);
            var section = new ManifestSection("Git");
            bool hasEntries = false;

            if (config.IncludeBranch)
            {
                var branch = RunGit("rev-parse --abbrev-ref HEAD");
                if (branch != null)
                {
                    section.Add("branch", branch);
                    hasEntries = true;
                }
            }

            if (config.IncludeCommit)
            {
                var commit = RunGit("rev-parse HEAD");
                if (commit != null)
                {
                    section.Add("commit", commit);
                    hasEntries = true;
                }
            }

            if (config.IncludeShortCommit)
            {
                var shortCommit = RunGit("rev-parse --short HEAD");
                if (shortCommit != null)
                {
                    section.Add("commitShort", shortCommit);
                    hasEntries = true;
                }
            }

            if (config.IncludeDescribe)
            {
                var describe = RunGit("describe --tags");
                if (describe != null)
                {
                    section.Add("describe", describe);
                    hasEntries = true;
                }
            }

            if (config.IncludeExactTag)
            {
                // Empty when HEAD is not exactly on a tag.
                var exactTag = RunGit("tag --points-at HEAD");
                if (!string.IsNullOrEmpty(exactTag))
                {
                    // A commit can carry multiple tags; take the first line.
                    var firstTag = exactTag.Split('\n')[0].Trim();
                    section.Add("tag", firstTag);
                    hasEntries = true;
                }
            }

            if (hasEntries)
            {
                manifest.AddSection(section);
                Debug.Log($"[Build Forge/Git] Added git metadata to manifest.");
            }
        }

        public string DescribeBuild(ForgeProfile forgeProfile, bool isCI)
        {
            var config = GetConfig(forgeProfile);
            var parts = new System.Collections.Generic.List<string>();
            if (config.IncludeBranch) parts.Add("branch");
            if (config.IncludeCommit) parts.Add("commit");
            if (config.IncludeShortCommit) parts.Add("short commit");
            if (config.IncludeDescribe) parts.Add("describe");
            if (config.IncludeExactTag) parts.Add("exact tag");
            return parts.Count == 0 ? "nothing selected for the manifest" : "manifest: " + string.Join(", ", parts);
        }

        public void OnDrawProfileGUI(ForgeProfile forgeProfile)
        {
            var config = GetConfig(forgeProfile);
            bool changed = false;

            var newBranch = EditorGUILayout.Toggle("Branch", config.IncludeBranch);
            if (newBranch != config.IncludeBranch)
            {
                config.IncludeBranch = newBranch;
                changed = true;
            }

            var newCommit = EditorGUILayout.Toggle("Commit Hash", config.IncludeCommit);
            if (newCommit != config.IncludeCommit)
            {
                config.IncludeCommit = newCommit;
                changed = true;
            }

            var newShort = EditorGUILayout.Toggle("Short Commit Hash", config.IncludeShortCommit);
            if (newShort != config.IncludeShortCommit)
            {
                config.IncludeShortCommit = newShort;
                changed = true;
            }

            var newDescribe = EditorGUILayout.Toggle(
                new GUIContent("Describe", "git describe --tags (e.g. v1.2.3-5-gabc1234); omitted if the repo has no tags."),
                config.IncludeDescribe);
            if (newDescribe != config.IncludeDescribe)
            {
                config.IncludeDescribe = newDescribe;
                changed = true;
            }

            var newExactTag = EditorGUILayout.Toggle(
                new GUIContent("Exact Tag", "The tag on HEAD, if any (git tag --points-at HEAD); omitted when HEAD is not a tagged release."),
                config.IncludeExactTag);
            if (newExactTag != config.IncludeExactTag)
            {
                config.IncludeExactTag = newExactTag;
                changed = true;
            }

            if (changed)
                SaveConfig(forgeProfile, config);
        }

        static GitMetadataConfig GetConfig(ForgeProfile profile)
        {
            return profile.GetPluginConfig<GitMetadataConfig>(ConfigKey);
        }

        static void SaveConfig(ForgeProfile profile, GitMetadataConfig config)
        {
            profile.SetPluginConfig(ConfigKey, config);
        }

        static string RunGit(string arguments) => GitHelper.Run(arguments);
    }
}
