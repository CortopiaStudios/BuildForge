# Build manifest

Every Build Forge build puts a small JSON file into the player that describes the build: which Build Profile and variant, when, which versions, and what the plugins recorded, such as the git commit and the enabled OpenXR features. The game can read it at runtime, for example for a version label or a bug report, and CI scripts can read it from the built artifact.

## Where it is

During the build the manifest is written to `Library/BuildForge/Manifest/BuildManifest.json` and handed to Unity's build pipeline, which adds it to the player's StreamingAssets as `BuildForge/BuildManifest.json`. The project's `Assets` folder is never touched, so there is nothing to import, commit or clean up; the `Library` copy is removed after the build.

In the player it is at `StreamingAssets/BuildForge/BuildManifest.json`: for a Windows player in `<Product>_Data/StreamingAssets/BuildForge/`, in an APK at `assets/BuildForge/BuildManifest.json`.

It is on by default. **Project Settings > Build Forge > Build Manifest > Write Build Manifest** turns it off; **Log Manifest Contents** logs the JSON during each build. With the manifest on, a build fails when the manifest cannot be written, or has disappeared when the build pipeline adds it, rather than producing a player without it.

## Fields

| Property | JSON key | Value |
|---|---|---|
| `BuildProfileName` | `buildProfileName` | The Unity Build Profile the build was made from. |
| `BuildVariant` | `buildVariant` | The variant, `Default` for the default build once the project has variants, empty when it has none. |
| `BuildTimestamp` | `buildTimestamp` | When the manifest was written, right before the player build, in UTC and ISO 8601, for example `2026-10-05T11:42:07.1234567Z`. |
| `BuildTarget` | `buildTarget` | Unity's build target, for example `Android`. |
| `BuildTargetGroup` | `buildTargetGroup` | Its target group, for example `Android` or `Standalone`. |
| `ProductName` | `productName` | The product name, marked for a variant build, for example `My Game (Internal)`. |
| `ProductVersion` | `productVersion` | `PlayerSettings.bundleVersion` as built, including a `-forgeVersion` override or a plugin's change. |
| `BuildNumber` | `buildNumber` | The [Build Number](plugins/build-number.md) plugin's number when it is enabled for the profile, on every platform; otherwise the Android version code or iOS build number as built; empty when there is neither. |
| `UnityVersion` | `unityVersion` | The editor version that built the player. |
| `BuildForgeVersion` | `buildForgeVersion` | The version in Build Forge's `package.json`, or `unknown` when it cannot be read. |
| `DevelopmentBuild` | `developmentBuild` | Whether the player is a development build, after the variant's rule. |
| `ManagedCodeVariant` | `managedCodeVariant` | Unity 6.6 and later: the Managed Code Variant player setting (`Debug`, `Checked`, `Instrumented` or `Release`). Empty on earlier editors. |
| `PluginSections` | `pluginSections` | One section per contributing plugin, each with a `name` and `entries` of `key` and `value`. |

The values are taken after every plugin's pre-build step, so they are what the build used.

## Plugin sections

| Section | Plugin | Entries |
|---|---|---|
| `XRLoaders` | [XR Loaders](plugins/xr-loaders.md), when enabled for the profile | `loaders` |
| `OpenXR` | [OpenXR](plugins/openxr.md) | `onlyListedFeatures`, `enabledFeatures`, `disabledFeatures`, `effectiveEnabledFeatures`, the settings overrides, the feature pins |
| `Git` | [Git Metadata](plugins/git-metadata.md) | `branch`, `commit`, `commitShort`, `describe`, `tag`, as configured and available |
| `BuildNumber` | [Build Number](plugins/build-number.md), when enabled for the profile | `buildNumber` |
| `Addressables` | [Addressables Stripper](plugins/addressables-stripper.md), when the build excludes content | `excludedGroups`, `excludedLabels` |
| `AndroidManifest` | [Android Manifest](plugins/android-manifest.md), when the profile has a main manifest | `mainManifest` |
| `XRVendorFilter` | [XR Vendor Filter](plugins/xr-vendor-filter.md), when enabled for the profile | `excludedPackages`, `removedManifestPrefixes` |

Lists inside a value are separated by `;`. Plugins of your own add sections through `IForgeManifestContributor`; see [Writing plugins](custom-plugins.md#contributing-to-the-manifest).

An example, shortened:

```json
{
    "buildProfileName": "Quest",
    "buildTimestamp": "2026-10-05T11:42:07.1234567Z",
    "buildTarget": "Android",
    "buildTargetGroup": "Android",
    "productName": "My Game (Internal)",
    "productVersion": "1.4.0",
    "buildNumber": "412",
    "unityVersion": "6000.3.23f1",
    "buildForgeVersion": "0.9.0",
    "developmentBuild": true,
    "managedCodeVariant": "",
    "buildVariant": "Internal",
    "pluginSections": [
        {
            "name": "Git",
            "entries": [
                { "key": "branch", "value": "main" },
                { "key": "commit", "value": "3f1c0d2e…" },
                { "key": "commitShort", "value": "3f1c0d2" }
            ]
        },
        {
            "name": "BuildNumber",
            "entries": [ { "key": "buildNumber", "value": "412" } ]
        }
    ]
}
```

## Reading it at runtime

The runtime API is in the `BuildForge.Runtime` assembly, which players include:

```csharp
using BuildForge.Runtime;
using UnityEngine;

public class BuildInfo : MonoBehaviour
{
    async void Start()
    {
        var manifest = await BuildManifestLoader.LoadAsync();
        if (manifest == null)
            return; // in the editor, or in a player built without the manifest

        Debug.Log($"{manifest.BuildProfileName} {manifest.BuildVariant} {manifest.ProductVersion} ({manifest.BuildNumber})");

        var git = manifest.GetSection("Git");
        if (git != null)
            Debug.Log($"Commit {git.Get("commitShort")}");
    }
}
```

- `LoadAsync()` is the recommended call. On Android and WebGL StreamingAssets is not a plain folder, so the manifest is fetched with `UnityWebRequest`, which `LoadAsync` does without blocking. Concurrent callers share one load.
- `Load()` is synchronous. It blocks briefly on Android (up to five seconds) and is not supported on WebGL, where it logs a warning and returns null.
- Both cache the result, also a missing manifest, after the first load. `BuildManifestLoader.ClearCache()` forgets it.
- `GetSection(name)` returns a section or null; `ManifestSection.Get(key)` returns a value or null; `PluginSections` and `Entries` list everything.
- In the editor there is no manifest, and both calls return null.
- For a version label such as `v1.4.0 #412`, combine `ProductVersion` and `BuildNumber`. `BuildNumber` is empty for a build without one, such as a Windows profile without [Build Number](plugins/build-number.md).

The **Manifest Reader** sample is a MonoBehaviour that logs every field and section.

## Reading it on CI

The JSON keys are the ones in the table. A script can open the artifact and check what was built before publishing it, for example that an APK's `assets/BuildForge/BuildManifest.json` names the expected profile and variant, or that `effectiveEnabledFeatures` in the `OpenXR` section holds the features the device needs.
