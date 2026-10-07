# Concepts

## A Unity Build Profile and its Build Forge settings

A **Unity Build Profile** is Unity's asset for one way of building the project: its platform, scenes, scripting defines and build options, and optionally its own Player, Quality and Graphics Settings. Unity applies those whenever the profile is active, in the editor and in builds.

**Build Forge settings** extend one Unity Build Profile. They are stored inside the Build Profile's `.asset` file, as a Build Profile component (Unity 6.3's component API) that appears in the file as a YAML document of its own named `Build Forge`. They hold:

- the output path and the Remove Previous Build Output option;
- which variants the profile allows;
- the configuration of each plugin for this profile, one readable YAML block per plugin.

Consequences of storing them in the Build Profile:

- **One per Build Profile.** Two configurations that differ only in plugin settings, for example two OpenXR feature sets for the same platform, need two Unity Build Profiles.
- **One name.** Everything user-facing uses the Unity Build Profile's name: the build window, the `{ProfileName}` output path placeholder, the `BUILD_PROFILE_<NAME>` define and the manifest's `BuildProfileName`.
- **They travel with the asset.** Renaming, duplicating or deleting the Build Profile in the editor carries the Build Forge settings; a duplicate gets its own independent copy.
- **Only Build Profile assets in `Assets/`** can hold them. Unity's platform profiles live in `Library/`, which is not committed.

The Build Forge settings are edited in the **Build Forge** section of the Build Profile editor, in Unity's Build Profiles window and in the Inspector. See [Profiles and settings](settings.md).

## What is stored where

| Location | Holds | Version control |
|---|---|---|
| The Unity Build Profile asset | Unity's settings for the profile, and its Build Forge settings | Commit |
| `ProjectSettings/ForgeSettings.asset` | Project-wide Build Forge settings: variants and their rules, toggles, which plugins are enabled, plugins' global settings | Commit |
| `UserSettings/ForgeEditorState.asset` | Which profile is applied here, and the baseline of every setting Apply changed | Per user, do not commit |
| `UserSettings/ForgeVariantDefines.asset` | What Apply changed in a Build Profile's defines, so it can be undone exactly | Per user, do not commit |
| `UserSettings/ForgeBuildOutputs.asset` | The files each profile's and variant's last build produced, for Remove Previous Build Output | Per user, do not commit |
| `UserSettings/ForgeUserPreferences.asset` | Whether the main toolbar element is shown | Per user, do not commit |
| `Library/BuildForge/Manifest/` | The manifest of the build in progress, removed afterwards | Not committed |

`Library/` and `UserSettings/` are in Unity's standard `.gitignore`.

## Builds

Build Forge builds one profile per Unity process, and only the **active** Unity Build Profile: Unity's static Player Settings API reads and writes the active profile's settings, so building another profile would let its own Player Settings silently win over plugin changes. The build window enables Build for the active profile only; the command line selects the profile with `-activeBuildProfile`.

A build runs these steps:

1. Checks that the variant is valid for the profile and that the profile is active.
2. With **Remove Previous Build Output** on, removes the previous build's files from the destination.
3. Marks a variant build's product name, for example `My Game (Internal)`.
4. If a profile is applied to the editor, restores its baseline, so the build starts from the project's own settings.
5. Puts the variant's development flags and defines on the Build Profile.
6. Runs each plugin's pre-build step, in plugin order.
7. Writes the build manifest, with every plugin's contribution.
8. Builds through `BuildPipeline.BuildPlayer` with the Build Profile, so Unity applies its scenes, defines, options and Player Settings.
9. In a `finally` block: runs each plugin's post-build step in reverse order, undoes the variant and the product name, records the build's output files, applies the applied profile again, saves the restored settings to disk and removes the temporary manifest.

Step 9 runs also when the build fails or throws: Build Forge undoes its own changes, and every plugin whose pre-build step started gets its post-build step to undo its own. The bundled plugins restore everything they change. Unity's own `IPostprocessBuildWithReport` callbacks do not run on a failed build; Build Forge's restore does. A failed restore is logged as an error and makes a command-line build fail, even when the player itself was built.

An editor crash or a killed editor process cannot run step 9. Restore the changed files from version control afterwards, typically `ProjectSettings/`, the XR settings under `Assets/XR/` and the Build Profile that was built. If a profile was applied at the time, **Revert to Baseline** restores the applied plugin settings, because their baselines are on disk.

## Active and applied

Two states, kept apart on purpose:

- **Active** is Unity's: the Build Profile Unity currently uses. The build window tags it **Active**.
- **Applied** is Build Forge's: the profile whose plugin settings are written to the editor right now. The build window tags it **Applied** and the main toolbar's apply dropdown names it.

Build Forge's Activate keeps the two consistent. Unity can change the active profile on its own, and Build Forge then warns; see [When Unity switches profiles on its own](#when-unity-switches-profiles-on-its-own).

## Activate

**Activate** makes a profile's Unity Build Profile the active one, like Unity's own Switch Profile. Before that, it reverts whatever another profile applied to the editor, so nothing stale stays behind. Activating the profile that is still applied keeps its settings.

Activate also writes the profile's `BUILD_PROFILE_<NAME>` define and, unless the profile is the applied one, removes variant defines from it, including ones saved on another machine. If the profile targets another platform, Unity switches platform and reimports assets; Build Forge logs what it did once the editor has reloaded.

Activate does not apply the profile's plugin settings. Play Mode then runs with the project's own settings plus the Build Profile's Unity settings.

## Apply and Re-apply

**Apply** writes the active profile's plugin settings to the editor, so Play Mode matches what a build of the profile gets:

- the settings of plugins that support it: among the bundled plugins, the [OpenXR](plugins/openxr.md) features and settings and the [XR Loaders](plugins/xr-loaders.md) list;
- the selected variant's define and the extra defines its rule lists, on the Build Profile. A variant's development flags and Build Configuration are not applied: they only matter to builds, the editor never compiles with `DEVELOPMENT_BUILD` anyway, and on a committed Build Profile they would turn other machines' default builds into development builds;
- for a profile of another platform than Standalone, with **Play Mode Follows Applied Profile** on (the default): Play Mode's XR. Play Mode runs on Standalone's XR settings whatever the active platform, so Apply copies the profile's platform's XR state there after the plugins have run: the XR Plug-in Management loader list, without loaders whose provider doesn't support Standalone; Initialize XR on Startup; and the on/off state of each OpenXR feature that Standalone also has, interaction profiles included. Play Mode with an applied Quest profile then runs the Quest's OpenXR setup through a PC runtime, such as Quest Link or SteamVR.

Build-only plugin work, such as build numbers, signing, crash reporting and Addressables content, is never applied to the editor.

Apply first reverts whatever is applied, then records each setting's current value as its **baseline** before writing it. The baselines are saved to `UserSettings/ForgeEditorState.asset` before a plugin writes anything, so applying never stacks one profile on top of another, and a crash in the middle of an Apply can still be reverted.

Once a profile is applied, the button reads **Re-apply**. Use it after editing the profile's plugin settings: Re-apply goes back to the baseline and applies the profile as it is now, so removed overrides disappear too.

Apply is available for the active profile when it has something to apply: a plugin with editor settings, Play Mode XR to copy, or any configured variant. Apply, Activate and Revert to Baseline are disabled in Play Mode, while scripts compile or assets import, and during builds.

## Revert to Baseline

**Revert to Baseline** restores every setting the applied profile changed, in reverse order, removes the variant defines Apply wrote, and forgets the applied profile.

If a setting cannot be restored, its baseline is kept for another attempt, and Apply, Activate, Revert to Baseline and builds stop on it until a revert succeeds. When the cause is permanent, for example the package of the plugin that wrote the setting was removed, or the baseline names an XR loader that no longer exists, the dialog after the failed revert offers **Stop Tracking**: those settings keep their current values and Build Forge forgets them. As a last resort, with the editor closed, deleting `UserSettings/ForgeEditorState.asset` forgets every baseline.

## Drift

While a profile is applied, Build Forge compares the current editor state of each applied setting with the state right after the Apply. A difference is **drift**: someone, or some package, changed an applied setting since. A missing variant define on the applied Build Profile counts as drift too. Editing the profile's own configuration is not drift; use Re-apply for that.

The build window lists drifted settings, for example `OpenXR (Android)`, with **Dismiss Drift**, and the toolbar label reads **Applied, drifted**. The fixes:

- **Re-apply** writes the profile's settings again.
- **Revert to Baseline** undoes the Apply.
- **Dismiss Drift** accepts the current state as the applied one, for legitimate changes such as a package upgrade rewriting its settings. Baselines and the profile are unchanged, and the next Apply or build writes the profile's settings again.

Drift is checked at most every two seconds.

## When Unity switches profiles on its own

Unity can change the active profile without Build Forge: its own Switch Profile button, a platform in its Platforms list, `-activeBuildProfile` or `-buildTarget` on the command line, or a deleted `Library/` folder, which leaves Unity on a platform profile. The previous profile's plugin settings are then still applied underneath the new active profile.

The build window and the main toolbar warn about that and name the fixes: Apply the active profile to replace the settings with its own, activate the applied profile again to keep them, or Revert to Baseline. Building stays possible and correct in that state, because every build starts from the baseline.

## Builds while a profile is applied

A build always starts from the baseline: it restores the applied profile's settings first, applies the built profile's own settings, and applies the previously applied profile again afterwards. That holds also when the built profile is the applied one, since its configuration may have changed since it was applied. An applied profile therefore never leaks into a build.

## Commit settings while nothing is applied

Applying writes version-controlled assets: OpenXR and XR Management settings under `Assets/XR/`, and the variant defines on the Build Profile. Other machines and CI have no record of that Apply, so for them the applied values are simply the project's settings, and every profile that leaves such a setting at **No Override** builds with it. Revert to Baseline before committing those files, unless the applied state is meant to become the project's.

The build window reminds you while a profile is applied. A `BUILD_VARIANT_` define on a Build Profile that is not applied on this machine was saved while a variant was applied elsewhere; the build window, the Build Forge section and the build log say so, builds recompute the defines anyway, and Activate removes it.

## The BUILD_PROFILE_ define

Build Forge keeps one scripting define in each Unity Build Profile with Build Forge settings: `BUILD_PROFILE_` plus the profile's name, upper-cased, with every run of other characters collapsed to one underscore. "Quest 3" gives `BUILD_PROFILE_QUEST_3`. Letters outside ASCII are kept: "Übung" gives `BUILD_PROFILE_ÜBUNG`.

The define is in the Build Profile's own Scripting Defines list, which Unity applies whenever the profile is active, in the editor and in builds:

```csharp
#if BUILD_PROFILE_QUEST_3
    // Only in builds of, and while the editor is on, the Quest 3 profile
#endif
```

- Build Forge writes it when it touches the profile: when the settings are added, when the Build Forge section or the build window shows the profile, and on Activate and Apply. After a rename it replaces the old one. Defines without the prefix are left alone.
- Writing to the active profile recompiles scripts once.
- At build time a missing or stale define is only reported in the build log, never written, since writing would recompile in the middle of the build. Open the build window once and commit the profile.
- Two names can give the same define ("Quest 3" and "Quest-3"); the build window and the Build Forge section warn about that.
- **Project Settings > Build Forge > Profiles > Maintain Build Profile Defines** turns the define off.

## Variants

A variant is a project-wide name, such as `Internal`, picked per build. It adds a `BUILD_VARIANT_<NAME>` define and can change development options, add defines and mark the product name. See [Build variants](variants.md).

## CI detection

Plugins can do some work only on build machines, such as uploading symbols. A build counts as a CI build when Unity runs in batch mode, unless `-forgeCI` or the `FORGE_CI` environment variable says otherwise. See [Command line and CI](ci.md#ci-detection).

## Build interception

With **Project Settings > Build Forge > Intercept Builds** on (the default), Build Forge stops every player build that it did not start, with a message pointing to its build window. That includes Unity's Build buttons and other scripts that call `BuildPipeline.BuildPlayer`. The point is that every build gets the plugin settings, the manifest and the restore. Turn it off while older build scripts still have to work.

## Fresh clones

Unity keeps the active Build Profile in `Library/`, and Build Forge keeps what is applied in `UserSettings/`. Neither is committed, so a fresh clone opens on Unity's default platform profile with nothing applied, and imports every asset for that platform.

- To import once, for the right platform, open the project the first time with `-activeBuildProfile "Assets/Settings/Build Profiles/<Profile>.asset"`. In Unity Hub that is the project's menu > Add command line arguments; remove the argument afterwards, since the Hub keeps it.
- Apply the profile in the build window if Play Mode should match its plugin settings.
- Unity records the active Build Profile by its path. If that asset is renamed or moved outside the editor, for example by a pulled commit, the next batch-mode start fails before any `-executeMethod` runs; see [Troubleshooting](troubleshooting.md#failed-to-read-the-specified-build-profile).
