# Installation

Build Forge is a UPM package. Its source is the `Unity` folder of the repository, so the git URL names that folder with `?path=/Unity`.

## Requirements

- Unity 6000.3 or later.
- Git on the PATH of every machine that opens the project, build agents included. Unity fetches git packages with the machine's own Git.
- The package depends on two built-in modules, JSON Serialize (the build manifest) and UnityWebRequest (reading the manifest on Android and WebGL). The Package Manager enables them in a project that had them disabled.

## Install from the git URL

In the Package Manager, open the **+** menu, choose to install a package from a git URL <!-- unverified: the exact menu label differs between Unity versions -->, and enter:

```
https://github.com/CortopiaStudios/BuildForge.git?path=/Unity
```

Or add the line to `Packages/manifest.json` yourself:

```json
{
  "dependencies": {
    "com.cortopiastudios.buildforge": "https://github.com/CortopiaStudios/BuildForge.git?path=/Unity#<tag or commit>"
  }
}
```

## Pin a version

Append `#` and a tag, a branch or a commit hash to the URL. Without a revision Unity uses the repository's default branch.

Unity records the commit it resolved in `Packages/packages-lock.json` (the `hash` of the package's entry) and keeps building that commit, also when the manifest names a branch that has moved on. Commit `packages-lock.json` with the manifest, so every machine and CI build the same Build Forge commit.

- To move to another tag or commit, change the revision in `manifest.json`.
- To take the newest commit of a branch you track (for example `#main`), delete the `com.cortopiastudios.buildforge` entry from `packages-lock.json`, open the project so Unity resolves the branch again, and commit the updated lock file. A batch-mode open is enough for that.

Tracking a branch and refreshing the lock deliberately suits a project that is developed together with Build Forge; a project that ships should pin a tag or commit.

## Other ways to install

- **Embedded copy**: copy the `Unity` folder into the project as `Packages/com.cortopiastudios.buildforge/`. The copy is then part of your repository and is updated by hand.
- **Local working copy**: while you work on Build Forge itself, point the manifest at a checkout with a `file:` reference, for example `"com.cortopiastudios.buildforge": "file:../../BuildForge/Unity"`. <!-- unverified: a relative file: path is resolved from the project's Packages folder --> Commit such a manifest only to a branch that every machine can resolve.

## Private repositories

If the repository is private, Unity cannot ask for credentials: the fetch fails unless Git already has them.

- Every machine needs a stored login for an account that can read the repository, for example in Git Credential Manager. Build agents need it for the account the job runs as; the credentials Jenkins or another CI service uses for its own checkout are not used by Unity.
- A machine with several logins for the same host needs to know which one to use, for example `git config --global credential.https://github.com.username <account>`. Otherwise the fetch fails.

## Optional packages and their plugins

Four bundled plugins live in assemblies that compile only when their Unity package is installed:

| Plugin | Needs |
|---|---|
| XR Loaders | `com.unity.xr.management` 4.0.0 or later |
| OpenXR | `com.unity.xr.openxr` (which brings XR Plug-in Management) |
| Addressables Stripper, Addressables Rebuild | `com.unity.addressables`, with Addressables settings created in the project |

No scripting defines are needed. While a package is missing, **Project Settings > Build Forge** lists its plugins under **Available Plugins** with an **Install** button, which adds the package through the Package Manager.

## What changes when the package is installed

- **Builds outside Build Forge are blocked.** Build interception is on by default: Unity's own Build buttons and scripts that call `BuildPipeline.BuildPlayer` fail with a message pointing to the Build Forge window. Turn it off in **Project Settings > Build Forge > Intercept Builds** while existing build scripts must keep working, for example during a migration.
- A **Build Forge** element appears in Unity's main toolbar. Hide it like any toolbar element; see [Build window and main toolbar](build-window.md#main-toolbar).
- Nothing else changes until you add Build Forge settings to a Build Profile. Build Forge's project settings are written to `ProjectSettings/ForgeSettings.asset` when you first change one; commit that file.

## Updating

Change the revision, or refresh the lock entry as described above, and read the package's `CHANGELOG.md` for the version you move to. Commit the updated `packages-lock.json` (and `manifest.json` if it changed) so every machine follows.

## Removing the package

1. If a profile is applied to the editor, click **Revert to Baseline** in the build window first. Otherwise its plugin settings (OpenXR features, the XR loader list, variant defines) stay as applied, and nothing is left that knows their baseline.
2. Remove the package from the Package Manager.

The Build Forge settings stay inside the Build Profile assets. Unity keeps that data while the package is gone, and the Build Profiles still load and build; adding the package again restores the settings. The `BUILD_PROFILE_<NAME>` defines stay in the Build Profiles' Scripting Defines lists; remove them by hand once no code uses them. `ProjectSettings/ForgeSettings.asset` and the `UserSettings/Forge*.asset` files can be deleted.

## Installation problems

- **The fetch fails with an authentication error**: see [Private repositories](#private-repositories).
- **The fetch fails with "unable to write file" under `.git/objects`** on Windows: the project path is probably too long for Git's temporary files. A project in a shorter folder resolved the package in that case.
- **Every machine builds an old Build Forge commit although the manifest says `#main`**: that is the lock file at work. Refresh the lock entry as described in [Pin a version](#pin-a-version).
