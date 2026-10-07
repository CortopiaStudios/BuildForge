# XR Loaders

Order 100. Needs XR Plug-in Management (`com.unity.xr.management`) 4.0.0 or later; the plugin's assembly compiles only when it is installed.

XR Plug-in Management keeps one ordered list of XR providers (loaders) per platform, shared by every Build Profile of that platform. XR Loaders gives each Build Profile its own list.

## When you need it

Only when profiles of the same platform need different loaders, or when one of them must run without XR: for example an Android profile for a headset with the OpenXR loader and an Android TV profile without XR. When every profile of a platform uses the same loaders, the ordinary XR Plug-in Management settings are enough, also in projects that use OpenXR everywhere.

## Settings

| Field | Meaning |
|---|---|
| Enabled | Off by default. While off, the plugin leaves XR Plug-in Management's list alone. |
| Loader 1, Loader 2, … | The loader assets this profile uses, in order. **Add Loader** appends a row, **Up** moves a loader up, **Remove** removes it. |

With the plugin enabled, only the listed loaders are active for the profile, in that order. Enabled with an empty list means **no XR** for the profile. The loaders must be saved loader assets, such as the ones XR Plug-in Management creates when you tick a provider.

## Builds and Apply

- **Build**: before the build the plugin records the platform's current loader list, by asset GUID, and sets the profile's list. After the build it restores the recorded list in its original order.
- **Apply**: writes the profile's list to its platform; Revert to Baseline restores the previous one. A profile with the plugin off leaves the list alone, also when it is applied. Play Mode runs on Standalone's list: for a profile of another platform, **Play Mode Follows Applied Profile** then copies the platform's list to Standalone. See [Apply](../concepts.md#apply-and-re-apply).

Setting the list fails, and stops the build or the Apply, when XR Plug-in Management refuses the list or does not keep the requested order.

## Warnings

The build window shows these for a profile with the plugin enabled; Apply and builds of the profile stop on them:

| Message | Fix |
|---|---|
| XR Management settings are missing for this platform. | Set up XR Plug-in Management for the platform once in Project Settings. |
| An XR loader reference is missing; install its provider or repair the selection. | A listed loader's asset or provider package is gone. |
| The XR loader selection contains a duplicate. | Remove the second entry. |
| All selected XR loaders must be saved assets. | Use loader assets from the project. |

The [OpenXR](openxr.md) plugin checks this selection when it warns that OpenXR is not an active provider for a profile.

## Manifest and build window

- Manifest section `XRLoaders`, key `loaders`: the type names of the loaders the build used, separated by `;`.
- Build window line: `use <loader>, <loader>`, or `disable XR for this profile` for an empty list.
