# Build Number

Order 400. Sets the platform build number for the build and restores the previous values afterwards:

- Android: the version code (`PlayerSettings.Android.bundleVersionCode`);
- iOS: the build number (`PlayerSettings.iOS.buildNumber`).

Both are set for every profile the plugin is enabled for. The version string (`bundleVersion`, Player Settings > Version) is not touched; a variant rule's [Version](../variants.md#version) can include the number. Other platforms have no such setting; there the number appears in the manifest.

## Settings

| Field | Meaning |
|---|---|
| Enabled | Off by default. |
| Source | **Git Commit Count**: `git rev-list --count HEAD`. **Environment Variable**: the value of the variable below. **Env Var on CI, Git Count Locally**: the variable for CI builds, the commit count for local builds. |
| Env Var Name | For the variable sources. Default `BUILD_NUMBER`, which Jenkins sets for every build. |
| Offset | Added to the base number. |

While the plugin is enabled, the foldout previews the number, for example `Current: 434 (base: 434)`. With **Env Var on CI, Git Count Locally** it shows both cases, the side this editor would use first: `Local build: 434 (base: 434) · CI build: $BUILD_NUMBER`. Which case applies follows [CI detection](../ci.md#ci-detection): batch mode counts as CI unless `-forgeCI false` or `FORGE_CI=0` says otherwise.

## When the build stops

The plugin is an explicit opt-in, so a build without the configured number fails instead of shipping a wrong one. It stops before any Player Setting changes when:

- the source is the commit count and the repository is a shallow clone, where the count is the fetch depth, not the history. Fetch the full history (`git fetch --unshallow`; in GitHub Actions, `actions/checkout` with `fetch-depth: 0`) or use the variable;
- git cannot give the count, for example outside a repository;
- the variable is missing, empty, or not a whole number of 0 or more. The build window warns about this beforehand;
- the base plus the offset is outside 0 to 2147483647. The preview shows this too.

## Notes

- Store version codes only grow. When the store already has a higher version code than your counter gives, set **Offset** so new builds start above it.
- With `-forgeVersionCode` on the command line, a profile with Build Number enabled builds with the plugin's number: the plugin runs after the command line has set the version code, and restores that value afterwards.
- The commit count depends on the history of the branch you build, so two branches can produce the same number.
- A local scripted build runs in batch mode and counts as CI. A wrapper script that should use the local source passes `-forgeCI false`.
- To put the number into the version string as well, for example `1.4.0.412`, give the variant rule the Version `{Version}.{BuildNumber}`. See [Version](../variants.md#version).

## Manifest and build window

- Manifest section `BuildNumber`, key `buildNumber`. The manifest's own `BuildNumber` field has the same number, on every platform.
- Build window line: `build number from git commit count + 20`, `build number from $BUILD_NUMBER`, or, for the mixed source, the same with `(local build)` or `(CI build)`.
