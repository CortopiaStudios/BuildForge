using BuildForge.Editor.Core;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// The line Activate logs after the domain reload (parked in SessionState
    /// across the reload, because a Console set to "Clear on Recompile" drops
    /// everything logged before it), and the warnings for a Build Profile or a
    /// platform profile that became active behind an applied profile.
    /// </summary>
    public class ActivationSummaryTests
    {
        // A deleted Library/ or -buildTarget leaves a platform profile active,
        // which no Build Forge profile can reference.
        [Test]
        public void PlatformProfileMismatch_NamesTheTargetAndTheAppliedProfile()
        {
            var text = ForgeEditorState.PlatformProfileMismatch("StandaloneWindows64", "Quest", canReactivate: true);
            StringAssert.StartsWith("Unity is on the StandaloneWindows64 platform profile, not on a Build Profile;", text);
            StringAssert.Contains("still belong to 'Quest'", text);
            StringAssert.EndsWith("Activate 'Quest' again to keep them, or Revert to Baseline.", text);
        }

        [Test]
        public void PlatformProfileMismatch_WithoutABuildForgeProfileToActivate_PointsToUnitysWindow()
        {
            var text = ForgeEditorState.PlatformProfileMismatch("StandaloneWindows64", "Quest", canReactivate: false);
            StringAssert.EndsWith("Revert to Baseline, or switch back to 'Quest' in Unity's Build Profiles window to keep them.", text);
            StringAssert.DoesNotContain("Activate", text);
        }

        [Test]
        public void SwitchedProfileMismatch_OffersApplyActivateAgainAndRevert()
        {
            Assert.AreEqual(
                "Unity switched to 'Pico' without applying its Build Forge settings; the plugin-managed editor settings " +
                "still belong to 'Quest'. Apply 'Pico' to replace them with its own, activate 'Quest' again to keep them, " +
                "or Revert to Baseline.",
                ForgeEditorState.SwitchedProfileMismatch("Pico", "Quest", "Pico", canReactivate: true));
        }

        [Test]
        public void SwitchedProfileMismatch_OffersOnlyWhatCanBeDone()
        {
            // No Build Forge Profile references the active Build Profile: nothing to apply.
            StringAssert.EndsWith(
                "Activate 'Quest' again to keep them, or Revert to Baseline " +
                "(the active Unity Build Profile has no Build Forge settings).",
                ForgeEditorState.SwitchedProfileMismatch("Unmanaged", "Quest", null, canReactivate: true));

            // The applied Build Profile is gone: nothing to activate again.
            var gone = ForgeEditorState.SwitchedProfileMismatch("Pico", null, "Pico", canReactivate: false);
            StringAssert.Contains("still belong to a Unity Build Profile that no longer exists.", gone);
            StringAssert.EndsWith("Apply 'Pico' to replace them with its own, or Revert to Baseline.", gone);
        }

        [Test]
        public void NothingWasApplied_NamesOnlyTheActivatedProfile()
        {
            var text = ForgeEditorState.ActivationSummary("Frame", null, null, false);
            Assert.AreEqual(
                "[Build Forge] Activated 'Frame'. Plugin settings are not applied; use Apply to write those of 'Frame'.",
                text);
        }

        [Test]
        public void PreviousProfileWithVariantAndDefine_IsSpelledOut()
        {
            var text = ForgeEditorState.ActivationSummary("Frame", "Quest", "Internal", true);
            Assert.AreEqual(
                "[Build Forge] Activated 'Frame'; reverted 'Quest' (variant Internal) to baseline and removed its variant define. "
                + "Plugin settings are not applied; use Apply to write those of 'Frame'.",
                text);
        }

        [Test]
        public void PreviousProfileWithoutVariant_OmitsVariantAndDefine()
        {
            var text = ForgeEditorState.ActivationSummary("Frame", "Quest", "", false);
            StringAssert.Contains("reverted 'Quest' to baseline.", text);
            StringAssert.DoesNotContain("variant", text);
        }

        // Unity was switched away from the applied profile without Build Forge;
        // activating it again keeps what is applied.
        [Test]
        public void TheAppliedProfileActivatedAgain_SaysItsSettingsWereKept()
        {
            Assert.AreEqual(
                "[Build Forge] Activated 'Quest', which is still applied (variant Internal): its plugin settings were kept, not reverted.",
                ForgeEditorState.ReactivationSummary("Quest", "Internal"));
            Assert.AreEqual(
                "[Build Forge] Activated 'Quest', which is still applied: its plugin settings were kept, not reverted.",
                ForgeEditorState.ReactivationSummary("Quest", null));
        }
    }
}
