# Release Status — 1.0.0 Handoff

Working state for the pre-1.0 push. Written 2026-07-31 as a session handoff;
update or delete as items complete. Durable design knowledge does NOT live
here — see ARCHITECTURE.md (decisions, pitfalls), Development~/LEARNINGS.md
(abandoned approaches), and AGENTS.md at the repository root (working guidelines).

## Remaining before 1.0 (rough order)

1. **Docs cluster** — written after converting real projects: the migration
   guide is `Documentation~/integration-guide.md`, the plugin authoring guide
   `Documentation~/custom-plugins.md` (public API only; InternalsVisibleTo is
   not available to third parties), the CI arguments `Documentation~/ci.md`.
2. **CI** (agreed, after 1.0 structural work settles): GitHub Actions
   workflow reusing the `Tools~` scaffold recipe. License handled ONCE as a
   repo secret — either a `.ulf` (manual activation) or a Unity Cloud
   service-account key (`unity auth login --client-id/--client-secret`).
   Per-session sandbox activation was evaluated and rejected: ephemeral
   machines make it a recurring manual dance.
3. **Never checked yet** (as last noted, 2026-10-06):
   - the OpenXR feature-group build error with a group ticked (needs Unity
     OpenXR Meta);
   - by eye: the Addressables Stripper's Move to…/Remove row for exclusions
     under an unknown variant, the Stop Tracking dialog after a failed Revert
     to Baseline, the XR Vendor Filter's grouped package list and the Android
     Manifest inspector;
   - anything on macOS or Linux.

## Release sequence (owner: Patrik, at the very end, in this order)

1. Final verification: full test run + a real Android build via the window
   and via CLI.
2. CHANGELOG.md: turn `[Unreleased]` into `[1.0.0]` with the release date.
3. **Asset metadata** is retained with existing GUIDs. Check for missing,
   duplicate or orphaned metadata before release; no wholesale GUID regeneration.
   `Development~` and `Tools~` are excluded from Unity import and need no metadata.
4. **Reference source removed:** `PackageSnapshots~` was removed on 2026-09-14.
   Download third-party package source outside the repository when needed.
   Its files remain only in the previous repository's history; this repository
   started without that history on 2026-09-28.
5. Wrapping-project hygiene: its `ProjectSettings/ForgeSettings.asset` may
   still contain stray `com.test.*` plugin entries from old test runs.

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
