# Bundled plugins

Build Forge ships ten plugins. Each has its own fixed order: a build runs the plugins' pre-build steps from the lowest order to the highest, and their post-build steps in reverse.

| Order | Plugin | Needs | Per-profile switch | Changes for a build | Apply | Manifest section |
|---|---|---|---|---|---|---|
| 100 | [XR Loaders](xr-loaders.md) | XR Plug-in Management 4.0+ | Enabled, off by default | The platform's XR loader list | Yes | `XRLoaders` |
| 200 | [OpenXR](openxr.md) | OpenXR | None | The platform's OpenXR settings and features | Yes | `OpenXR` |
| 300 | [Git Metadata](git-metadata.md) | Git on PATH | None | Nothing; manifest only | No | `Git` |
| 400 | [Build Number](build-number.md) | Git, or an environment variable | Enabled, off by default | Android version code, iOS build number | No | `BuildNumber` |
| 500 | [Cloud Diagnostics](cloud-diagnostics.md) | | Enabled, off by default | Crash reporting, and with it the symbol upload | No | |
| 600 | [Android Keystore Env](android-keystore-env.md) | An Android profile | Enabled, off by default | Android keystore path, passwords and key alias | No | |
| 700 | [Addressables Stripper](addressables-stripper.md) | Addressables, initialized | None | Which groups and labelled entries are in the build | No | `Addressables` |
| 800 | [Addressables Rebuild](addressables-rebuild.md) | Addressables, initialized | Enabled, off by default | Builds the Addressables content | No | |
| 900 | [Android Manifest](android-manifest.md) | An Android profile | None | The main manifest file and Custom Main Manifest | No | `AndroidManifest` |
| 1000 | [XR Vendor Filter](xr-vendor-filter.md) | An Android profile | Enabled, off by default | Which vendor libraries reach the Gradle project, and the vendors' manifest entries | No | `XRVendorFilter` |

**Apply** marks the plugins whose settings Apply writes to the editor for Play Mode. The others only act during builds.

## Enabling and disabling plugins

- **For the whole project**: **Project Settings > Build Forge > Installed Plugins** has a toggle per plugin, all on by default. A plugin turned off there is hidden from every Build Forge section and skipped by every build.
- **For one profile**: plugins with a per-profile switch show an **Enabled** checkbox in the profile's Build Forge section. They start off, so installing Build Forge does not change what a build produces until you enable them. Git Metadata, OpenXR, Addressables Stripper and Android Manifest have no switch: Git Metadata only writes the manifest, and the others change nothing until the profile configures overrides, exclusions or a manifest. (A build of a [Meta Quest profile](openxr.md#meta-quest-build-profiles) before OpenXR's own Meta Quest setup has run, as in a fresh CI checkout, is the one case where OpenXR enables features without overrides.)

A plugin that does not apply to a profile is skipped for it, and its foldout explains why: Android Keystore Env, Android Manifest and XR Vendor Filter apply only to Android profiles, OpenXR only to platforms with OpenXR settings, and the Addressables plugins only once the project has Addressables settings.

## Order

| Order | Why there |
|---|---|
| 100 XR Loaders | Selects the XR providers before OpenXR settings and content generation look at them. |
| 200 OpenXR | Applies features after loader selection and before content generation. |
| 300 to 600 | Git Metadata only reads; Build Number, Cloud Diagnostics and Android Keystore Env change independent settings. |
| 700 Addressables Stripper | Must run before Addressables Rebuild, because Include in Build flags decide what the content build packs. |
| 800 Addressables Rebuild | Builds content with the stripping already in place. |
| 900 Android Manifest | Swaps the profile's main manifest in before the player build. |
| 1000 XR Vendor Filter | Last, so its Gradle step's leftover check sees what every other plugin's Gradle step left. |

The variant's settings are put in place before any plugin runs and undone after the last one. Plugins of your own pick an order between these values; see [Writing plugins](../custom-plugins.md#execution-order).

## In the build window

The build window's details pane lists every plugin under **Plugins for this build**, in order. A plugin that will run is numbered and followed by one line of what it will do, such as `build number from git commit count + 20` or `crash reporting off (CI only; local build)`. A plugin that will be skipped is greyed with the reason: disabled in Project Settings, not applicable, or disabled for this profile.

The list describes the build the window's Build button makes, so it reflects the editor's CI detection, normally a local build. A command-line build counts as CI; see [CI detection](../ci.md#ci-detection).
