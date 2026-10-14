# Build window and main toolbar

## Build window

Open it with **Window > Build Forge > Build**. It is laid out like Unity's Build Profiles window: a list of profiles on the left, details of the selected one on the right, and an action bar along the bottom.

### Profile list

One row per Unity Build Profile with Build Forge settings, sorted by platform, then name. The platform is the profile's own, as in Unity's Build Profiles window: a Meta Quest profile shows Meta Quest's icon and name, although it builds for Android. A row shows the platform icon and the profile's name, and on the right:

- **Active**, outlined in green: Unity's active Build Profile.
- **Applied**: the profile whose plugin settings are applied to the editor; its name is also bold.
- A warning or error icon when the profile has issues; hover it for the full text.

Click a row to select it, double-click to open its Unity Build Profile, with the Build Forge section, in the Inspector. When the window opens, it selects the active profile.

The toolbar's **Refresh** button finds profiles again and recomputes the warnings. Use it after adding Build Forge settings to a profile while the window is open; actions such as Activate and Apply refresh by themselves.

### Notices about the editor state

While something is applied, the top of the details pane explains the editor state:

- A warning when Unity switched to another Build Profile, or to a platform profile, without Build Forge, with the fixes. See [When Unity switches profiles on its own](concepts.md#when-unity-switches-profiles-on-its-own).
- Then either the drift notice, naming the editor settings that differ from the profile as applied, with **Dismiss Drift**, while plugin settings have drifted, and **Revert to Baseline** buttons (see [Drift](concepts.md#drift)), or a reminder that the applied settings live in version-controlled assets, so revert to baseline before committing them, with a **Revert to Baseline** button.
- If the applied Build Profile no longer exists, or no Build Forge settings can be found for it, only a warning that Revert to Baseline restores the settings it changed, with that button.

So Revert to Baseline is always in the notices, whichever profile is selected.

### Details

| Item | Shows |
|---|---|
| Header | The platform icon, the profile's name, the **Variant** dropdown once variants exist, and **Open in Inspector**, which selects the Unity Build Profile: the Inspector shows Unity's settings and the Build Forge section. |
| Variant | The variant the details below and the CI Command show, for any selected profile, and the one Apply, Build and Build and Run use, as do the main toolbar's Build buttons: `<default>` or a configured name. Variants the selected profile does not allow are listed as unavailable. A long name ends in "…"; the tooltip then starts with the whole label. |
| Unity Build Profile | The asset, marked "(active)" when it is; click to show it in the Project window. |
| Build Forge Settings | Click to edit them in the Inspector, in the Unity Build Profile's Build Forge section. |
| Platform | The profile's platform, and the build target it builds for when that has another name: `Meta Quest (Android)`, `Windows (StandaloneWindows64)`. |
| Product Name | Shown for a variant whose build is marked, for example `My Game (Internal)`. |
| Output | The resolved output path for the active profile; the template, marked as such, for the others. |
| Scripting Define(s) | The profile's `BUILD_PROFILE_` define, and the selected variant's define. |
| Version | The selected variant's [Version](variants.md#version): for the active profile filled in except for `{BuildNumber}`, for another profile as written. A warning when the profile's builds have no build number. |
| Build Configuration | The selected variant's [Build Configuration](variants.md#build-configuration) settings that this profile's builds get, with a warning when development options would go unused. |
| Editor | What is applied: "no plugin settings to apply", "plugin settings applied", or "plugin settings and Play Mode XR applied" when Apply also set [Play Mode's XR](concepts.md#apply-and-re-apply) (with ", drifted" and the variant), "plugin settings not applied (Apply to make Play Mode match)", or "activate, then apply, to iterate on this profile in the editor". |
| Warnings | Each issue in full: plugins' warnings, a missing `{Variant}` in the output path, a define collision, a variant define saved elsewhere. An invalid variant selection shows as an error. |
| Plugins for this build | Every plugin in execution order: numbered with what it will do, or greyed with why it is skipped. See [Bundled plugins](plugins/index.md#in-the-build-window). |
| CI Command | The commands that build this profile and the selected variant on CI, each with a **Copy** button: **One workspace per job**, the build command, and **Shared workspace**, Activate followed by the build, for a workspace that also builds other platforms' profiles. See [Command line and CI](ci.md#changing-platform). |

### Action bar

The action bar shows the actions the selected profile has. For a profile that is not active, that is **Activate**, on the left. The active profile's actions are on the right, as in Unity's Build Profiles window. Each side keeps to its edge, so no button moves when the selection changes, and a button the profile has is disabled with the reason as its tooltip rather than hidden. The actions use the variant picked in the details header. **Build** and **Build and Run** are green, Build with the Build Forge anvil and Build and Run with Unity's Play icon, as in the main toolbar.

| Button | Action |
|---|---|
| Activate | For a profile that is not active: makes it the active Unity Build Profile, after reverting another profile's applied settings. |
| Apply / Re-apply | Writes the active profile's plugin settings and the variant's defines to the editor. Disabled when the profile has nothing to apply. Its **▾** menu holds **Revert to Baseline**, which restores every setting the applied profile changed. |
| Build | Builds the active profile with the selected variant. |
| Build and Run | Builds like **Build**, then runs the build, like Unity's own Build And Run, and doesn't show it in the file browser. |

The actions run after the current editor frame, never in the middle of drawing, and are refused in Play Mode, while scripts compile or assets import, and during a build.

After a failed **Revert to Baseline**, a dialog lists what could not be restored and offers **Stop Tracking** or **Keep Tracking**. See [Revert to Baseline](concepts.md#revert-to-baseline).

## Main toolbar

Build Forge adds two dropdowns and two buttons to Unity's main toolbar, together one toolbar element named **Build Forge**.

### Profile dropdown

Shows the active Unity Build Profile with its platform icon, or **Platform profile** while Unity is on one of its platform profiles. The menu lists every profile with Build Forge settings, sorted like the build window, the active one checked. Picking one activates it exactly like the build window's **Activate**.

### Apply dropdown

Its icon and label say what is applied to the active profile. Only the variant is text, so that it fits the toolbar:

| Shows | Meaning |
|---|---|
| Not applied | Nothing of the active profile is applied. |
| A check mark and **Applied** | Applied, in a project without variants. |
| A check mark and **Internal** | Applied with the `Internal` variant. |
| A check mark and **\<default\>** | Applied with the default build, in a project with variants. |
| The warning icon | Applied, and editor settings changed since; the tooltip names them. |
| Not applied, with the warning icon | Another profile's settings are applied underneath the active one, after Unity switched profiles without Build Forge, or the applied profile no longer exists; the tooltip has the build window's warning and its fixes. |

The menu:

- with variants: `<default>` and each variant, the applied one checked. Picking one applies the active profile with it, picking the checked one re-applies it, and the build window then selects that variant too;
- without variants: **Apply**, or **Re-apply** once applied;
- **Revert to Baseline** whenever anything is applied, also another profile's settings left applied when Unity switched profiles on its own. The tooltip then carries the build window's warning.

The dropdown is shown only while its menu has something to offer.

### Build and Build and Run

**Build** and **Build and Run** build the active profile like the build window's buttons of the same names, and show the same icons: **Build and Run** also runs the build afterwards, like Unity's own Build And Run, and doesn't show it in the file browser.

Both build the variant the build window has selected, which picking a variant in the apply dropdown also sets; the tooltip names the profile and the variant. They are disabled, with the reason as their tooltip, when the window's Build would be: the active Unity Build Profile has no Build Forge settings, or the variant isn't allowed for it.

### Behavior

- Profiles that cannot be activated and variants the profile does not allow are listed but disabled.
- The dropdowns and buttons are disabled in Play Mode, while scripts compile or assets import, and during builds.
- The labels follow changes made elsewhere, such as Unity's own Switch Profile, within about a second.
- Unity starts package toolbar elements hidden; Build Forge shows its own by default. Hide it like any toolbar element: right-click > **Hide**, or **Build Forge** in the toolbar's ⋮ menu, which also shows it again. The choice is kept per user in `UserSettings/ForgeUserPreferences.asset`, so it survives a layout reset.
- Unity remembers where toolbar elements were placed. Hold Ctrl and drag to move it.
- Unity 6.6 cuts toolbar labels at 120 pixels, so a long profile or variant name still ends in "…" there; hover for the full label.
- Batch mode has no toolbar; nothing of this runs there.
