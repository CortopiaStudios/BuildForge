# Profiles and settings

## Adding, editing and removing Build Forge settings

| Where | What you can do |
|---|---|
| Unity's Build Profiles window, with a Build Profile selected | The **Build Forge** section after Unity's own sections: **Add Build Forge Settings**, edit them, **Remove Build Forge Settings** |
| The Inspector, with a Build Profile asset selected | The same section |
| **Assets > Build Forge** (also the Project window's context menu) | **Add**, **Edit** or **Remove Build Forge Settings** for the selected Build Profile assets; Add and Remove work on several at once |
| The Build Profile Inspector's context menu | **Add** or **Edit Build Forge Settings** |
| The build window | **Open in Inspector** in the details header, or double-click a profile in the list |

**Edit** shows the settings on their own in the Inspector. That view also has the **Unity Build Profile** field (read-only, with **Select**) and a **Player Overrides** foldout, which the section inside Unity's Build Profile editor leaves out because that editor already shows the profile and its Player Settings. **Open in Inspector** and a double-click in the build window select the Unity Build Profile instead, so the Inspector shows Unity's settings and the Build Forge section. Both views end with an **Open Build Window** button.

**Remove** asks first, then deletes the profile's plugin settings, output path and variant restrictions; version control can bring them back. A `BUILD_PROFILE_<NAME>` define that Build Forge wrote stays in its Scripting Defines list.

Unity offers no public way for packages outside the Unity registry to add a section to its Build Profile editor, so Build Forge inserts its section into that editor's UI itself. If a future Unity version changes that UI, the section may be missing; the Assets menu and the build window keep working.

## The Build Forge section

**Core Settings**

| Field | Meaning |
|---|---|
| Restrict Variants | Limit which named variants this profile can be built with. Off: every variant is available. |
| Allowed Variants | With Restrict Variants on: the variants this profile allows, at least one. |
| Variant Restriction Reason | Optional text shown when someone picks a variant this profile does not allow. |
| Output Path | Where builds go. See [Output path](#output-path). |
| Remove Previous Build Output | Remove the previous build's files before each build. See [Remove Previous Build Output](#remove-previous-build-output). |

The section also shows problems that concern the whole profile: invalid variant configuration (as an error), two profile names that give the same define, and a variant define saved on another machine.

**Plugins**

One foldout per plugin that is enabled in **Project Settings > Build Forge > Installed Plugins**. Foldouts start collapsed; hovering a plugin's name shows its description.

- A plugin with a per-profile switch has an **Enabled** checkbox at the top of its foldout. While it is off, the foldout title ends in **(disabled)**, its settings are greyed out, and builds of the profile skip the plugin.
- A plugin that does not apply to the profile, for example Android Keystore Env on a Windows profile, is titled **(not applicable)** and explains why. Its stored settings are kept.

Edits are written to the Build Profile asset immediately, so the file always matches what the section shows and diffs stay readable (**Project Settings > Build Forge > Profiles > Save Profiles On Edit**). Each plugin's settings are one YAML block in the Build Profile asset:

```yaml
  pluginConfigs:
  - key: BuildNumber
    config:
      rid: 7130472218546188288
  references:
    version: 2
    RefIds:
    - rid: 7130472218546188288
      type: {class: BuildNumberConfig, ns: BuildForge.Editor.Plugins, asm: BuildForge.Editor.BuildNumber}
      data:
        enabled: 1
        source: 2
        envVarName: 
        offset: 0
```

## Output path

The output path is a template, relative to the Unity project folder unless it is absolute, with a drive letter or as a network path such as `\\buildserver\releases\{Variant}`. An extended-length (`\\?\`) or device (`\\.\`) path stops the build, with slashes or backslashes in any mix, and so does a template that a placeholder's value turns into one, such as a product name that isn't sanitized; the build window and the main toolbar disable Build for a template that is one. A placeholder that is empty, such as `{Variant}` in a project without variants, drops out with its separator, and a relative template stays relative:

| Placeholder | Becomes |
|---|---|
| `{ProjectName}` | The product name from Player Settings, sanitized for file names (see below). A variant build uses the marked name, for example `My_Game_Internal`. |
| `{ProfileName}` | The Unity Build Profile's name. |
| `{Target}` | Unity's build target name, for example `Android`, `StandaloneWindows64` or `iOS`. |
| `{Variant}` | The variant's name. With no variants configured it is empty and the empty folder level collapses. |

The default template is `Builds/{Target}/{ProfileName}/{Variant}/{ProjectName}`. A profile stores a path only once you change it, so profiles that keep the default follow later changes of the package default; **Reset** returns to it, and typing the default back has the same effect.

Build Forge appends the file extension a platform needs when the path lacks it: `.apk` or `.aab` for Android (following the profile's Build App Bundle option), `.exe` for Windows and `.app` for macOS. Other targets use the path as written.

With variants configured, the variants' builds land side by side:

```
Builds/Android/Quest/Release/My_Game.apk
Builds/Android/Quest/Internal/My_Game_Internal.apk
```

The build window warns when variants exist and a profile's output path has no `{Variant}`, because the variants' builds would then overwrite each other.

The resolved path depends on the active profile: the product name and Android's Build App Bundle option follow it. The build window therefore shows the resolved path for the active profile and the template for the others.

### Product name sanitizing

`{ProjectName}` is sanitized by default, because product names are written for people and break shell commands, Windows paths and tools such as `adb`:

| Character | Replacement | Example |
|---|---|---|
| Space | `_` | `My Game` → `My_Game` |
| `&` | `and` | `Cats & Dogs` → `Cats_and_Dogs` |
| `: ( ) ' " ! .` | removed | `Game: Remastered` → `Game_Remastered` |
| Other characters except letters, digits, `_` and `-` | `_` | |
| Runs of `_` or `-` | one character | `My___Game` → `My_Game` |
| Leading or trailing `_` and `-` | removed | |

Letters outside ASCII become `_` too. Turn sanitizing off in **Project Settings > Build Forge > Output > Sanitize Product Name**.

## Remove Previous Build Output

On by default, per profile. Before each build, in the editor and on CI, Build Forge removes:

- the artifact file the build is about to write (the APK, AAB, or Windows or Linux executable);
- the files the previous build of the same profile and variant produced in the destination, including old file names after a rename and supporting files such as `<Product>_Data` or the IL2CPP backup folder;
- the folders that build created, once they are empty.

The **destination** is the artifact's folder for APK, AAB and desktop players, and the output path itself for folder outputs such as WebGL, iOS and Android exports.

After each build Build Forge records what the build produced: every file and folder that is new in the destination since just before the build, plus the files Unity's build report lists there that the build rewrote. Comparing the folder catches files packages write themselves, which the report never lists. The record is per user, in `UserSettings/ForgeBuildOutputs.asset`; nothing is written into the build folder.

Kept: files that were in the destination before, files changed since the previous build, files another profile or variant recorded there while they still match that record, and anything behind a link or inside a nested Unity project or Git checkout.

Without a record, as on a first build or in a fresh CI workspace, only the artifact file is removed. A macOS `.app` bundle and folder outputs are cleaned only from the record.

The build stops before it starts when the destination:

- contains the project or repository root, lies inside a Unity project's own folders or Git metadata, is itself a Unity project or checkout, overlaps the package source or the Unity editor installation, or is a drive root, on Windows also where it leads through a junction or symbolic link (macOS and Linux check the path as written);
- cannot be read;
- holds a file that cannot be deleted, usually because the player is still running;
- holds a folder where the artifact file goes, typically an old Android export folder.

While the recorded destination is missing or offline, such as a disconnected share or an ejected drive, nothing is removed and the record is kept.

Use a dedicated destination per profile and variant: a file that lands in a shared folder while a build runs counts as that build's output and is removed by the next one if nobody changed it. On CI, see [Command line and CI](ci.md#build-output-and-artifacts).

Turn the option off only when earlier output must stay, for example files staged by a previous packaging step. It then applies to editor and CI builds and every variant of the profile, Build Forge removes and records nothing, and an old artifact can survive a failed build.

## Player Settings, scenes and defines

These stay on the Unity Build Profile and Build Forge builds with them as Unity would:

- the scene list (the profile's own, or the global one);
- the profile's Scripting Defines list, where Build Forge also keeps its variant defines and, with Maintain Build Profile Defines on, its `BUILD_PROFILE_` define;
- build options such as Development Build;
- the profile's own Player, Quality and Graphics Settings, when it has them.

A Build Profile's own Player Settings are a full copy of Project Settings > Player. A later change in Project Settings, such as a version bump, does not reach that copy. The [Player Overrides window](player-overrides.md) shows the differences, including such stale values.

## Project Settings > Build Forge

Stored in `ProjectSettings/ForgeSettings.asset`; commit it.

| Section | Setting | Default | Effect |
|---|---|---|---|
| Build Interception | Intercept Builds | On | Builds not started by Build Forge are stopped with a message pointing to its build window. See [Concepts](concepts.md#build-interception). |
| Build Manifest | Write Build Manifest | On | Every build gets [the manifest](build-manifest.md). |
| | Log Manifest Contents | Off | Logs the manifest's JSON during the build. |
| Output | Sanitize Product Name | On | Sanitizes `{ProjectName}`. |
| | Reveal Build In File Browser | On | After a successful editor build, shows the built file in Explorer, Finder or the Linux file manager. Never for CI builds, or for Build and Run, which runs the player instead. |
| Profiles | Save Profiles On Edit | On | Writes Build Forge settings to disk right after each edit. |
| | Maintain Build Profile Defines | Off | Keeps the `BUILD_PROFILE_<NAME>` define in each profile. See [The BUILD_PROFILE_ define](concepts.md#the-build_profile_-define). |
| Play Mode | Play Mode Follows Applied Profile | Off | Applying a profile of another platform than Standalone also gives Play Mode, which runs on Standalone's XR settings, that platform's XR loaders and Initialize XR on Startup, and its OpenXR features as OpenXR Features says. See [Apply](concepts.md#apply-and-re-apply). |
| | OpenXR Features | Turn on the platform's | Shown while Play Mode Follows Applied Profile is on. **Turn on the platform's** turns on Standalone's features that the platform has on and leaves the others as they are, so features Standalone has on for Play Mode itself stay on. **Match the platform's** also turns off those the platform has off, so Play Mode has exactly the platform's features where Standalone has them. |
| Build Variants | (list) | Empty | Project-wide variant names. See [Build variants](variants.md). |
| | Variant Rules | Empty | Optional rules per variant. |
| | Mark Variant Builds | On | Shown once variants exist. Suffixes a variant build's product name with the variant. |
| Installed Plugins | One toggle per plugin, sorted by name | On | Off disables the plugin for every profile: it is hidden from the Build Forge section and skipped in builds. Some plugins show global settings under their toggle. A plugin that failed to load follows the others, marked "(failed to load)" with its error; builds and Apply stop while it is on. |
| | Switches for plugins that aren't installed | | Shown while the settings hold a switch for a plugin type that isn't installed, because the plugin was renamed or moved, or its package was removed or is older than the plugin needs. Each entry shows the type name, whether the plugin was switched on or off, and **Remove**, which deletes the switch. Builds ignore these switches. A reinstalled or updated package uses its switch again; a renamed plugin starts from its default, so set it again under the new name. |
| Available Plugins | Install | | Shown while a bundled plugin's Unity package is missing. Adds the package. |

The global settings shown under a plugin's toggle are described on that plugin's page: [Android Keystore Env](plugins/android-keystore-env.md#default-variable-names) has the default variable names, [OpenXR](plugins/openxr.md#interaction-profiles) the Interaction Profiles option.
