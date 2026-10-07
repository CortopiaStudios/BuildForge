# Git Metadata

Order 300. Needs `git` on the PATH of the machine that builds.

Adds git information about the built commit to the [build manifest](../build-manifest.md). It changes nothing in the project. It has no per-profile switch and runs for every profile; to turn it off everywhere, use its toggle in **Project Settings > Build Forge > Installed Plugins**.

## Settings

| Field | Default | Manifest key | Source |
|---|---|---|---|
| Branch | On | `branch` | `git rev-parse --abbrev-ref HEAD` |
| Commit Hash | On | `commit` | `git rev-parse HEAD` |
| Short Commit Hash | On | `commitShort` | `git rev-parse --short HEAD` |
| Describe | Off | `describe` | `git describe --tags`, for example `v1.2.3-5-gabc1234` |
| Exact Tag | Off | `tag` | `git tag --points-at HEAD`; the first tag when HEAD has several |

The values go into the manifest section `Git`. A value git cannot give is left out without an error: Describe in a repository without tags, Exact Tag when HEAD is not tagged, everything outside a git repository or without git. With no value at all, the section is left out.

Git runs in the project's `Assets` folder, so the values describe the repository that contains the project, with a time limit of five seconds per command.

## On CI

- CI checkouts are often detached, and git then reports `HEAD` as the branch. If the manifest should name the branch, check out a branch in the job, or record the CI system's branch variable with a plugin of your own.
- Describe and Exact Tag need the tags in the clone; a shallow or tag-less fetch leaves them out.

Build window line: `manifest: branch, commit, short commit`, or `nothing selected for the manifest`.
