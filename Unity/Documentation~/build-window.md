# Build window and main toolbar

## Build window

Open it with **Window > Build Forge > Build**. It is laid out like Unity's Build Profiles window: a list of profiles on the left, details of the selected one on the right, and an action bar along the bottom.

### Profile list

One row per Unity Build Profile with Build Forge settings, sorted by platform, then name. A row shows the platform icon and the profile's name, and on the right:

- **Active**, outlined in green: Unity's active Build Profile.
- **Applied**: the profile whose plugin settings are applied to the editor; its name is also bold.
- A warning or error icon when the profile has issues; hover it for the full text.

Click a row to select it, double-click to open its Unity Build Profile, with the Build Forge section, in the Inspector. When the window opens, it selects the active profile.

The toolbar's **Refresh** button finds profiles again and recomputes the warnings. Use it after adding Build Forge settings to a profile while the window is open; actions such as Activate and Apply refresh by themselves.

### Notices about the editor state

While something is applied, the top of the details pane explains the editor state:

- A warning when Unity switched to another Build Profile, or to a platform profile, without Build Forge, with the fixes. See [When Unity switches profiles on its own](concepts.md#when-unity-switches-profiles-on-its-own).
- Then either the drift notice, naming the editor settings that differ from the profile as applied, with a **Dismiss Drift** button (see [Drift](concepts.md#drift)), or a reminder that the applied settings live in version-controlled assets, so revert to baseline before committing them.
- If the applied Build Profile no longer exists, or no Build Forge settings can be found for it, only a warning that Revert to Baseline restores the settings it changed.

### Details

| Item | Shows |
|---|---|
| Header | The platform icon, the profile's name, and **Open in Inspector**, which selects the Unity Build Profile: the Inspector shows Unity's settings and the Build Forge section. |
| Unity Build Profile | The asset, marked "(active)" when it is; click to show it in the Project window. |
| Build Forge Settings | Click to edit them in the Inspector, in the Unity Build Profile's Build Forge section. |
| Platform | The build target. |
| Product Name | Shown for a variant whose build is marked, for example `My Game (Internal)`. |
| Output | The resolved output path for the active profile; the template, marked as such, for the others. |
| Scripting Define(s) | The profile's `BUILD_PROFILE_` define, and the selected variant's define. |
| Version | The selected variant's [Version](variants.md#version), filled in except for `{BuildNumber}`, with a warning when the profile's builds have no build number. |
| Build Configuration | The selected variant's [Build Configuration](variants.md#build-configuration) settings that this profile's builds get, with a warning when development options would go unused. |
| Editor | What is applied: "no plugin settings to apply", "plugin settings applied", or "plugin settings and Play Mode XR applied" when Apply also set [Play Mode's XR](concepts.md#apply-and-re-apply) (with ", drifted" and the variant), "plugin settings not applied (Apply to make Play Mode match)", or "activate, then apply, to iterate on this profile in the editor". |
| Warnings | Each issue in full: plugins' warnings, a missing `{Variant}` in the output path, a define collision, a variant define saved elsewhere. An invalid variant selection shows as an error. |
| Plugins for this build | Every plugin in execution order: numbered with what it will do, or greyed with why it is skipped. See [Bundled plugins](plugins/index.md#in-the-build-window). |
| CI Command | The commands that build this profile and the selected variant on CI, each with a **Copy** button: **One workspace per job**, the build command, and **Shared workspace**, Activate followed by the build, for a workspace that also builds other platforms' profiles. See [Command line and CI](ci.md#changing-platform). |

### Action bar

Every slot is always shown and is disabled with the reason as its tooltip, so the buttons do not move as the state changes.

| Slot | Action |
|---|---|
| Variant | Shown once variants exist. The variant for Build and Apply: `<default>` or a configured name. Variants the selected profile does not allow are listed as unavailable. |
| Revert to Baseline | Restores every setting the applied profile changed. Available whenever something is applied, whichever profile is selected. |
| Activate *profile* | For a profile that is not active: makes it the active Unity Build Profile, after reverting another profile's applied settings. |
| Apply *profile* / Re-apply *profile* | For the active profile: writes its plugin settings and the variant's defines to the editor. Disabled when the profile has nothing to apply. |
| Build *profile* | Builds the selected profile with the selected variant. Only for the active profile. |

The actions run after the current editor frame, never in the middle of drawing, and are refused in Play Mode, while scripts compile or assets import, and during a build.

After a failed **Revert to Baseline**, a dialog lists what could not be restored and offers **Stop Tracking** or **Keep Tracking**. See [Revert to Baseline](concepts.md#revert-to-baseline).

## Main toolbar

Build Forge adds two dropdowns to Unity's main toolbar, together one toolbar element named **Build Forge**.

### Profile dropdown

Shows the active Unity Build Profile with its platform icon, or **Platform profile** while Unity is on one of its platform profiles. The menu lists every profile with Build Forge settings, sorted like the build window, the active one checked. Picking one activates it exactly like the build window's **Activate**.

### Apply dropdown

Its label says what is applied to the active profile, in the build window's words:

| Label | Meaning |
|---|---|
| Not applied | Nothing of the active profile is applied. |
| Applied | Applied, in a project without variants. |
| Applied (Internal) | Applied with the `Internal` variant. |
| Applied (default variant) | Applied with the default build, in a project with variants. |
| Applied, drifted (…) | Applied, and editor settings changed since. |

The menu:

- with variants: `<default>` and each variant, the applied one checked. Picking one applies the active profile with it, picking the checked one re-applies it, and the build window then selects that variant too;
- without variants: **Apply**, or **Re-apply** once applied;
- **Revert to Baseline** whenever anything is applied, also another profile's settings left applied when Unity switched profiles on its own. The tooltip then carries the build window's warning.

The dropdown is shown only while its menu has something to offer.

### Behavior

- Profiles that cannot be activated and variants the profile does not allow are listed but disabled.
- Both dropdowns are disabled in Play Mode, while scripts compile or assets import, and during builds.
- The labels follow changes made elsewhere, such as Unity's own Switch Profile, within about a second.
- Unity starts package toolbar elements hidden; Build Forge shows its own by default. Hide it like any toolbar element: right-click > **Hide**, or **Build Forge** in the toolbar's ⋮ menu, which also shows it again. The choice is kept per user in `UserSettings/ForgeUserPreferences.asset`, so it survives a layout reset.
- Unity remembers where toolbar elements were placed. Hold Ctrl and drag to move it.
- Unity 6.6 cuts toolbar labels at 120 pixels, so a long profile or variant name ends in "…" there; hover for the full label.
- Batch mode has no toolbar; nothing of this runs there.
