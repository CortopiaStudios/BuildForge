# Release Status — 1.0.0 Handoff

Working state for the pre-1.0 push. Written 2026-07-31 as a session handoff;
update or delete as items complete. Durable design knowledge does NOT live
here — see ARCHITECTURE.md (decisions, pitfalls), Development~/LEARNINGS.md
(abandoned approaches), and AGENTS.md at the repository root (working guidelines).

## Where things stand

- On 2026-09-28 the package moved to github.com/CortopiaStudios/BuildForge, into
  `Unity/` (a planned CI server gets `Server/`), without the previous repository's
  history. `AGENTS.md` and `CLAUDE.md` moved to the repository root, so they no
  longer ship with the package.
- Current results (2026-09-29): EditMode 468/470 in the clean core host on
  6000.3.0f1, and 486/488 in the OpenXR + Addressables hosts on 6000.3.0f1 and
  6000.6.0f1; the 2 skips are the conditional `PlayerSettingsProfileTests`. Real
  builds since 2026-09-16: Remove Previous Build Output on Windows (6000.3.0f1,
  6000.6.0f1) and Android IL2CPP (6000.3.0f1); the variant player proof on
  6000.3.23f1 with the strict CI arguments and the reversed restore order, last run
  2026-09-29; and on 2026-09-29 Windows players for the committed-while-applied
  Development variant scenario on 6000.3.0f1 and 6000.6.0f1 (below). Not re-run
  since 2026-09-14: the EditMode suite on 6000.3.23f1. Not run at all: the OpenXR
  feature-group build error with a group ticked (needs Unity OpenXR Meta); the two
  new editor UIs, which need an interactive look (the Addressables Stripper's
  Move to…/Remove row for exclusions under an unknown variant, and the Stop Tracking
  dialog after a failed Revert to Baseline); and anything on macOS/Linux.
- On 2026-10-06, on `feature/embedded-profiles` (not on `main`): the XR Vendor
  Filter (order 1000) and Android Manifest (900) plugins, and
  `IForgeGradleProcessor`, a step for plugins in an Android build's Gradle project
  after the vendors' own (Unity callback order 1000000). The filter keeps the
  Android libraries of the packages a profile excludes out of its builds, with
  Unity's include-in-build delegate, and removes the manifest entries that Meta's
  and Pico's Gradle steps write, which no setting prevents. EditMode 547 with 2
  skips in the OpenXR + Addressables hosts on 6000.3.0f1 and 6000.6.0f1, and on
  6000.6.4f1 with OpenXR 1.18 and Addressables 4.1.0. Real
  Android builds in three game projects: their Steam Frame APKs no longer carry
  the other vendors' platform loaders and manifest entries, and in one project the
  APK has the same files and entries as the APK of the packaging hook the plugins
  replaced. A look at the XR Vendor Filter's inspector in a game project led to
  its grouped package list (vendors by vendor, the other packages folded), which
  has not been seen rendered yet; neither has the Android Manifest inspector.
  Details are in ARCHITECTURE.md ("XR Vendor SDKs in Other
  Vendors' Builds") and the CHANGELOG.
- On 2026-09-29, from a review of what a fresh clone gets: editor Apply no longer
  writes a variant rule's Development Build flag to the Build Profile. A Build
  Profile committed while a Development variant was applied had made teammates'
  and CI's Default builds development players, and a teammate's Activate removed
  the only trace. A `BUILD_VARIANT_` define on a Build Profile that is not applied
  on this machine is now reported in the build window, the profile inspector and
  the build log, and Activate removes it with the rules' aliases. The
  active-vs-applied warning also covers a platform profile (a deleted `Library/`,
  `-buildTarget`, Unity's Platforms list), and activating the applied profile
  again keeps its settings instead of reverting them. On 6000.3.0, Unity's Build
  Profiles window no longer asks to apply or revert the defines Build Forge writes
  (that patch's setter left Unity's record of the compiled defines behind;
  6000.3.23 updates it). The README has a Fresh Clones section (open a first clone
  with `-activeBuildProfile`; commit settings only while nothing is applied), and
  its Unity CLI notes were rechecked with beta.11. Verified with real Windows
  players on 6000.3.0f1 and 6000.6.0f1: the fresh-clone, CI and teammate Default
  players were development builds before the change and release builds after it.
- On 2026-09-29: Unity's main toolbar has Build Forge's Build Profile and apply
  dropdowns, one toolbar element named Build Forge. They activate a profile, apply
  it with a variant and Revert to Baseline like the build window, whose wording they
  share ("Applied (Internal)", ", drifted"). Unity starts package toolbar elements
  hidden and offers no default, so Build Forge shows its own through the overlay
  Unity creates for it (the overlay id is the element path on 6000.3, 6000.6 and
  Unity's `master`; if that changes, the element just stays hidden) and keeps Hide
  and Show per user in `UserSettings/ForgeUserPreferences.asset`. Walked through
  the editor states interactively on 6000.3.0f1 and 6000.6.0f1; Unity 6.6 cuts
  toolbar labels at 120 px. The build window's tab shows the Build Forge anvil,
  which now also has a light theme version (seen in both themes). Details are in
  ARCHITECTURE.md ("Main Toolbar") and the CHANGELOG.
- Since 2026-09-16: Remove Previous Build Output deletes only what the previous
  build of the same profile and variant wrote (recorded per user) instead of
  clearing the folder; the variant ledger records only what Apply changed; the
  package is named `com.cortopiastudios.buildforge`; CI arguments are strict and
  `-versionCode` became `-forgeVersionCode`; Addressables exclusions left under a
  renamed variant stop the build; a failed Revert to Baseline offers Stop Tracking;
  OpenXR says a build stops on a Disable override that a feature group blocks; XR
  Loaders turned off for a profile is no longer re-applied after a build; builds
  undo their changes in reverse order; Player Overrides compares batching and
  Dedicated Server settings for the profile's own platform; the public API test
  covers the XR Management assembly, and the XR tests compile without warnings on
  XR Management 4.7. Every finding of the pre-merge review is resolved. Details
  are in the CHANGELOG.
- The variants/XR/CLI tightening pass completed on 2026-09-14. Its results, exact package
  versions and reproduction commands are in [VALIDATION-2026-09-14.md](VALIDATION-2026-09-14.md):
  352/352 tests on each of 6000.3.0f1, 6000.3.23f1 and 6000.6.0f1; real
  Development/Default players and injected-failure restoration pass on all three.
  A clean non-XR host also passes, with only the two expected conditional skips.
  Existing GUIDs are retained; the sample now has stable metadata. The OpenXR
  reference source snapshot has been removed. macOS/Linux execution remains unverified.

- The September feature audit's four P1 findings are fixed: the CLI version
  flag is now `-forgeVersion`, restored native PlayerSettings are persisted,
  editor-state suspension/resumption failures fail builds, and enabled
  manifest failures cannot produce a successful CI result.
- The audit's six P2 findings are also fixed: failed Revert retains baselines
  for retry and blocks Apply/Activate until recovery succeeds; Player Overrides
  compares the selected target's platform entries; manual Re-apply stays
  available after configuration edits; the window's minimum width fits its
  action controls; build-number sums are range-checked; and the README's
  `unity run` example no longer forwards the reserved `-batchmode` flag.
- Historical baseline (2026-09-11, before the variants/XR/CLI extension): 301 Build Forge tests. On both 6000.3.0f1 and
  6000.6.0f1, minimal hosts pass 299 with 2 expected conditional skips
  (`PlayerSettingsProfileTests` need an active profile with overrides);
  full hosts with OpenXR, Addressables, and native profile overrides pass
  all 301. Temporary projects, real-build regression logs, and reopen
  assertions are retained under
  `%LOCALAPPDATA%\BuildForge\audit-20260911\p1-fixes` and `p2-fixes`.
  The P2 pass also verified successful CI and interactive Windows builds,
  their players, recovery across editor restarts, and restored settings on
  both versions; see `p2-fixes/VALIDATION.md` for the full evidence index.
  The test scripts pick the oldest installed 6000.3.x on purpose; set
  UNITY_EDITOR_VERSION for the newest.
- Since the handoff: profile switching in the editor (per-user ledger in `UserSettings/`),
  CI detection + Cloud Diagnostics plugin, native `[SerializeReference]`
  plugin config storage with per-property OpenXR feature pins,
  save-on-edit for profiles, the 1:1 Build Forge Profile <-> Unity
  Build Profile rule with CI resolving the profile from
  `-activeBuildProfile` (`-forgeProfile` removed), and the
  `BUILD_PROFILE_<NAME>` scripting define kept in each Build Profile.
  All of it shipped in the CHANGELOG's `[0.9.0]` section, the first internal
  release (2026-10-07, tagged `v0.9.0` on a `main` with no earlier history);
  later changes go under `[Unreleased]`.
- `Tools~/run-tests.sh|ps1` run the suite via the Unity CLI against a
  generated host project — validated end-to-end on Windows (2026-07-31).
  Hard-won details: the host needs an `Assets/` folder or the editor fails
  with a misleading "Couldn't set project path"; paths handed to the CLI
  use forward slashes; the editor version defaults to the oldest installed
  6000.3.x (the support floor).
- The Unity CLI (2026, `unity` binary) composes with Build Forge — it is a
  launcher, not a build system. README's CI section documents the
  `unity run` invocation. Roadmap has a `[CliCommand]` integration entry
  (deferred: com.unity.pipeline is experimental).

## Remaining before 1.0 (rough order)

1. **Docs cluster** — deliberately deferred until after converting a real
   project (the migration experience should inform them):
   - README migration guide (from custom build scripts to Build Forge)
   - Plugin authoring guide (namespaces, asmdef setup, InternalsVisibleTo
     is NOT available to third parties — public API only)
   - CI arguments doc completeness pass
2. **Open decisions**:
   - `package.json` `documentationUrl`/`changelogUrl` are empty — fill when
     the repository is public, with the README and CHANGELOG under
     `https://github.com/CortopiaStudios/BuildForge/blob/main/Unity/`.
3. **CI** (agreed, after 1.0 structural work settles): GitHub Actions
   workflow reusing the `Tools~` scaffold recipe. License handled ONCE as a
   repo secret — either a `.ulf` (manual activation) or a Unity Cloud
   service-account key (`unity auth login --client-id/--client-secret`).
   Per-session sandbox activation was evaluated and rejected: ephemeral
   machines make it a recurring manual dance.

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
