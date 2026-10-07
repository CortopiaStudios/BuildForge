# XR Vendor Filter

Order 1000. Keeps the SDKs of the packages a profile excludes out of its Android builds: their libraries, and the manifest entries their vendors' Gradle steps write. Only applicable to Android profiles.

A project that builds for several headsets has every installed XR vendor SDK in every Android build. A Steam Frame APK of a project with Meta's SDKs installed carries Meta's Platform SDK loader and Meta's Quest manifest entries: the Oculus VR launch category, the supported Quest devices, focus awareness and the Horizon OS supplement. Give the Steam Frame profile an XR Vendor Filter that excludes Meta's packages, and its APK has neither.

## How it works

- **Libraries are kept out.** The excluded packages' Android libraries (`.aar`, `.jar`, `.so`, Java and Kotlin sources, `.androidlib` folders) are never copied into the build's Gradle project. Build Forge sets Unity's include-in-build delegate on them after the vendors' own pre-build steps. A library's AAR manifest stays out with it. Managed assemblies stay in, so game code that references them still compiles. Code that starts a vendor SDK must check the headset itself, since the SDK's native library is missing on the others.
- **Manifest entries are removed.** Meta's SDKs write their entries into every Android build's manifest during the Gradle step, and nothing a profile can set prevents them, so Build Forge removes them afterwards:

  | Excluded vendor | Removed manifest entries |
  |---|---|
  | Meta (`com.meta.xr.*`, `com.unity.xr.oculus`) | Entries named `com.oculus.*` or `com.meta.*`, and Horizon OS elements |
  | Pico (`com.unity.xr.picoxr`, `com.unity.xr.openxr.picoxr`, `com.pico.*`) | Entries named `com.pico.*` or `com.picovr.*` |
  | Steam (`com.rlabrecque.steamworks.net`) | None: Steamworks.NET writes no manifest entries, so only its library is kept out |

  This covers the profile's own manifest too: entries a project's main manifest declares for Quest are removed from a build that excludes Meta.
- **Leftovers fail the build.** With **Check for Leftovers** on, the Gradle step then looks for anything still in the Gradle project: an excluded library, a removed kind of entry in a manifest, or such an entry in the manifest of a remaining library, which Gradle would merge later. The build stops with a list:

  ```
  [Build Forge/XR Vendor Filter] 1 excluded vendor item is still in the Gradle project:
  unityLibrary\libs\vendor.aar (merged by Gradle): meta-data com.oculus.platform.app
  Exclude the package that provides it, add a manifest prefix, or turn off Check for Leftovers.
  ```

## Settings

| Field | Meaning |
|---|---|
| Enabled | Off by default. |
| Excluded Packages | The installed XR vendor SDKs that ship Android libraries, by vendor: Meta, Pico, Steam. Tick the ones this profile's builds must not contain. **Other packages with Android libraries**, folded by default, holds the rest: Unity's OpenXR and XR Plug-in Management, which every OpenXR headset needs, and any other package, such as a peripheral's SDK. Its title counts the excluded ones. The tooltip shows what a known vendor's exclusion removes. A package that is excluded but no longer installed is listed with a Remove button. |
| Extra Manifest Prefixes | Comma-separated `android:name` prefixes to remove as well, for packages without built-in rules, such as `com.example.`. |
| Check for Leftovers | On by default. Turn it off only if a library you keep declares entries that a rule removes. |

**Refresh Package List** reads the installed packages again.

## Build window and manifest

`keeps com.meta.xr.sdk.core and com.meta.xr.sdk.platform out; removes com.oculus.*, com.meta.*, Horizon OS manifest entries`. The build manifest gets an `XRVendorFilter` section with `excludedPackages` and `removedManifestPrefixes`.

## Tips

- Exclude a vendor on every profile that isn't for that vendor's headsets: Meta on the Pico and Steam Frame profiles, Pico on the Quest and Steam Frame profiles, Steam on the Quest and Pico profiles.
- Leave `com.unity.xr.openxr` alone. Its OpenXR loader is what every OpenXR headset runs on, and the build window warns if it is excluded.
- When a profile needs a different manifest, rather than fewer entries, give it its own with [Android Manifest](android-manifest.md).
