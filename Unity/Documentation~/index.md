# Build Forge manual

Build Forge (`com.cortopiastudios.buildforge`) is a Unity package that extends Unity's Build Profiles. Each Unity Build Profile can hold a **Build Forge** section with settings for that profile: OpenXR features, the XR loader list, the build number, Android signing from environment variables, Addressables content rules, an Android manifest, the XR vendor SDKs to keep out of Android builds, and settings for your own plugins. Build Forge applies those settings for the duration of a build and restores them afterwards, also when the build fails.

Around that it adds:

- a build window to activate, apply and build profiles, and a main toolbar element to activate and apply them;
- **Apply** and **Revert to Baseline**, so Play Mode can run with a profile's plugin settings, with drift detection;
- build variants, such as an internal build with debug tools, selectable per build;
- a `BUILD_PROFILE_<NAME>` scripting define in every Build Profile with Build Forge settings;
- a build manifest in the player, readable at runtime;
- a command-line entry point for CI with strict argument checking;
- a Player Overrides window that shows how each Build Profile's own Player Settings differ from Project Settings.

Build Forge leaves to Unity what Build Profiles already do. Scenes, scripting defines, build options and Player Settings stay on the Unity Build Profile. Build Forge does not override Player Settings; when a profile needs its own Player Settings, use the Build Profile's own Player Settings and check them in the [Player Overrides window](player-overrides.md).

## Requirements

- Unity 6000.3 or later.
- For the optional plugins: XR Plug-in Management 4.0 or later (XR Loaders), OpenXR (OpenXR), Addressables (Addressables Stripper and Addressables Rebuild).
- For CI builds: the standalone Unity CLI (`unity`) on the build machine. The tested combination is CLI 1.0.0-beta.11 with Unity 6000.3.23f1 on Windows.

The package's tests and real player builds have been run on Windows editors. macOS and Linux have not been verified.

## Contents

Getting started

- [Installation](installation.md): git URL, pinning a version, the lock file, private repositories.
- [Quick start](quick-start.md): from a new Build Profile to a first build and its CI command.
- [Concepts](concepts.md): the Build Forge section, Activate, Apply, Revert to Baseline, drift, builds, defines.

Configuration

- [Profiles and settings](settings.md): the Build Forge section, output paths, Remove Previous Build Output, Project Settings > Build Forge, what is stored where.
- [Build variants](variants.md): variant names, rules, restrictions and their defines.
- [Bundled plugins](plugins/index.md), one page each:
  [XR Loaders](plugins/xr-loaders.md),
  [OpenXR](plugins/openxr.md),
  [Git Metadata](plugins/git-metadata.md),
  [Build Number](plugins/build-number.md),
  [Cloud Diagnostics](plugins/cloud-diagnostics.md),
  [Android Keystore Env](plugins/android-keystore-env.md),
  [Addressables Stripper](plugins/addressables-stripper.md),
  [Addressables Rebuild](plugins/addressables-rebuild.md),
  [Android Manifest](plugins/android-manifest.md),
  [XR Vendor Filter](plugins/xr-vendor-filter.md).

Using Build Forge

- [Build window and main toolbar](build-window.md)
- [Player Overrides window](player-overrides.md): one profile, or every profile side by side in Matrix mode.
- [Command line and CI](ci.md): arguments, CI detection, exit codes, Jenkins and GitHub Actions examples.
- [Build manifest](build-manifest.md): what a player can read about its build.

Extending and adopting

- [Writing plugins](custom-plugins.md): the `IForgePlugin` API, configuration storage and execution order.
- [Integrating Build Forge into an existing project](integration-guide.md): a step-by-step guide with the pitfalls seen in real projects.
- [Troubleshooting and FAQ](troubleshooting.md)

The package also ships two samples, importable from the Package Manager: **Manifest Reader** (reads the build manifest at runtime) and **Example Integration** (a small project plugin and CI scripts; see its README).

## Terms used in this manual

| Term | Meaning |
|---|---|
| Unity Build Profile | Unity's Build Profile asset: platform, scenes, scripting defines, build options, and optionally its own Player, Quality and Graphics Settings. |
| Build Forge settings | The Build Forge section stored inside a Unity Build Profile asset (the `ForgeProfile` type in the API). |
| Platform profile | One of Unity's built-in per-platform profiles in the Build Profiles window's Platforms list. They live in `Library/` and cannot hold Build Forge settings. |
| Plugin | A class that takes part in builds of every profile it applies to. Ten are bundled; projects can add their own. |
| Variant | A project-wide build choice such as `Internal`, picked per build. The build without a variant is the default build. |
| Active | Unity's state: the Build Profile Unity currently uses. |
| Applied | Build Forge's state: the profile whose plugin settings are currently written to the editor. |
| Baseline | The editor settings as they were before Build Forge applied a profile. |
| Drift | An editor setting that changed after a profile was applied. |
