# Integrating Build Forge into an existing project

This guide comes from moving several existing Unity projects to Build Forge: VR and desktop titles for Android headsets, PC and console, each with its own build scripts, per-platform switching code and Jenkins jobs. It lists the steps in the order that worked, then the pitfalls those projects ran into.

## 1. Take stock of the current pipeline

Write down what the current build scripts do for each target, then decide where each piece goes:

| In the current pipeline | With Build Forge |
|---|---|
| A menu item or CI method per target that switches platform, sets defines and settings, then builds | One Unity Build Profile per target, with Build Forge settings; the build window or the generated CI command builds it |
| Scripting defines per target, set in Player Settings | The Build Profile's Scripting Defines list, or the `BUILD_PROFILE_<NAME>` define Build Forge keeps there |
| Debug and release switches: development build and its options, debug defines, IL2CPP, stripping and compression settings, Android debug symbols | [Variants](variants.md) with rules |
| A version string with the build number | The variant rules' [Version](variants.md#version) |
| An XR provider per target | XR Plug-in Management, when every profile of a platform uses the same providers; [XR Loaders](plugins/xr-loaders.md) when they differ |
| OpenXR feature lists per target | [OpenXR](plugins/openxr.md) per profile: Only Listed Features for a list of the target's features, or Enable and Disable overrides |
| The Android version code from the CI build number | [Build Number](plugins/build-number.md) |
| Release signing from CI secrets | [Android Keystore Env](plugins/android-keystore-env.md) |
| An Addressables content build before the player build | [Addressables Rebuild](plugins/addressables-rebuild.md), or Addressables' own build-with-player setting |
| Player Settings that differ per target, such as the minimum API level, split binaries or window settings | The Build Profile's own Player Settings |
| A per-target Android manifest | [Android Manifest](plugins/android-manifest.md) |
| Other headset vendors' SDKs out of a target's APK: their libraries and the manifest entries their Gradle steps write | [XR Vendor Filter](plugins/xr-vendor-filter.md) |
| Anything else, such as content baking | A [plugin](custom-plugins.md) of your own, or a Unity build callback that reads the profile's Build Forge settings |

Things Build Forge does not do: it does not override Player Settings, does not build several profiles in one Unity process, does not switch packages per profile, and does not support OpenXR feature groups. Variant rules set the build settings their [Build Configuration](variants.md#build-configuration) lists; other per-variant settings need a plugin or separate Build Profiles.

## 2. Install the package and keep the old pipeline working

1. [Install](installation.md) the package from its git URL, pinned to a tag or commit, or tracking a branch with the lock file committed. Commit `Packages/manifest.json` and `Packages/packages-lock.json`.
2. If the repository is private, give every machine that opens the project, build agents included, Git credentials that can read it.
3. Turn off **Project Settings > Build Forge > Intercept Builds** while old build entry points still have to work. Otherwise every build they start fails.

## 3. Create the Unity Build Profiles

Create one Unity Build Profile per target configuration in Unity's Build Profiles window. Unity has no public API for creating Build Profile assets, so this step is done by hand.

- **Name them as they should appear.** The name becomes the `BUILD_PROFILE_` define, a folder in the output path, the build window's row and the manifest's `BuildProfileName`. Renaming later is possible and Build Forge follows, but code that uses the old define does not.
- **Profiles may share a platform.** Headsets that are all Android get one Android Build Profile each.
- **Decide on the Quest profile type.** Unity's Meta Quest Build Profile type and a plain Android profile both work; the Meta Quest type changes shared Android OpenXR features in the editor. See [Meta Quest Build Profiles](plugins/openxr.md#meta-quest-build-profiles).
- **Set scenes and defines on each profile.** Move defines that belong to one target out of Player Settings into that profile's Scripting Defines list: a define in the Android Player Settings reaches every Android profile that has no Player Settings of its own.
- **Player Settings that must differ** go into the Build Profile's own Player Settings. That makes a full copy of Project Settings > Player for the profile, so later changes in Project Settings, such as a version bump, have to be made in the copy as well. The [Player Overrides window](player-overrides.md) in Matrix mode shows such stale values.

## 4. Add Build Forge settings and project-wide configuration

1. Select all the new Build Profile assets and use **Assets > Build Forge > Add Build Forge Settings**.
2. In **Project Settings > Build Forge**:
   - add the variants and their rules, for example `Default` with Development Build **Disabled** and `Internal` with Development Build **Enabled** and the debug defines the old pipeline set;
   - set the Android Keystore Env variable names to the names your CI already provides, if they differ from the defaults;
   - turn on OpenXR's **Interaction Profiles** only if controller profiles really must differ per profile.
3. Move debug defines that the old pipeline added for internal builds into the variant rules, and remove them from Player Settings, so they do not reach the default build.

## 5. Configure each profile's plugins

- **OpenXR**: where the old code turns on exactly a list of features per target, turn on **Only Listed Features** and tick that list; hidden features are left to their packages. Otherwise translate what the target changes into Enable and Disable overrides, and for profiles that share a platform give every feature that any of them enables an explicit state in each of them, so no build depends on the committed OpenXR settings. Leave features you do not recognize at No Override.
- **XR Loaders**: only where profiles of one platform need different providers or no XR.
- **Build Number**: pick the source. **Env Var on CI, Git Count Locally** gives CI builds the CI's counter and local builds the commit count. If the store already has a higher version code, set Offset above it.
- **Android Keystore Env**: enable it on the Android profiles that CI signs for release. Keep the key alias in Player Settings unless CI provides it.
- **XR Vendor Filter**: on each Android profile, exclude the XR vendor packages its headsets do not use, for example Meta's and Pico's on a Steam Frame profile. It replaces build post-processing that removed their libraries or manifest entries.
- **Android Manifest**: on the profiles that had a main manifest of their own. It need not remove other vendors' entries when the XR Vendor Filter does.
- **Addressables Rebuild**: enable it if the old pipeline built content before the player and Addressables' build-with-player setting is off.
- **Cloud Diagnostics**: enable it with **CI only** to keep symbol uploads and crash reports to CI builds.

## 6. Update project code

- Code that looked for build settings by searching assets: Build Forge settings are a component of the Build Profile asset. Use `BuildProfile.GetActiveBuildProfile()` and `GetComponent<ForgeProfile>()`; `AssetDatabase.FindAssets("t:ForgeProfile")` finds nothing. One project's Android post-processing silently stopped working because of this, and its first build kept vendor entries it should have removed.
- Code that branched on the active platform to tell targets apart: use the `BUILD_PROFILE_` defines in player code, and `ForgeBuildContext` or the active Build Profile in editor code.
- Code that relied on `DEVELOPMENT_BUILD` in the editor: the editor never defines it. Use a variant define or a rule's define.
- Custom build steps from the old scripts: move them into a plugin with an order that places them correctly, for example above 400 to read the build number.

## 7. Build locally and compare

Build every profile and variant from the build window and compare each with the old pipeline's output before switching CI:

- the version code, the package name, the signing and the merged Android manifest;
- the file list and the size;
- the OpenXR features: the manifest's `effectiveEnabledFeatures`;
- the defines the player was compiled with, through a feature that depends on them;
- a run on the device.

In the projects this guide comes from, the first builds found missing per-target Player Settings, a feature disabled that a vendor SDK needed, and a profile whose OpenXR validation failed because of a project-wide minimum API level.

## 8. Script local builds

Developers who build from scripts call the same entry point in batch mode, which counts as CI. Pass `-forgeCI false` so plugins such as Build Number treat the build as local. The **Example Integration** sample has PowerShell and bash wrappers.

## 9. Move CI to the generated command

Copy each profile's command from the build window's **CI Command** field: **One workspace per job**, or **Shared workspace** when one workspace builds profiles of several platforms. See [Command line and CI](ci.md) for the details; in short:

- run it from the repository root, one Unity process per workspace;
- provide `BUILD_NUMBER` and the signing variables from the CI secret store;
- check out the full history when a profile uses the git commit count;
- clean the output folder, or keep the workspace, before each build;
- treat every nonzero exit as a failure and archive only after 0;
- check `unity --version` on the agent and clear `UNITY_EDITOR_VERSION`.

Keep the old job until the new one has produced artifacts that pass the comparison of step 7.

## 10. Finish

1. Remove the old build entry points, then turn **Intercept Builds** back on.
2. Commit the Build Profile assets, `ProjectSettings/ForgeSettings.asset` and the package files, with nothing applied in the editor. Do not commit `UserSettings/` or `Library/`.

## Pitfalls seen in real integrations

### Unity

- **A Meta Quest Build Profile changes shared Android features.** With OpenXR 1.18, moving a project from the Meta Quest type to a plain Android profile turned the Meta controller profiles off at the next editor load, which would have shipped a build without controller bindings. Set `isMetaQuestInitialized: 0` in `ProjectSettings/BuildProfileUtilityOpenXR.asset` and commit it; Build Forge warns while it is 1.
- **`-buildTarget` does not leave an active Build Profile of the same platform.** With a custom Android profile active, `-buildTarget Android` keeps it. Use `-activeBuildProfile` to choose a Build Profile, and Unity's Build Profiles window to go back to a platform profile.
- **A Build Profile renamed outside the editor breaks the next batch start** on machines whose `Library/` still names the old path; see [Troubleshooting](troubleshooting.md#failed-to-read-the-specified-build-profile).
- **A Build Profile's own Player Settings are a copy.** A version bump in Project Settings does not reach it.
- **A fresh clone imports for the editor's default platform first.** Open it once with `-activeBuildProfile`.

### OpenXR

- **Settings a feature controls are overwritten during the build.** An override of symmetric projection in the OpenXR settings never reached a Quest player, because Unity's Meta Quest build step copies the value from the Meta Quest feature. Set it in the feature's own settings; Build Forge warns about the other form.
- **Symmetric projection and the original Quest exclude each other.** With symmetric projection on, OpenXR's validation fails the build while the Meta Quest feature targets the original Quest.
- **Disabling every feature a target does not list can disable one a vendor SDK needs internally.** Leave unknown features at No Override.
- **Ticked feature groups refuse Disable overrides.** Untick them and manage features per profile.

### CI and the command line

- **Batch mode counts as CI.** A local build script runs in batch mode, so without `-forgeCI false` it gets the CI side of every CI-dependent setting: a profile whose build number comes from the variable on CI then fails on a machine without `BUILD_NUMBER`.
- **Shallow clones break the commit count.** Build Forge fails the build rather than using the fetch depth as the build number.
- **Nonzero is failure.** The Unity CLI reports a failed build as 6, not 1.
- **A cancelled job can leave the editor running** on Windows with CLI beta.9 and beta.10, and the next build then finds the project already open.
- **The editor can hang after a build.** Once, after the first build with a complete Android reimport, Unity logged `[Build Forge] Build process complete.` and then did not exit; the next `unity run` refused the project as already open, while later builds exited normally. Stop that editor process, identified by its project path, before retrying.
- **`Start-Process -Wait` hangs** in PowerShell when the build leaves processes running; use `-PassThru` and `WaitForExit()`.
- **Git packages need Git credentials on every machine**, and a machine with several accounts for the same host needs to know which one to use.

### Build Forge settings

- **Commit while nothing is applied.** An applied variant or OpenXR state committed by accident becomes every other machine's baseline; Build Forge reports variant defines saved that way.
- **Keep `{Variant}` in custom output paths**, or a variant build overwrites the default build.
- **Re-check your plugins' orders when updating Build Forge.** Before 1.0 the bundled orders were renumbered once, and a project plugin at 350 had to move to 650 to stay between the same plugins.
- **Do not list a variant rule's defines in a Build Profile as well**; the variant system owns them and removes them for other variants.

### Other packages

- **Every installed XR vendor SDK reaches every Android build.** In two projects, the Steam Frame builds carried other vendors' platform loaders and a dozen of their manifest entries: Meta's SDKs write their entries into every Android build's manifest. Exclude those packages on the profile with [XR Vendor Filter](plugins/xr-vendor-filter.md).
- **Some packages rewrite files whenever the editor opens or builds**: fonts, shader tool defines, XR migration flags, render pipeline assets. Review the diff after the first builds, revert what nobody meant to change, and keep it apart from Build Forge's changes. Build Forge restores only what it and its plugins change.
- **A failed build can leave other packages' build-time changes behind**, for example library switches an audio middleware writes into `.meta` files. Revert them.
- **An adb server left running while a headset is connected crashed the editor at startup** in a project with Meta's OVRPlugin. Run `adb kill-server` before starting Unity.
- **Split application binaries need their OBB pushed after the first launch** when a test build is installed next to the store build: on the headset tested, adb could create the app's OBB folder only after the app had run once.
