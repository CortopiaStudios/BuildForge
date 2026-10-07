# Working Guidelines

## Respect the user's time
- The user's time is the most valuable asset. Don't create work for the user to clean up.
- Ask before refactoring, consolidating, or restructuring code beyond what was explicitly requested.
- Don't introduce new dependencies or architectural changes without discussing the tradeoffs first.
- When something goes wrong, fix it — don't ask the user to verify your fix.
- When there are multiple valid options for naming, design, or approach, present the options and let the user choose — don't pick one and implement it.

## Verify before claiming
- Don't claim a third-party library or tool will solve a problem without first verifying it works with the actual inputs and environment.
- Test with realistic data, not just toy examples. Unit tests passing does not mean the feature works in practice.
- If you're unsure whether something will work, say so upfront rather than presenting it as a solution.

## Keep changes minimal and focused
- Do what was asked. Don't add extras, don't "improve" adjacent code, don't consolidate things that weren't asked to be consolidated.
- If you notice something that could be improved, mention it — don't just do it.
- When a significantly better approach exists, don't be afraid to experiment — git history preserves the working implementation as a fallback.

# Project

## Layout
- `Unity/`: the Build Forge Unity package (`com.cortopiastudios.buildforge`), with its own README, ARCHITECTURE, CHANGELOG, the user manual in `Documentation~/` and notes for Build Forge's developers in `Development~/`.
- `Server/`: the planned CI server; not started.

## Build system (Unity/)
- Unity 6000.3, C# 9 language level (no C# 10+ features)
- This is a UPM package, not a Unity project. Tests run in a host Unity project that references this package.

## Test commands (Unity/)
- On a machine with a licensed Unity editor: `Unity/Tools~/run-tests.sh` (or `.ps1` on Windows) — generates a disposable host project and runs the EditMode suite via the Unity CLI. Expect 2 conditional skips (`PlayerSettingsProfileTests`) in the clean host on Windows; on macOS and Linux the Windows-only `BuildOutputDirectoryTests` cases skip too.
- In environments without an editor/license (e.g. cloud containers), tests cannot run; the user runs them.

## Release work
- Pre-1.0 backlog and release sequence: see `Unity/Development~/RELEASE-STATUS.md`.
