using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// The main toolbar's Build Profile and apply dropdowns and Build buttons: what
    /// their menus offer and what they show. The toolbar itself needs an
    /// interactive editor.
    /// </summary>
    public class MainToolbarTests
    {
        readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        BuildProfile NewBuildProfile(string name)
        {
            var factory = typeof(BuildProfile)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "CreateInstance" && m.GetParameters().Length == 2);
            Assert.IsNotNull(factory, "BuildProfile.CreateInstance(BuildTarget, StandaloneBuildSubtarget) not found.");
            var bp = (BuildProfile)factory.Invoke(null,
                new object[] { BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player });
            bp.name = name;
            created.Add(bp);
            return bp;
        }

        ForgeProfile NewForgeProfile(string name, BuildProfile buildProfile)
        {
            var profile = ScriptableObject.CreateInstance<ForgeProfile>();
            profile.name = name;
            if (buildProfile != null)
            {
                var so = new SerializedObject(profile);
                so.FindProperty("buildProfile").objectReferenceValue = buildProfile;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            created.Add(profile);
            return profile;
        }

        [Test]
        public void ProfileEntries_CheckTheActiveOne_AndDisableProfilesThatCannotBeActivated()
        {
            var quest = NewBuildProfile("Quest");
            var shared = NewBuildProfile("Shared");
            var all = new List<ForgeProfile>
            {
                NewForgeProfile("PicoForge", NewBuildProfile("Pico")),
                NewForgeProfile("QuestForge", quest),
                NewForgeProfile("Unassigned", null),
                NewForgeProfile("SharedA", shared),
                NewForgeProfile("SharedB", shared),
            };

            var entries = ForgeMainToolbar.ProfileEntries(all, quest, canMutate: true);

            CollectionAssert.AreEqual(new[]
            {
                "Pico", "Quest", "Shared (shares its Unity Build Profile)", "Shared (shares its Unity Build Profile)",
                "Unassigned (no Unity Build Profile)",
            }, entries.Select(e => e.Label));
            CollectionAssert.AreEqual(new[] { true, false, false, false, false }, entries.Select(e => e.Enabled));
            CollectionAssert.AreEqual(new[] { false, true, false, false, false }, entries.Select(e => e.Checked));
        }

        [Test]
        public void ProfileEntries_WhileMutatingIsRefused_AreAllDisabled()
        {
            var quest = NewBuildProfile("Quest");
            var all = new List<ForgeProfile> { NewForgeProfile("QuestForge", quest), NewForgeProfile("PicoForge", NewBuildProfile("Pico")) };

            var entries = ForgeMainToolbar.ProfileEntries(all, quest, canMutate: false);

            Assert.That(entries.All(e => !e.Enabled));
            Assert.That(entries.Single(e => e.Checked).Label, Is.EqualTo("Quest"));
        }

        [Test]
        public void ApplyEntries_WithVariants_OfferEachVariant_ThenRevertWhileApplied()
        {
            var entries = ForgeMainToolbar.ApplyEntries(true, new[] { "Internal", "China" },
                v => v == "China" ? "Variant 'China' is not enabled for 'Quest'." : null,
                applied: true, appliedVariant: "Internal", anythingApplied: true, canMutate: true);

            CollectionAssert.AreEqual(new[] { "Internal", "China (unavailable for this profile)", "Revert to Baseline" },
                entries.Select(e => e.Label));
            CollectionAssert.AreEqual(new[] { true, false, true }, entries.Select(e => e.Enabled));
            CollectionAssert.AreEqual(new[] { true, false, false }, entries.Select(e => e.Checked));
            CollectionAssert.AreEqual(new[] { false, false, true }, entries.Select(e => e.Revert));
            CollectionAssert.AreEqual(new[] { "Internal", "China", null }, entries.Select(e => e.Variant));
        }

        [Test]
        public void ApplyEntries_WithoutVariants_OfferApply_ThenReapplyAndRevertOnceApplied()
        {
            var notApplied = ForgeMainToolbar.ApplyEntries(true, new string[0], _ => null,
                applied: false, appliedVariant: null, anythingApplied: false, canMutate: true);
            CollectionAssert.AreEqual(new[] { "Apply" }, notApplied.Select(e => e.Label));
            Assert.That(notApplied[0].Enabled && !notApplied[0].Revert && notApplied[0].Variant == null);

            var applied = ForgeMainToolbar.ApplyEntries(true, null, _ => null,
                applied: true, appliedVariant: null, anythingApplied: true, canMutate: true);
            CollectionAssert.AreEqual(new[] { "Re-apply", "Revert to Baseline" }, applied.Select(e => e.Label));
            CollectionAssert.AreEqual(new[] { false, true }, applied.Select(e => e.Revert));
        }

        [Test]
        public void ApplyEntries_NothingApplied_HaveNoRevert_AndRefusedMutationDisablesThem()
        {
            var notApplied = ForgeMainToolbar.ApplyEntries(true, new[] { "Internal" }, _ => null,
                applied: false, appliedVariant: null, anythingApplied: false, canMutate: true);
            Assert.That(notApplied.All(e => !e.Checked && e.Enabled && !e.Revert));

            var applied = ForgeMainToolbar.ApplyEntries(true, new[] { "Internal" }, _ => null,
                applied: true, appliedVariant: "Internal", anythingApplied: true, canMutate: false);
            Assert.That(applied[0].Checked, "the applied variant");
            Assert.That(applied.All(e => !e.Enabled), "Revert included");

            // Applied without a variant, before the project had variants: none is checked.
            var withoutVariant = ForgeMainToolbar.ApplyEntries(true, new[] { "Internal" }, _ => null,
                applied: true, appliedVariant: null, anythingApplied: true, canMutate: true);
            Assert.That(withoutVariant.All(e => !e.Checked));
        }

        [Test]
        public void ApplyEntries_AnotherProfileApplied_OfferRevert_EvenWhenTheActiveOneCannotBeApplied()
        {
            // Unity switched to another Build Profile without Build Forge: apply the active one, or revert the other.
            var switched = ForgeMainToolbar.ApplyEntries(true, new[] { "Internal" }, _ => null,
                applied: false, appliedVariant: "Internal", anythingApplied: true, canMutate: true);
            CollectionAssert.AreEqual(new[] { "Internal", "Revert to Baseline" }, switched.Select(e => e.Label));
            Assert.That(switched.All(e => !e.Checked), "the applied variant belongs to the other profile");

            // A platform profile, or a Build Profile without a Build Forge Profile, is active: only the revert.
            var platform = ForgeMainToolbar.ApplyEntries(false, new[] { "Internal" }, _ => null,
                applied: false, appliedVariant: null, anythingApplied: true, canMutate: true);
            CollectionAssert.AreEqual(new[] { "Revert to Baseline" }, platform.Select(e => e.Label));
            Assert.That(platform[0].Revert && platform[0].Enabled);

            // Nothing to apply and nothing applied: nothing to offer, so the dropdown is hidden.
            Assert.IsEmpty(ForgeMainToolbar.ApplyEntries(false, new string[0], _ => null,
                applied: false, appliedVariant: null, anythingApplied: false, canMutate: true));
        }

        [Test]
        public void BuildButtons_NameWhatTheyBuild()
        {
            var build = ForgeMainToolbar.BuildButtonFor(false, "Quest", null, "Internal", true, null, true, null);
            Assert.That(build.Enabled);
            Assert.AreEqual("Build", build.Label);
            Assert.AreEqual("Build 'Quest' with variant 'Internal' through Build Forge. " +
                            "The variant is the build window's, which the apply menu also sets.", build.Tooltip);

            var run = ForgeMainToolbar.BuildButtonFor(true, "Quest", null, "Internal", true, null, true, null);
            Assert.That(run.Enabled);
            Assert.AreEqual("Build and Run", run.Label);
            StringAssert.StartsWith("Build 'Quest' with variant 'Internal' through Build Forge, then run it.", run.Tooltip);

            // Without variants the tooltip doesn't mention them.
            var plain = ForgeMainToolbar.BuildButtonFor(false, "Quest", null, null, false, null, true, null);
            Assert.AreEqual("Build 'Quest' through Build Forge.", plain.Tooltip);
        }

        [Test]
        public void BuildButtons_AreDisabledWhenTheWindowsBuildWouldBe_WithTheReason()
        {
            var refused = ForgeMainToolbar.BuildButtonFor(false, "Quest", null, null, false, null, false, "Not available in Play Mode.");
            Assert.That(!refused.Enabled);
            Assert.AreEqual("Not available in Play Mode.", refused.Tooltip);

            var noProfile = ForgeMainToolbar.BuildButtonFor(true, null, "Activate a Build Profile with Build Forge settings to build it.",
                null, true, null, true, null);
            Assert.That(!noProfile.Enabled);
            Assert.AreEqual("Activate a Build Profile with Build Forge settings to build it.", noProfile.Tooltip);

            var variantRefused = ForgeMainToolbar.BuildButtonFor(false, "Quest", null, "China", true,
                "Variant 'China' is not enabled for 'Quest'.", true, null);
            Assert.That(!variantRefused.Enabled);
            Assert.AreEqual("Variant 'China' is not enabled for 'Quest'.", variantRefused.Tooltip);

            // Once the project has variants, nothing is built until one is picked or applied.
            var unpicked = ForgeMainToolbar.BuildButtonFor(false, "Quest", null, null, true,
                "No build variant given.", true, null);
            Assert.That(!unpicked.Enabled);
            Assert.AreEqual(ForgeMainToolbar.PickVariantReason, unpicked.Tooltip);
        }

        [Test]
        public void Labels_ShowTheActiveBuildProfileAndWhatIsApplied()
        {
            Assert.AreEqual("Quest", ForgeMainToolbar.ProfileText("Quest"));
            Assert.AreEqual("Platform profile", ForgeMainToolbar.ProfileText(null));
            Assert.AreEqual("Not applied", ForgeMainToolbar.ApplyText(false, "Internal"));
            Assert.AreEqual("Not applied", ForgeMainToolbar.ApplyText(false, null));
            Assert.AreEqual("Applied", ForgeMainToolbar.ApplyText(true, null));
            // Also a variant applied before the variant list was emptied, as in the window.
            Assert.AreEqual("Internal", ForgeMainToolbar.ApplyText(true, "Internal"));
        }

        [Test]
        public void ApplyIcon_ShowsTheState_CheckMarkWhileApplied_WarningOnceDriftedOrWithAnotherProfileApplied()
        {
            Assert.IsNull(ForgeMainToolbar.ApplyIcon(applied: false, drifted: false, anythingApplied: false));
            Assert.AreEqual("GreenCheckmark", ForgeMainToolbar.ApplyIcon(applied: true, drifted: false, anythingApplied: true));
            Assert.AreEqual("console.warnicon.sml", ForgeMainToolbar.ApplyIcon(applied: true, drifted: true, anythingApplied: true));
            // Unity switched profiles without Build Forge: another profile's settings are
            // still applied underneath the active one, which reads "Not applied".
            Assert.AreEqual("console.warnicon.sml", ForgeMainToolbar.ApplyIcon(applied: false, drifted: false, anythingApplied: true));
            // Both icons exist in the editor.
            Assert.IsNotNull(EditorGUIUtility.FindTexture("GreenCheckmark"));
            Assert.IsNotNull(EditorGUIUtility.FindTexture("console.warnicon.sml"));
        }

        [Test]
        public void BuildIcons_AreTheAnvilOnBuild_AndUnitysPlayIconOnBuildAndRun()
        {
            // Shared by the build window's action bar and the toolbar's buttons.
            var anvil = ForgeBuildWindow.BuildIcon(run: false);
            Assert.IsNotNull(anvil, "The anvil icon did not load from the package's Editor/Icons folder.");
            StringAssert.EndsWith("ForgeProfile", anvil.name);
            Assert.IsNotNull(ForgeBuildWindow.BuildIcon(run: true));
            Assert.AreSame(EditorGUIUtility.IconContent("PlayButton").image, ForgeBuildWindow.BuildIcon(run: true));
        }

        // The toolbar redraws when its state key changes. The unfinished-build marker, which
        // disables its Build buttons, is outside the AssetDatabase, so the key follows the file.
        [Test]
        public void TheStateKey_FollowsTheUnfinishedBuildMarker()
        {
            var path = BuildForge.Editor.Core.InterruptedBuildMarker.FilePath;
            var previous = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
            try
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
                var without = ForgeMainToolbar.StateKey();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, "{}");
                Assert.AreNotEqual(without, ForgeMainToolbar.StateKey());
                System.IO.File.Delete(path);
                Assert.AreEqual(without, ForgeMainToolbar.StateKey());
            }
            finally
            {
                if (previous != null)
                    System.IO.File.WriteAllText(path, previous);
                else if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
        }
    }
}
