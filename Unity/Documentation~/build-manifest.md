# Build manifest

Every Build Forge build puts a small JSON file into the player that describes the build: which Build Profile and variant, when, which versions, and what the plugins recorded, such as the git commit and the enabled OpenXR features. The game can read it at runtime, for example for a version label or a bug report, and CI scripts can read it from the built artifact.

## Where it is

During the build the manifest is written to `Library/BuildForge/Manifest/BuildManifest.json` and handed to Unity's build pipeline, which adds it to the player's StreamingAssets as `BuildForge/BuildManifest.json`. The project's `Assets` folder is never touched, so there is nothing to import, commit or clean up; the `Library` copy is removed after the build.

In the player it is at `StreamingAssets/BuildForge/BuildManifest.json`: for a Windows player in `<Product>_Data/StreamingAssets/BuildForge/`, in an APK at `assets/BuildForge/BuildManifest.json`.

It is on by default. **Project Settings > Build Forge > Build Manifest > Write Build Manifest** turns it off; **Log Manifest Contents** logs the JSON during each build. With the manifest on, a build fails when the manifest cannot be written, or has disappeared when the build pipeline adds it, rather than producing a player without it.

## Fields

The fields come in three groups, in this order in the file: the product, what was built, and the environment it was built in. The plugins' sections follow.

### Product

| Property | JSON key | Value |
|---|---|---|
| `ProductName` | `productName` | The product name, marked for a variant build, for example `My Game (Internal)`. |
| `ProductVersion` | `productVersion` | `PlayerSettings.bundleVersion` as built, including a `-forgeVersion` override or a plugin's change. |
| `BuildNumber` | `buildNumber` | The [Build Number](plugins/build-number.md) plugin's number when it is enabled for the profile, on every platform; otherwise the Android version code or iOS build number as built; empty when there is neither. |

### Target

| Property | JSON key | Value |
|---|---|---|
| `BuildProfile` | `buildProfile` | The name of the Unity Build Profile the build was made from. |
| `BuildVariant` | `buildVariant` | The variant, empty when the project has none. |
| `BuildTarget` | `buildTarget` | Unity's build target, for example `Android`. |
| `BuildTargetGroup` | `buildTargetGroup` | Its target group, for example `Android` or `Standalone`. |
| `DevelopmentBuild` | `developmentBuild` | Whether the player is a development build, after the variant's rule. See [Development build and managed code variant](#development-build-and-managed-code-variant). |
| `ManagedCodeVariant` | `managedCodeVariant` | Unity 6.6 and later: the managed code variant the C# code was compiled with, `Debug`, `Checked`, `Instrumented` or `Release`. That is the Managed Code Variant player setting, or `Debug` for a development build with Script Debugging. Empty on earlier editors. See [Development build and managed code variant](#development-build-and-managed-code-variant). |

### Environment

| Property | JSON key | Value |
|---|---|---|
| `UnityVersion` | `unityVersion` | The editor version that built the player. |
| `BuildForgeVersion` | `buildForgeVersion` | The version in Build Forge's `package.json`, or `unknown` when it cannot be read. |
| `BuildTimestamp` | `buildTimestamp` | When the manifest was written, right before the player build, in UTC and ISO 8601, for example `2026-10-05T11:42:07.1234567Z`. |

`PluginSections` (`pluginSections`) holds one section per contributing plugin, each with a `name` and `entries` of `key` and `value`; see [Plugin sections](#plugin-sections).

The values are taken after every plugin's pre-build step, so they are what the build used.

## Development build and managed code variant

Two fields say how the player was built. From Unity 6.6 they describe different things, and you need both to know what a player contains:

- `developmentBuild` is about the engine. A development player runs Unity's development engine binary, which contains the profiler and the memory leak detector, and `Debug.isDebugBuild` is true in it.
- `managedCodeVariant` is about your C# code: how Unity compiled it.

| Managed code variant | Compiled with | Optimization |
|---|---|---|
| `Release` | No diagnostics | On |
| `Instrumented` | Profiler instrumentation: `UNITY_INCLUDE_INSTRUMENTATION`, `ENABLE_PROFILER` | On |
| `Checked` | Instrumented's, plus assertions and safety checks: `UNITY_ASSERTIONS`, `UNITY_ENABLE_CHECKS` | On |
| `Debug` | Checked's, plus `DEBUG` | Off |

Before Unity 6.6, Development Build decided both: a development build's C# code was compiled like `Checked`, and `managedCodeVariant` is empty. From Unity 6.6 the two are set separately, so a development build can run `Release` code and a release build `Checked` code. When a bug report or a performance measurement comes from a player, check both fields.

Two details on Unity's side, as of Unity 6.6:

- With **Script Debugging** on in a development build, Unity compiles the C# code like `Debug` whatever the Managed Code Variant setting says, so `managedCodeVariant` is `Debug` for such a build.
- A development build still defines `UNITY_ASSERTIONS` and `UNITY_INCLUDE_INSTRUMENTATION` whatever the variant, which Unity says may change.

For the settings themselves, see Unity's [Configure a build for debugging and profiling](https://docs.unity.com/en-us/engine/6000.6/manual/building-and-publishing/managed-code-variants).

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
    "productName": "My Game (Internal)",
    "productVersion": "1.4.0",
    "buildNumber": "412",
    "buildProfile": "Quest",
    "buildVariant": "Internal",
    "buildTarget": "Android",
    "buildTargetGroup": "Android",
    "developmentBuild": true,
    "managedCodeVariant": "",
    "unityVersion": "6000.3.23f1",
    "buildForgeVersion": "0.9.0",
    "buildTimestamp": "2026-10-05T11:42:07.1234567Z",
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

        Debug.Log($"{manifest.BuildProfile} {manifest.BuildVariant} {manifest.ProductVersion} ({manifest.BuildNumber})");

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
