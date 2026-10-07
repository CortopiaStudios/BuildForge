# Troubleshooting and FAQ

## Building

### "Build Forge is configured to intercept builds"

A build was started outside Build Forge, for example with Unity's own Build button or a script calling `BuildPipeline.BuildPlayer`, while **Intercept Builds** is on. Build through the Build Forge window or the command line, or turn interception off in **Project Settings > Build Forge > Intercept Builds** while other build scripts must keep working.

### "Unity Build Profile '…' must be active before building"

Builds run on the active Unity Build Profile only. Activate the profile in the build window or the main toolbar, call `BuildProfile.SetActiveBuildProfile` in a script, or pass `-activeBuildProfile` on the command line.

### Build or Apply is disabled

Hover the button for the reason. The usual ones: the profile is not active (Build, Apply); it has nothing to apply, meaning no plugin with editor settings and no variants (Apply); the selected variant is not allowed for the profile; or the editor is in Play Mode, compiling, importing or building.

### The build stops before it starts

The message names the cause. The common ones:

| Message starts with | Page |
|---|---|
| Build Forge refuses to remove previous build output in … | The destination is unsafe to clean, for example it contains the project. Choose a dedicated folder; see [Remove Previous Build Output](settings.md#remove-previous-build-output). |
| Build Forge could not delete … | A file from the previous build is in use, usually because the player is still running. |
| Build Forge cannot build '…': a folder is in the way | An old Android export folder sits where the APK goes. |
| [Build Forge/BuildNumber] … | [Build Number](plugins/build-number.md#when-the-build-stops) |
| [Build Forge/AndroidKeystoreEnv] … | [Android Keystore Env](plugins/android-keystore-env.md#what-a-build-does) |
| OpenXR feature '…' did not accept its configured override, or OpenXR kept '…' enabled | [OpenXR](plugins/openxr.md#builds-and-apply) |
| [Build Forge/Addressables] … | [Addressables Stripper](plugins/addressables-stripper.md) |
| [Build Forge/Android Manifest] … | [Android Manifest](plugins/android-manifest.md#settings) |
| Could not set XR loaders, or an XR loader warning | [XR Loaders](plugins/xr-loaders.md#warnings) |
| A variant or variant rule error | [Build variants](variants.md) |

### The log has no pre-build lines

The Console's **Clear on Build** option clears the Console when the player build starts, so the plugins' pre-build lines vanish. Turn it off in the Console's toolbar, or read the editor log.

### "post-build restore/cleanup step(s) failed"

The player was built, but restoring a setting or cleaning up failed. The errors above that line name the step. The project may have been left modified: check the changed files before committing. A command-line build fails in this case.

### The editor crashed during a build

Restore the files the build changed from version control, typically `ProjectSettings/`, the XR settings under `Assets/XR/` and the Build Profile that was built. If a profile was applied to the editor, **Revert to Baseline** restores its plugin settings, because their baselines are on disk.

## Command line and CI

### Failed to read the specified build profile

A batch-mode start fails with `Failed to read the specified build profile '<path>'` and exit code 1, before any `-executeMethod` runs. Unity records the active Build Profile by its path in `Library/`; when that asset was renamed or moved outside the editor, for example by a pulled commit, the recorded path no longer exists. Pass `-activeBuildProfile` with an existing profile on that start, as the generated CI command always does. (Verified on Unity 6000.6.3f1.) `BuildForge.CommandLine.Activate` starts without it and fails the same way: pass `-activeBuildProfile` with the moved profile's new path as well as `-forgeBuildProfile`. (Verified on Unity 6000.3.23f1.)

### "Scripts have compiler errors" after a platform change

A batch-mode start with `-activeBuildProfile` fails with compile errors, such as `The type or namespace name '…' could not be found`, before Build Forge runs, while the same command works in a workspace that is already on the profile's platform. Unity 6000.3.23 compiled the new profile's scripting defines for the previous platform, where an assembly that code needs is missing. Run `BuildForge.CommandLine.Activate` with `-forgeBuildProfile` first; see [Changing platform](ci.md#changing-platform).

### "No Unity Build Profile is active"

The command line has no `-activeBuildProfile`, and Unity was on a platform profile. Add the argument; copy the command from the build window's **CI Command** field.

### "Unity Build Profile '…' has no Build Forge settings"

The Build Profile given to `-activeBuildProfile` has no Build Forge settings. Add them, and check that the Build Profile asset with its Build Forge section is committed.

### "Unknown argument", "needs a value", "Unknown build variant"

Build Forge checks its arguments strictly; see [Build Forge's arguments](ci.md#build-forges-arguments). An argument that needs a value and gets none is usually an unset CI variable. Variant names must match exactly, including case; `Default` is the default build.

### The job fails with exit code 6

The Unity CLI reports any failed editor run as 6. The editor log above names the real error, and the CLI's message names the editor's own exit code.

### "already open" after a cancelled or crashed job

An editor from the previous run is still running for that project. Find it by its process and project path and stop it, with the build processes it started, before retrying. Do not kill every Unity process by name or delete the project's lock file while that editor is alive. See [Timeouts, cancellation and one build per checkout](ci.md#timeouts-cancellation-and-one-build-per-checkout).

### The editor does not exit after "Build process complete"

Seen once, after a first build that included a complete Android reimport: Unity hung in its own shutdown after Build Forge had finished. Stop that editor process; later builds exited normally.

### The CLI uses another editor version

`UNITY_EDITOR_VERSION` in the job's environment overrides the version in `ProjectSettings/ProjectVersion.txt`. Clear it in the job.

### A local build script gets CI behavior

Batch mode counts as CI. Pass `-forgeCI false`; see [CI detection](ci.md#ci-detection).

## Editor state

### "Unity switched to '…' without applying its Build Forge settings"

Unity changed the active profile without Build Forge, and the previous profile's plugin settings are still applied. Apply the active profile, activate the applied one again, or Revert to Baseline. See [When Unity switches profiles on its own](concepts.md#when-unity-switches-profiles-on-its-own).

### Drift is reported

An applied setting changed after the Apply. Re-apply, Revert to Baseline, or Dismiss Drift when the change is legitimate. See [Drift](concepts.md#drift).

### "Build Forge could not restore some settings"

A Revert to Baseline could not restore everything. If the cause can be fixed, for example by reinstalling a removed package, choose **Keep Tracking** and revert again. Otherwise **Stop Tracking**: those settings keep their current values. See [Revert to Baseline](concepts.md#revert-to-baseline).

### A `BUILD_VARIANT_` define appeared in a Build Profile

The profile was saved while a variant was applied on another machine, usually committed by accident. Builds recompute the variant defines, so they are not affected, and Activate removes the define here. Revert to Baseline on the machine that applied it before committing the Build Profile again.

### "Scripting define '…' is also the define of Unity Build Profile '…'"

Two profile names give the same define, for example "Quest 3" and "Quest-3". Rename one so the names differ in letters or digits.

### Editor settings changed after Apply and show up in version control

Applying writes version-controlled assets. Revert to Baseline before committing them; see [Commit settings while nothing is applied](concepts.md#commit-settings-while-nothing-is-applied).

## Windows and toolbar

### No Build Forge section in the Build Profiles window

- A platform profile is selected: those cannot hold Build Forge settings.
- The Build Profile asset is outside `Assets/`.
- The section is inserted into Unity's own UI, which a future Unity version may change. **Assets > Build Forge > Edit Build Forge Settings** and the build window still work.

### A new profile is missing from the build window

Click **Refresh** in the window's toolbar after adding Build Forge settings while it is open.

### The main toolbar element is missing

It may have been hidden: open the toolbar's ⋮ menu and choose **Build Forge**. Hold Ctrl and drag to move it. Unity 6.6 cuts toolbar labels at 120 pixels; hover for the full label.

### The Player Overrides window shows old values

It reads the files on disk. Save the project after changing Project Settings and click **Refresh**.

## OpenXR

| Symptom | See |
|---|---|
| An OpenXR settings override does not reach the player | [Settings that a feature controls](plugins/openxr.md#settings-that-a-feature-controls) |
| A Disable override is undone, or the build stops naming a feature group | [Feature groups](plugins/openxr.md#feature-groups-are-not-supported) |
| Meta controller profiles turn off after leaving a Meta Quest profile | [Meta Quest Build Profiles](plugins/openxr.md#meta-quest-build-profiles) |
| A vendor SDK stops working after Disable overrides | [OpenXR tips](plugins/openxr.md#tips) |
| A Steam Frame or Pico APK carries Quest entries or another vendor's libraries | [XR Vendor Filter](plugins/xr-vendor-filter.md) |
| The build stops with "excluded vendor items are still in the Gradle project" | [XR Vendor Filter: leftovers](plugins/xr-vendor-filter.md#how-it-works) |

## Other packages and tools

- **Packages that rewrite files on every editor open or build** show up as changes after a Build Forge build too. Build Forge restores only what it and its plugins change; review such diffs and revert what nobody meant to change.
- **The editor crashes at startup** in a project with Meta's OVRPlugin while a headset is connected and an adb server is running: run `adb kill-server` before starting Unity.
- **`Start-Process -Wait` does not return** in a PowerShell build script: it waits for processes the build leaves running. Use `Start-Process -PassThru` and `WaitForExit()`.
- **A git package fails to resolve**: see [Installation problems](installation.md#installation-problems).

## FAQ

**Does Build Forge override Player Settings?**
No. A profile that needs different Player Settings uses the Build Profile's own Player Settings. Build Forge shows the differences in the [Player Overrides window](player-overrides.md). Some plugins change single values for the duration of a build, such as the version code or the signing, and restore them afterwards.

**Can one Unity Build Profile have two sets of plugin settings?**
No. Each Build Profile has at most one Build Forge section. Two configurations need two Build Profiles, or one profile with [variants](variants.md) when the difference is a variant's.

**Can Build Forge build several profiles in one go?**
Not in one Unity process: switching profiles between builds reloads scripts and loses the build's state. Run one command per profile and variant; see [Building several profiles](ci.md#building-several-profiles).

**Does a variant build install next to the store build?**
Not by default: Build Forge marks the product name but keeps the application identifier. The **Example Integration** sample has a plugin that adds a suffix to the Android application identifier for chosen variants.

**Why does the toolbar say "Platform profile"?**
Unity is on one of its platform profiles, not on a Build Profile asset. Pick a profile in the dropdown to activate it.

**Do I need the XR Loaders plugin?**
Only when profiles of the same platform need different XR providers, or one of them must run without XR.

**What do I commit?**
The Build Profile assets, `ProjectSettings/ForgeSettings.asset`, and `Packages/manifest.json` with `Packages/packages-lock.json`, while nothing is applied. Not `UserSettings/` or `Library/`.

**What happens to the settings if I remove the package?**
They stay in the Build Profile assets, and come back when the package returns. Revert to Baseline before removing it; see [Removing the package](installation.md#removing-the-package).

**Does Build Forge work on macOS and Linux?**
The package's tests and real builds have been run on Windows editors only. The command line and the scripts are written for all three systems, but macOS and Linux have not been verified.

**Which Unity CLI version should I use?**
Build Forge was checked with 1.0.0-beta.9 and 1.0.0-beta.11. Pin the version you verified on your agents.
