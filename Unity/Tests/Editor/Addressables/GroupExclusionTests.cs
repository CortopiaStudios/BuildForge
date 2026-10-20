using System.Collections.Generic;
using System.Linq;
using BuildForge.Editor.Addressables;
using NUnit.Framework;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace BuildForge.Tests.Addressables
{
    /// <summary>
    /// The Stripper leaves a group out of the build through Include in Build, the group's own from
    /// Addressables 4.1, the bundled schema's before, and stops builds whose exclusions name groups
    /// or labels the project doesn't have. Addressables settings that exist only in memory.
    /// </summary>
    public class GroupExclusionTests
    {
        AddressableAssetSettings settings;

        [SetUp] public void SetUp() =>
            settings = AddressableAssetSettings.Create("Assets/BuildForgeTestAddressables", "Test", false, false);

        [TearDown] public void TearDown()
        {
            foreach (var group in settings.groups.Where(g => g != null).ToList())
            {
                foreach (var schema in group.Schemas.ToList())
                    UnityEngine.Object.DestroyImmediate(schema);
                UnityEngine.Object.DestroyImmediate(group);
            }
            UnityEngine.Object.DestroyImmediate(settings);
        }

        static AddressablesStripperConfig Excluding(params string[] groups)
        {
            var config = new AddressablesStripperConfig();
            config.ExcludedGroups.AddRange(groups);
            return config;
        }

        AddressableAssetGroup Bundled(string name) =>
            settings.CreateGroup(name, false, false, false, null, typeof(BundledAssetGroupSchema));

        [Test]
        public void ExcludedGroups_AreLeftOutOfTheBuild_AndPutBack()
        {
            var excluded = Bundled("Spoilers");
            var kept = Bundled("Act1");
            var names = new List<string> { "Spoilers" };
#if BUILDFORGE_ADDRESSABLES_GROUP_INCLUDE
            // A Content Directory group has no bundled schema, only the group's own Include in Build.
            var content = settings.CreateGroup("Scenes", false, false, false, null, typeof(ContentDirectoryGroupSchema));
            names.Add("Scenes");
#endif
            var config = Excluding(names.ToArray());
            Assert.IsNull(AddressablesStripperPlugin.ExclusionError(config, settings));

            var captured = AddressablesStripperPlugin.ExcludeGroups(settings, config.ExcludedGroups);
            Assert.IsFalse(AddressablesStripperPlugin.IncludeInBuild(excluded));
            Assert.IsTrue(AddressablesStripperPlugin.IncludeInBuild(kept));
#if BUILDFORGE_ADDRESSABLES_GROUP_INCLUDE
            Assert.IsFalse(content.IncludeInBuild, "The Content Directory group is excluded too.");
#endif

            AddressablesStripperPlugin.RestoreGroups(settings, captured);
            Assert.IsTrue(AddressablesStripperPlugin.IncludeInBuild(excluded));
            Assert.IsTrue(AddressablesStripperPlugin.IncludeInBuild(kept));
#if BUILDFORGE_ADDRESSABLES_GROUP_INCLUDE
            Assert.IsTrue(content.IncludeInBuild);
#endif
        }

        // A group or label renamed after it was excluded: the old name excludes nothing.
        [Test]
        public void ExclusionsOfNamesTheProjectDoesNotHave_StopTheBuild()
        {
            Bundled("Act1");
            settings.AddLabel("act1");
            var config = Excluding("Act1", "Act3");
            config.ExcludedLabels.AddRange(new[] { "act1", "act3" });

            var error = AddressablesStripperPlugin.ExclusionError(config, settings);
            StringAssert.Contains("the group 'Act3' and the label 'act3'", error);
            StringAssert.DoesNotContain("'Act1'", error);

            config.ExcludedGroups.Remove("Act3");
            config.ExcludedLabels.Remove("act3");
            Assert.IsNull(AddressablesStripperPlugin.ExclusionError(config, settings));
        }

#if !BUILDFORGE_ADDRESSABLES_GROUP_INCLUDE
        // Before Addressables 4.1, only the bundled schema can leave a group out.
        [Test]
        public void AGroupWithoutTheBundledSchema_CannotBeExcluded()
        {
            settings.CreateGroup("Bare", false, false, false, null);
            StringAssert.Contains("'Bare' can't be excluded", AddressablesStripperPlugin.ExclusionError(Excluding("Bare"), settings));
        }
#endif
    }
}
