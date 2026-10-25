# Release Status — Before 1.0

What is left before 1.0, and the release sequence for it. Update it as items
complete; the release sequence retires it before the release commit. Durable design
knowledge does NOT live here — see ARCHITECTURE.md (decisions, pitfalls),
Development~/LEARNINGS.md (abandoned approaches), and AGENTS.md at the
repository root (working guidelines).

## Remaining before 1.0

**Never checked yet** (as last noted, 2026-10-21):
- by eye: the Addressables Stripper's Move to…/Remove row for exclusions
  under an unknown variant, the Stop Tracking dialog after a failed Revert
  to Baseline, the XR Vendor Filter's grouped package list and the Android
  Manifest inspector;
- Activate of the applied profile while its Apply or a build did not finish:
  the tests cover the message it logs, not Activate itself;
- anything on macOS or Linux.

## Release sequence (owner: Patrik, at the very end, in this order)

1. Final verification: full test run (the plain run and both optional-package
   sets, `BUILDFORGE_PACKAGES=oldest` and `newest`, on 6000.3.0f1 and the newest
   stable editor, 6000.6.6f1 as of 2026-10-22; see AGENTS.md) + the variant proof
   on the same two editors (`Tools~/run-variant-proof.ps1 -Editor <path>`, which
   uses a project of its own per editor; see Development~/VARIANTS-AND-XR.md) + a
   real Android build via the window and via CLI. Runs on a pre-release editor,
   such as 6000.7.0b4, are reported separately, as extra coverage.
2. **Asset metadata** is retained with existing GUIDs. Check for missing,
   duplicate or orphaned metadata before release; no wholesale GUID regeneration.
   `Development~` and `Tools~` are excluded from Unity import and need no metadata.
   A fix goes in a commit of its own.
3. Retire this file, in a commit of its own, so that the 1.0.0 tag doesn't
   carry it: move the release sequence into a lasting release checklist in
   `Development~` (its name chosen then), and the post-1.0 notes below into the
   roadmap (README's Roadmap or ARCHITECTURE's Roadmap Context); delete
   RELEASE-STATUS.md and point AGENTS.md at the repository root to the new
   checklist.
4. CHANGELOG.md: a `## [1.0.0] - <release date>` heading right under
   `## [Unreleased]`, which stays, empty.
5. package.json: `"version": "1.0.0"`.
6. The release commit, with only CHANGELOG.md and package.json, as in the
   0.9.x releases: the subject `Build Forge 1.0.0` and the body
   `The changes since <previous version> are in the CHANGELOG's 1.0.0 section.`
7. An annotated tag `v1.0.0` on that commit, with the message `Build Forge 1.0.0`.
8. Push the commits and the tag.

## Post-1.0 roadmap notes (beyond README's Roadmap section)

From the Unity Build Automation gap analysis (2026-07-31), features addable
in-package without an external runner: auto-increment build-number source
(persistent counter), iOS signing env plugin (Android Keystore Env analog),
post-build command plugin, notifications webhook plugin, local build history
window, per-profile Unity-version pinning warning, clean-build toggle,
`{Version}`/`{BuildNumber}` output-path placeholders. Needs-a-runner items
are folded into README's Out-of-Process Build Orchestrator entry. From the
engine research (Godot/Unreal/O3DE): a validation framework was the one
strong 1.x candidate.

Variant renames (decided 2026-10-21): a variant's name stays its identity,
with no stable IDs; Documentation~/variants.md lists what a rename touches.
A Rename Variant action in Project Settings > Build Forge could preview the
change, then update the Variant Rules, every profile's Allowed Variants and
the Addressables Stripper's exclusions; third-party plugin configs could
join later through an optional interface.

Generated build info (proposed 2026-10-14): an opt-in plugin, off by default,
that generates a committed C# file of static readonly values chosen by the
existing `BUILD_PROFILE_` and `BUILD_VARIANT_` defines, so that code branching
on them with plain `if` statements compiles on every platform, where code under
`#if` is checked only while its define is set. The profile values depend on the
opt-in Maintain Build Profile Defines. A stale file stops builds rather than
building with wrong values. The file is generated in the editor, never during a
build, since writing it there would recompile in the middle of the build.
Documentation~/variants.md shows the pattern written by hand, for variants.

CI (moved here from the pre-1.0 list on 2026-10-21; the release gate stays a
full test run on a developer machine): a GitHub Actions workflow on a Windows
runner that calls `Tools~/run-tests.ps1` for the plain run and both
`BUILDFORGE_PACKAGES` sets, on the floor and the newest stable editor. The license is
handled ONCE as a repo secret, either a `.ulf` (manual activation) or a Unity
Cloud service-account key (`unity auth login --client-id/--client-secret`).
Per-session sandbox activation was evaluated and rejected: ephemeral machines
make it a recurring manual dance. Downloading package versions an editor
hasn't cached needs a signed-in Unity account on 6000.6, so the runner needs
the account too, or a package cache.

Diff view and profile setup (suggested 2026-10-22): the Platform Player
Overrides View could show OpenXR's settings, and possibly those of every Build
Forge plugin, next to Player Settings; README's OpenXR Features Diff View entry
sketches the OpenXR part. It could also help move a setting that several
profiles override with the same value up to Project Settings, so that profiles
hold fewer overrides. And a wizard could create new profiles for known OpenXR
targets.
