using BuildForge.Editor.Core;
using BuildForge.Editor.UI;
using NUnit.Framework;

namespace BuildForge.Tests.Editor
{
    /// <summary>
    /// The line Activate logs after the domain reload (parked in SessionState
    /// across the reload, because a Console set to "Clear on Recompile" drops
    /// everything logged before it), and the warnings for a Build Profile or a
    /// platform profile that became active behind an applied profile, when the
    /// drift notice offers Dismiss Drift, and what the reminder names while nothing drifted.
    /// </summary>
    public class ActivationSummaryTests
    {
        // Dismiss Drift accepts plugin settings only; a missing variant define needs
        // Re-apply or Revert to Baseline.
        [Test]
        public void DismissDrift_IsOfferedOnlyWhilePluginSettingsHaveDrifted()
        {
            const string openXR = "BuildForge.Editor.OpenXR.OpenXRPlugin|Android";
            Assert.IsFalse(ForgeBuildWindow.OffersDismissDrift(new[] { ForgeEditorState.VariantDriftKey }));
            Assert.IsTrue(ForgeBuildWindow.OffersDismissDrift(new[] { openXR }));
            Assert.IsTrue(ForgeBuildWindow.OffersDismissDrift(new[] { openXR, ForgeEditorState.VariantDriftKey }));
        }

        // Settings whose Apply or build did not finish can't be accepted as applied.
        [Test]
        public void DismissDrift_IsNotOfferedForSettingsWhoseApplyOrBuildDidNotFinish()
        {
            const string openXR = "BuildForge.Editor.OpenXR.OpenXRPlugin|Android";
            Assert.IsFalse(ForgeBuildWindow.OffersDismissDrift(new[] { openXR + EditorStateApplier.ApplyUnfinishedSuffix }));
            Assert.IsFalse(ForgeBuildWindow.OffersDismissDrift(new[] { openXR + EditorStateApplier.BuildUnfinishedSuffix }));
            Assert.IsTrue(ForgeBuildWindow.OffersDismissDrift(new[]
                { openXR + EditorStateApplier.BuildUnfinishedSuffix, "BuildForge.Editor.XRManagement.XRLoaderSelectionPlugin|Android" }));
        }

        // The reminder names what is applied, as drift names it, and where the variant's defines are,
        // so it says which version-controlled settings to revert before a commit.
        [Test]
        public void AppliedReminder_NamesTheAppliedSettings_AndTheBuildProfileWithTheVariantsDefines()
        {
            Assert.AreEqual("'Quest' is applied to the editor: OpenXR (Android), Play Mode OpenXR features and the defines " +
                            "of variant 'Develop' on Unity Build Profile 'Quest'. These live in version-controlled assets: " +
                            "Revert to Baseline before committing them and Apply again afterwards, or commit them deliberately.",
                ForgeBuildWindow.AppliedReminder("Quest", new[] { "OpenXR (Android)", "Play Mode OpenXR features" }, "Develop", "Quest"));
            StringAssert.StartsWith("'Quest' is applied to the editor: OpenXR (Android). These live in version-controlled assets:",
                ForgeBuildWindow.AppliedReminder("Quest", new[] { "OpenXR (Android)" }, null, "Quest"));
            StringAssert.StartsWith("'Quest' is applied to the editor: the defines of variant 'Develop' on Unity Build Profile " +
                                    "'Quest'. These live in version-controlled assets:",
                ForgeBuildWindow.AppliedReminder("Quest", System.Array.Empty<string>(), "Develop", "Quest"));
            Assert.AreEqual("'Quest' is applied to the editor, without changing any plugin settings or variant defines.",
                ForgeBuildWindow.AppliedReminder("Quest", System.Array.Empty<string>(), null, "Quest"));
        }

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

        // While an Apply or a build did not finish, applying another profile stops, so it isn't offered.
        [Test]
        public void SwitchedProfileMismatch_WhileInterrupted_DoesNotOfferApply()
        {
            StringAssert.EndsWith("still belong to 'Quest'. Activate 'Quest' again to keep them, or Revert to Baseline.",
                ForgeEditorState.SwitchedProfileMismatch("Pico", "Quest", "Pico", canReactivate: true, interrupted: true));
            StringAssert.EndsWith("Revert to Baseline.",
                ForgeEditorState.SwitchedProfileMismatch("Unmanaged", "Quest", null, canReactivate: false, interrupted: true));
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

        // An Apply or a build that did not finish leaves the profile only partly applied.
        [Test]
        public void TheAppliedProfileActivatedAgain_AfterAnApplyThatDidNotFinish_DoesNotClaimItsSettingsWereKept()
        {
            var text = ForgeEditorState.ReactivationSummary("Quest", null, interrupted: true);
            StringAssert.Contains("did not finish for some of its settings", text);
            StringAssert.DoesNotContain("were kept", text);
        }

        [Test]
        public void InterruptedMessage_NamesWhatDidNotFinishAndTheWayOut()
        {
            var text = ForgeEditorState.InterruptedMessage("Quest", new[] { "OpenXR (Android)" }, new[] { "XR Loaders (Android)" });
            StringAssert.Contains("the Apply of 'Quest' did not finish for OpenXR (Android)", text);
            StringAssert.Contains("a build did not put back 'Quest''s settings for XR Loaders (Android)", text);
            StringAssert.Contains("Revert to Baseline, or Re-apply 'Quest',", text);
            StringAssert.Contains("builds stop", text);

            var gone = ForgeEditorState.InterruptedMessage(null, new[] { "OpenXR (Android)" }, System.Array.Empty<string>());
            StringAssert.Contains("the Apply of the applied profile did not finish", gone);
            StringAssert.DoesNotContain("Re-apply", gone, "No Build Forge profile can apply it again.");
        }

        // Re-apply goes through what stops an Apply: while something stops it, the message offers
        // Revert to Baseline, and says what Re-apply waits for.
        [Test]
        public void InterruptedMessage_OffersReapplyOnlyWhenItWouldRun()
        {
            var text = ForgeEditorState.InterruptedMessage("Quest", new[] { "OpenXR (Android)" }, System.Array.Empty<string>(),
                "the record of the build that did not finish has to be cleared first.");
            StringAssert.Contains("failed or the editor quit. Revert to Baseline in the Build Forge window restores them", text);
            StringAssert.DoesNotContain("or Re-apply", text);
            StringAssert.EndsWith("Re-apply 'Quest' can't restore them yet: the record of the build that did not finish has to be cleared first.", text);
        }
    }
}
