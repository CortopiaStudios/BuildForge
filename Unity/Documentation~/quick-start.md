# Quick start

This walk-through takes a project with Build Forge installed from a new Unity Build Profile to a first build and the command that repeats it on CI. It uses a Windows profile, which needs no extra platform module on a Windows editor; the steps are the same for any platform.

## 1. Create a Unity Build Profile

1. Open Unity's Build Profiles window (**File > Build Profiles**) <!-- unverified: Unity menu path and button labels -->.
2. Click **Add Build Profile**, choose **Windows**, and add it. Unity saves the profile as an asset in the project (by default under `Assets/Settings/Build Profiles/`) <!-- unverified: Unity's default folder for new Build Profiles -->.
3. Rename it **Windows**.

The name matters: Build Forge uses the Unity Build Profile's name everywhere. It becomes the scripting define `BUILD_PROFILE_WINDOWS`, the `{ProfileName}` part of the output path, the row in the build window, and `BuildProfileName` in the build manifest.

Only Build Profile assets in `Assets/` can hold Build Forge settings. Unity's platform profiles, the entries in the window's Platforms list, live in `Library/` and cannot.

## 2. Add Build Forge settings

Select the profile in the Build Profiles window. At the end of its settings, after Unity's own sections, is a **Build Forge** section. Click **Add Build Forge Settings**.

The same section appears in the Inspector when the Build Profile asset is selected. You can also select one or more Build Profile assets in the Project window and use **Assets > Build Forge > Add Build Forge Settings**.

The settings are stored inside the Build Profile asset, so they share its name and file: renaming, duplicating or deleting the Build Profile carries them along.

## 3. Look at what the section holds

- **Output Path**: the default template `Builds/{Target}/{ProfileName}/{Variant}/{ProjectName}`. Leave it for now.
- **Remove Previous Build Output**: on. Each build first removes what the previous build of this profile wrote. See [Profiles and settings](settings.md#remove-previous-build-output).
- One foldout per plugin. **Git Metadata** runs for every profile and only adds git information to the manifest. **Build Number** and **Cloud Diagnostics** are off until you enable them for the profile. A plugin that does not apply to this profile, such as Android Keystore Env on a Windows profile, is labeled **(not applicable)** and says why when expanded.

Edits are saved to the Build Profile asset right away, so the file on disk always matches what you see.

## 4. Activate the profile

Open **Window > Build Forge > Build**. The list on the left shows every Unity Build Profile that has Build Forge settings. Select **Windows** and click **Activate Windows**.

Activating makes the profile Unity's active Build Profile, like Unity's own Switch Profile, after reverting any other profile's applied plugin settings. If the profile targets another platform than the editor is on, Unity switches platform and reimports assets.

Once the profile is active, the same button reads **Apply Windows**. Apply writes a profile's plugin settings, such as OpenXR features, to the editor so Play Mode matches the profile. In a project without OpenXR, XR Plug-in Management or variants, this profile has nothing to apply, so the button stays disabled with that reason as its tooltip. See [Concepts](concepts.md#apply-and-re-apply).

## 5. Build

Click **Build Windows**. Build Forge:

1. removes the previous build's output, if there is any;
2. runs every plugin's pre-build step in order (the details pane lists them under **Plugins for this build**);
3. writes the build manifest and builds through Unity's profile-aware build API, so the Build Profile's scenes, defines, options and Player Settings apply as usual;
4. restores whatever the plugins changed, also when the build fails;
5. logs a summary line such as `[Build Forge] Windows — Succeeded | 1m 12s | 98.4 MB | 3 warnings` and shows the built file in the file browser.

With no variants configured, the player is at `Builds/StandaloneWindows64/Windows/<Product>.exe`, where `<Product>` is the product name with spaces and special characters replaced (`My Game` becomes `My_Game`).

Unity's own Build buttons are blocked while Build Forge's build interception is on, which is the default. Builds go through the Build Forge window, the command line, or your own editor scripts calling `ForgeBuildRunner.RunBuild`.

## 6. Check the manifest

The player carries the build manifest at `StreamingAssets/BuildForge/BuildManifest.json`, for this player `My_Game_Data/StreamingAssets/BuildForge/BuildManifest.json`. It names the Build Profile, the variant, the time, the versions and what the plugins recorded, for example the git commit. Read it at runtime with `BuildManifestLoader`; see [Build manifest](build-manifest.md).

## 7. Build from the command line

The details pane ends with **CI Command**. For a project at the repository root, **One workspace per job** reads:

```sh
unity run "." --non-interactive -- -nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Build -activeBuildProfile "Assets/Settings/Build Profiles/Windows.asset"
```

Run it from the repository root on a machine with the standalone Unity CLI and the project's editor version installed. It exits with 0 when the build and its cleanup succeeded. **Shared workspace** runs Activate before it, for a workspace that also builds other platforms' profiles. See [Command line and CI](ci.md).

## Next steps

- Add a variant, for example an `Internal` build with debug tools: [Build variants](variants.md). The build window then shows a variant selector, and the output gets `Default` and `Internal` folders side by side.
- Configure plugins: [OpenXR features per profile](plugins/openxr.md), [build numbers](plugins/build-number.md), [Android signing for CI](plugins/android-keystore-env.md).
- Use the profile picker in Unity's main toolbar: [Build window and main toolbar](build-window.md).
- Moving an existing project with its own build scripts: [Integrating Build Forge into an existing project](integration-guide.md).
