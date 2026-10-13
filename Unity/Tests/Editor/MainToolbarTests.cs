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
        public void ApplyEntries_WithVariants_OfferTheDefaultThenEachVariant_ThenRevertWhileApplied()
        {
            var entries = ForgeMainToolbar.ApplyEntries(true, new[] { "Internal", "China" },
                v => v == "China" ? "Variant 'China' is not enabled for 'Quest'." : null,
                applied: true, appliedVariant: "Internal", anythingApplied: true, canMutate: true);

            CollectionAssert.AreEqual(new[] { "<default>", "Internal", "China (unavailable for this profile)", "Revert to Baseline" },
                entries.Select(e => e.Label));
            CollectionAssert.AreEqual(new[] { true, true, false, true }, entries.Select(e => e.Enabled));
            CollectionAssert.AreEqual(new[] { false, true, false, false }, entries.Select(e => e.Checked));
            CollectionAssert.AreEqual(new[] { false, false, false, true }, entries.Select(e => e.Revert));
            CollectionAssert.AreEqual(new[] { null, "Internal", "China", null }, entries.Select(e => e.Variant));
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

            var appliedDefault = ForgeMainToolbar.ApplyEntries(true, new[] { "Internal" }, _ => null,
                applied: true, appliedVariant: null, anythingApplied: true, canMutate: false);
            Assert.That(appliedDefault[0].Checked, "the default build is the applied variant");
            Assert.That(appliedDefault.All(e => !e.Enabled), "Revert included");
        }

        [Test]
        public void ApplyEntries_AnotherProfileApplied_OfferRevert_EvenWhenTheActiveOneCannotBeApplied()
        {
            // Unity switched to another Build Profile without Build Forge: apply the active one, or revert the other.
            var switched = ForgeMainToolbar.ApplyEntries(true, new[] { "Internal" }, _ => null,
                applied: false, appliedVariant: "Internal", anythingApplied: true, canMutate: true);
            CollectionAssert.AreEqual(new[] { "<default>", "Internal", "Revert to Baseline" }, switched.Select(e => e.Label));
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

            var run = ForgeMainToolbar.BuildButtonFor(true, "Quest", null, null, true, null, true, null);
            Assert.That(run.Enabled);
            Assert.AreEqual("Build and Run", run.Label);
            StringAssert.StartsWith("Build 'Quest' with the default variant through Build Forge, then run it.", run.Tooltip);

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
        }

        [Test]
        public void Labels_ShowTheActiveBuildProfileAndWhatIsApplied()
        {
            Assert.AreEqual("Quest", ForgeMainToolbar.ProfileText("Quest"));
            Assert.AreEqual("Platform profile", ForgeMainToolbar.ProfileText(null));
            Assert.AreEqual("Not applied", ForgeMainToolbar.ApplyText(true, false, "Internal"));
            Assert.AreEqual("<default>", ForgeMainToolbar.ApplyText(true, true, null));
            Assert.AreEqual("Internal", ForgeMainToolbar.ApplyText(true, true, "Internal"));
            Assert.AreEqual("Not applied", ForgeMainToolbar.ApplyText(false, false, null));
            Assert.AreEqual("Applied", ForgeMainToolbar.ApplyText(false, true, null));
            // A variant applied before the variant list was emptied is still named, as in the window.
            Assert.AreEqual("Internal", ForgeMainToolbar.ApplyText(false, true, "Internal"));
        }

        [Test]
        public void ApplyIcon_ShowsTheState_CheckMarkWhileApplied_WarningOnceDrifted()
        {
            Assert.IsNull(ForgeMainToolbar.ApplyIcon(applied: false, drifted: false));
            Assert.AreEqual("GreenCheckmark", ForgeMainToolbar.ApplyIcon(applied: true, drifted: false));
            Assert.AreEqual("console.warnicon.sml", ForgeMainToolbar.ApplyIcon(applied: true, drifted: true));
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
    }
}
