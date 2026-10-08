# OpenXR

Order 200. Needs the OpenXR package (`com.unity.xr.openxr`), which brings XR Plug-in Management; the plugin's assembly compiles only when it is installed.

Unity keeps OpenXR settings and features per platform, so every Android profile shares the Android OpenXR settings. This plugin gives each Build Profile its own overrides of those settings and features. A build applies them and restores the previous state afterwards; **Apply** writes them to the editor for Play Mode.

The plugin applies to every profile whose platform has OpenXR settings, which is the case once OpenXR is set up for that platform in XR Plug-in Management. It has no per-profile switch: a profile without overrides builds with the platform's settings as they are.

## Settings

The profile's OpenXR foldout starts with the same warnings the build window shows, then has two parts.

**Settings** lists every top-level OpenXR setting of the platform, such as render mode, depth submission and the foveated rendering API. The list is read from the installed OpenXR package, so settings added by a newer package appear on their own.

| Type | Control |
|---|---|
| On/off | **No Override (current value)**, **On**, **Off** |
| Choice | **No Override (current value)** or one of the values |
| Number | Click **No Override (current value)** to start overriding; **X** removes the override |

**Feature Overrides > Features** lists every OpenXR feature of the platform except interaction profiles. Each has **No Override**, **Enable** or **Disable**; the greyed checkbox in front shows the resulting state: the override, or for No Override the feature's current setting.

When a feature is set to **Enable**, its own settings editor (Unity's editor for that feature) appears under it. Editing it pins every visible property of the feature in the profile, one YAML line each, and builds and Apply write those values. The editor starts from the feature's current settings with the profile's pins on top. Changing the feature from Enable to No Override or Disable deletes its pins.

**Interaction Profiles** appear only when they are managed per profile; see [Interaction profiles](#interaction-profiles).

### Only Listed Features

**Only Listed Features**, above the feature list, makes the profile's features an allow-list, the way per-platform switching code usually lists each headset's features. Each feature row becomes a checkbox: builds and Apply turn the ticked features on and every other visible feature off. A feature that a package update adds is then off in the profile's builds until you tick it, and no visible feature depends on what the project's OpenXR settings say.

- A ticked feature shows its settings editor, as with Enable.
- Hidden features are left to their packages. Vendor SDKs register them for their own use, XR Plug-in Management does not show them, and their package turns them on and off itself: Meta's lifecycle feature, for example, follows the other Meta features. They are listed read-only, with their current state, under **Hidden features, left to their packages**.
- Interaction profiles follow the [Interaction Profiles](#interaction-profiles) option: with it on they are ticked like features, with it off they stay as the project's settings have them.
- Switching the mode on ticks the visible features that are on in the profile's builds now, so its builds do not change, and drops its Disable overrides. Switching it off gives every visible feature the profile doesn't list a Disable override.
- The build window's OpenXR line names the visible features that the build turns off because the profile doesn't list them, and so does the build log. Review it after a package update.

### Settings that a feature controls

Some features copy their own values into the OpenXR settings during the build. Unity's Meta Quest build step, for one, copies symmetric projection, buffer discard optimization and the space warp motion vector format from the Meta Quest feature. An override of such a setting in the Settings list would not reach the player.

The Settings list therefore hides the settings that a feature enabled in this profile's build also declares. Set them in that feature's own settings instead: enable the feature in the profile and edit them there. An override of such a setting can still come from a hand-edited profile file; the plugin then warns in the build window, in the foldout and in the build log. A profile that disables the feature can override the setting, because a disabled feature's build steps do not run.

### No Override

**No Override** means "whatever the project's OpenXR settings for the platform say". Builds and Apply start from those settings, never from another profile's, so a profile's build does not depend on which profile was applied before. It does depend on what is committed in the OpenXR settings.

For profiles that share a platform, give every feature that any of them enables or disables an explicit **Enable** or **Disable** in each of them, or use [Only Listed Features](#only-listed-features). Then no profile's build depends on the committed state of that feature. Commit the OpenXR settings while nothing is applied.

## Builds and Apply

Before the build the plugin takes a snapshot of every OpenXR setting and every managed feature of the platform, then writes the profile's overrides and pins. It then checks that each feature took its override; if one did not, the build stops before anything is built:

- `OpenXR feature '<type>' did not accept its configured override.`
- `OpenXR kept '<feature>' enabled although this profile disables it: the feature group(s) '<group>', ticked for <platform> in XR Plug-in Management, require it.` See [Feature groups](#feature-groups-are-not-supported). With Only Listed Features it says that the profile doesn't list the feature.

After the build the snapshot is restored in full. That also undoes what Unity's Meta Quest build step writes into the settings during the build.

**Apply** writes the same overrides and pins to the editor, and Revert to Baseline restores the snapshot taken before. Both save the OpenXR settings assets, which are version-controlled; see [Commit settings while nothing is applied](../concepts.md#commit-settings-while-nothing-is-applied).

Play Mode runs on Standalone's OpenXR settings. For a profile of another platform, **Play Mode Follows Applied Profile** then gives each Standalone feature the on/off state the same feature has on the profile's platform, interaction profiles included, so Play Mode uses the platform's controllers and features where Standalone has them. A feature only one platform has, such as Meta Quest Support, keeps its state. Settings overrides and feature pins are not copied. See [Apply](../concepts.md#apply-and-re-apply).

## Interaction profiles

Interaction profiles (controller bindings) are hidden by default. Several can be enabled at once, and the OpenXR runtime picks the matching one at launch, so most projects should enable every supported interaction profile once in XR Plug-in Management.

To manage them per profile, for example to leave a vendor's controllers out of one build, turn on **Project Settings > Build Forge > Installed Plugins > OpenXR > Interaction Profiles**. The profile's foldout then lists them under **Interaction Profiles**, with Enable and Disable but no settings editor. With the option off, overrides stored on interaction profiles are ignored, and the build window says so.

## Feature groups are not supported

Vendor packages add OpenXR feature groups, such as "Meta XR" or "Meta Quest", which switch a set of features on together. Unity stores which groups are ticked per platform, so a group cannot differ between two profiles of the same platform. Build Forge manages features one by one instead, which can express everything a group can.

A ticked group also works against per-profile overrides: Unity force-enables the group's required features on every domain reload and refuses to disable them. The build window warns while any group is ticked for a profile's platform, and a build stops on a Disable override that a group blocks, with an error naming the group. Untick the groups in XR Plug-in Management and enable their features per profile.

## Meta Quest Build Profiles

Unity's Meta Quest Build Profile type works with Build Forge like a plain Android profile: it builds for Android, with Android's OpenXR features and settings. In Unity 6000.6, URP also strips Quest-only shader variants for it, so its builds can be smaller. Unity's OpenXR package changes the shared Android features around such a profile:

- **OpenXR 1.16** enables the Oculus Touch, Meta Quest Touch Pro and Touch Plus interaction profiles on every domain reload while a Meta Quest profile is active. A Disable override on one of them therefore holds in builds but not in the editor; Build Forge warns about such an override, which exists only with Interaction Profiles enabled.
- **OpenXR 1.18** enables those three and Meta Quest Support once, when the editor loads with a Meta Quest profile active, and turns them off on the first load without one. It records this as `isMetaQuestInitialized` in `ProjectSettings/BuildProfileUtilityOpenXR.asset`, and only an interactive editor runs it.
  - A build of a Meta Quest profile therefore enables those features itself while the file says OpenXR has not, as in a CI checkout where it was committed as 0 or not at all, and restores the settings afterwards. CI builds get what the editor would.
  - In a project whose Android profile is a Meta Quest profile, commit what the editor leaves with that profile active: `isMetaQuestInitialized: 1` with those four features on. Sessions on that profile then change nothing; with 0 committed, each of them turns the features on again. Sessions on other profiles still turn them off until the Meta Quest profile is active again.
  - A plain Android profile warns while the file says OpenXR has enabled them, because the next editor load without a Meta Quest profile turns them off. To keep the features as configured, set `isMetaQuestInitialized: 0` in that file and commit it.

In an OpenXR 1.18 project, switching between a Meta Quest profile and another Android profile therefore changes `OpenXR Package Settings.asset` and `BuildProfileUtilityOpenXR.asset`. Give each Android profile an Enable or Disable override for Meta Quest Support, so its builds do not depend on which profile was active last.

When a profile pins symmetric projection on in the Meta Quest feature, untick the original Quest in that feature's target devices: OpenXR's validation rejects symmetric projection while it is targeted, and the build fails.

## Warnings

| Warning | Meaning |
|---|---|
| OpenXR is not enabled as a plug-in provider in XR Plug-in Management for *platform* | The profile has OpenXR overrides, and they have no effect until OpenXR is a provider. With [XR Loaders](xr-loaders.md) enabled, its list is checked. A profile without overrides, such as one for a target without OpenXR, gets no warning. |
| OpenXR feature '*type*' is configured for Enable but is not installed for this target | The feature's package is missing, or the type was renamed. |
| OpenXR interaction profile '*type*' has an override that is ignored | Interaction Profiles are not managed per profile. |
| OpenXR feature '*feature*' is hidden | An Enable or Disable override on a hidden feature, which its package turns on and off itself; turning it off can break the package. Set it to No Override. With Only Listed Features, a hidden feature in the list has no effect. |
| OpenXR setting override '*setting*' may not reach the player | See [Settings that a feature controls](#settings-that-a-feature-controls). |
| Feature group(s) '*group*' are enabled for *platform* | See [Feature groups](#feature-groups-are-not-supported). |
| A Meta Quest platform profile is active: Unity re-enables … | OpenXR 1.16, with Interaction Profiles on. |
| OpenXR enabled the Oculus Touch, … and turns them off again the next time the editor loads without one active | OpenXR 1.18, on a plain Android profile. |

## Manifest

Section `OpenXR`:

| Key | Value |
|---|---|
| `onlyListedFeatures` | `true` when the profile uses [Only Listed Features](#only-listed-features); `enabledFeatures` is then its list |
| `enabledFeatures`, `disabledFeatures` | The profile's Enable and Disable overrides, type names separated by `;` |
| `effectiveEnabledFeatures` | The features enabled when the build ran, after the overrides |
| One key per settings override | Its value, for example `m_depthSubmissionMode` |
| One key per feature pin | `FeatureType.propertyPath`, for example `UnityEngine.XR.OpenXR.Features.MetaQuestSupport.MetaQuestFeature.m_symmetricProjection`, for the features the profile enables |

Check `effectiveEnabledFeatures` when you verify what a player was built with: the configured lists say what the profile asked for, the effective list what the build got.

Build window line: `enable 3 feature(s), disable 12, 1 setting override(s)`. With Only Listed Features: `only the 3 listed feature(s); off in this build (not listed): Debug Utils, Mock Runtime; 1 setting override(s)`.

## Known limitation

"Additional Graphics Queue (Vulkan)" and "Offscreen Rendering Only (Vulkan)" cannot be overridden per profile. They live on an internal OpenXR editor settings class and are written to the player's boot configuration by OpenXR's build processor.

## Tips

- Without Only Listed Features, the feature list shows every feature in the platform's OpenXR settings, hidden ones included: the features that vendor SDKs add for their own use, which OpenXR marks hidden and XR Plug-in Management does not show. Disabling one can break the SDK; in one project a vendor's lifecycle feature ended up disabled when every unlisted feature was disabled. The plugin warns about an override on a hidden feature. Leave features you do not know at No Override.
- To move a project's existing per-platform switching code onto profiles: where the code turns on exactly a list of features, turn on Only Listed Features and tick that list; otherwise translate what it changes into Enable and Disable overrides. Then compare `effectiveEnabledFeatures` in the new build's manifest with what the old pipeline produced.
- Disabling a vendor's features does not keep its SDK out of Android builds: Meta's SDKs, for example, still ship their libraries and write their manifest entries. Keep them out with [XR Vendor Filter](xr-vendor-filter.md).
- Editing a feature's settings for a profile is done with the feature set to Enable. To pin a setting for a feature that is already on in the project's settings, set it to Enable anyway; the override then also documents that the profile needs it.
