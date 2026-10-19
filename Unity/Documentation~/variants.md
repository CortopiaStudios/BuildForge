# Build variants

A variant is a project-wide name, such as `Release` or `Internal`, that you pick per build. Once a project has variants, every build names one of them; a project without variants builds without one. Variants exist so that one Unity Build Profile can produce, for example, both the store build and an internal build with debug tools, without a second Build Profile.

## Defining variants

Add names in **Project Settings > Build Forge > Build Variants**. Each name gives a scripting define, sanitized like the profile define: `Internal` gives `BUILD_VARIANT_INTERNAL`, `Internal QA` gives `BUILD_VARIANT_INTERNAL_QA`. With no variants configured, no `BUILD_VARIANT_` define is set at all.

```csharp
#if BUILD_VARIANT_INTERNAL
    DebugMenu.Enable();
#endif
```

A name is a folder in the output path and an argument on the command line, so it may contain only letters (in any alphabet), digits, spaces, `-` and `_`, and must start with a letter or digit. Names must be unique, must not end in a space, and must not be a device name Windows reserves: `CON`, `PRN`, `AUX`, `NUL`, `COM0` to `COM9` or `LPT0` to `LPT9`, in any casing. Two names that sanitize to the same define, such as `QA Build` and `QA-Build`, are an error.

Nothing picks a variant for a build: the build window and the main toolbar start on the applied variant, and until one is applied or picked, their Apply and Build wait for a pick, as a command-line build without `-forgeVariant` fails.

Adding the first variant changes every build: from then on each build names a variant, so CI commands need `-forgeVariant`, and `{Variant}` in the output path gets the variant's folder. Add the variant you release too, for example `Release` next to `Internal`.

## Variant rules

Without rules, a variant only adds its define. **Variant Rules** in the same settings page change more. A rule names a configured variant and sets:

| Field | Values | Effect on builds |
|---|---|---|
| Development Build | Inherit, Disabled, Enabled | Inherit keeps the Build Profile's own Development Build setting. Disabled turns development off and also clears the profiler connection, deep profiling support, script debugging and wait-for-debugger options. Enabled turns development on and keeps the profile's choice of those options, unless the rule's Build Configuration sets them. |
| Mark Product Name | Inherit, Disabled, Enabled | Inherit follows **Mark Variant Builds**; Disabled and Enabled decide for this variant regardless of it. Disable it for the variant you release. |
| Scripting Defines | A list of defines | Extra defines for this variant, in addition to `BUILD_VARIANT_<NAME>`. |
| Version | A template, or empty | Player Settings > Version for the build, restored afterwards. Empty keeps the version. See [Version](#version). |
| Build Configuration | Development options, IL2CPP, stripping and build output settings, each Inherit by default | Unity's settings for the variant's builds, restored afterwards. See [Build configuration](#build-configuration). |

A typical pair of rules: `Release` with Development Build **Disabled** and Mark Product Name **Disabled**, so the store build is never a development build whatever the Build Profile says and keeps its product name, and `Internal` with Development Build **Enabled** and an extra define such as `ENABLE_DEBUG_MENU`.

The extra defines are owned by the rules. A build or an Apply of one variant removes the other rules' defines from the Build Profile's list and adds its own, keeping every unrelated define. So:

- Put a define that belongs to one variant in that variant's rule, not in Player Settings or in a Build Profile's Scripting Defines, where it would reach every variant.
- Do not list a rule's define in a Build Profile's own Scripting Defines as well; the variant system removes it for the other variants.
- Several rules may list the same define.

Rules are validated as a whole, also the ones for other variants, because each can remove its defines while another variant is built. These stop Apply and builds before anything changes: a rule with an empty or unknown name, two rules for the same variant, a define that is not a single C# preprocessor identifier, a define listed twice in one rule, a define starting with `BUILD_VARIANT_` or `BUILD_PROFILE_`, which belong to Build Forge, a development option turned on in a rule that turns Development Build off, and Wait For Managed Debugger turned on in a rule that turns Script Debugging off. After renaming or removing a variant, update its rule.

## Marking variant builds

With **Mark Variant Builds** on (the default), a variant build's product name gets the variant in parentheses for the duration of the build: `My Game (Internal)` as the app's display name and window title, `My_Game_Internal.apk` as the file name through `{ProjectName}`, and `ProductName` in the manifest. An internal build is then hard to hand out by mistake. Turn marking off for the variant you release, with Mark Product Name **Disabled** in its rule.

The application identifier is not changed, so on a device an internal build replaces the installed store build rather than sitting beside it. The **Example Integration** sample shows a small plugin that adds a suffix to the Android application identifier for chosen variants.

## Version

A rule's **Version** sets Player Settings > Version (`bundleVersion`) for its builds, from a template:

| Placeholder | Value |
|---|---|
| `{Version}` | The version in Player Settings, or the `-forgeVersion` value on the command line |
| `{BuildNumber}` | The [Build Number](plugins/build-number.md) plugin's number when it is enabled for the profile, otherwise the Android version code or iOS build number |
| `{Variant}` | The variant's name |

For example `Release` with `{Version}.{BuildNumber}`, `Development` with `{Version}.{BuildNumber}d` and `China` with `{Version}-cn.{BuildNumber}` give `1.4.0.412`, `1.4.0.412d` and `1.4.0-cn.412`. Any other `{…}` is an error that stops builds and Apply, like the rules' other errors.

The template is filled in after the plugins' pre-build steps, so `{BuildNumber}` is the number Build Number set, and before the [build manifest](build-manifest.md) is written, so its `ProductVersion` is the version as built. The previous version is restored right after the player build. A build without a build number, such as a Windows profile without Build Number, cannot fill in `{BuildNumber}`: it keeps the plain version and logs a warning, and the build window warns beforehand. The build window shows the template filled in for the active profile, with `{BuildNumber}` left as is, and as written for other profiles. Apply does not change the version.

## Build configuration

A rule's **Build Configuration** sets Unity build settings for the variant's builds. Every setting starts at **Inherit**, which keeps the profile's own value. A setting you choose is set for the duration of the build and restored afterwards, like the Development Build flag. Apply never writes them.

| Group | Settings | Where Unity keeps them |
|---|---|---|
| Development | Autoconnect Profiler, Deep Profiling Support, Script Debugging, Wait For Managed Debugger | The Build Profile |
| IL2CPP | C++ Compiler Configuration, IL2CPP Code Generation, IL2CPP Stacktrace Information | Player Settings > Other Settings |
| Stripping | Managed Stripping Level, Strip Engine Code | Player Settings > Other Settings |
| Build Output | Compression Method | The Build Profile |
| Build Output, Android only | Link Time Optimization, Debug Symbols (the level) | The Android Build Profile |

- Unity uses the development options only in development builds, and Wait For Managed Debugger only with Script Debugging. A rule that turns Development Build off and one of them on, or Script Debugging off and Wait For Managed Debugger on, is an error. When the rule leaves Development Build to a profile whose builds are not development builds, the build window and the build log warn that the options go unused.
- The IL2CPP settings and Strip Engine Code matter only to IL2CPP builds.
- The Player Settings are written for the platform being built, to the Build Profile's own Player Settings when it has them, otherwise to Project Settings > Player. They are restored exactly as Unity stored them, so `ProjectSettings.asset` and the Build Profile are unchanged after the build.
- Link Time Optimization and Debug Symbols exist only on Android Build Profiles. Other platforms' builds leave them out, and the build window lists only the settings the selected profile's builds get.
- When a profile's platform has a setting but Build Forge cannot find Unity's field for it, as after Unity renamed one, the build stops before it starts and names the field, instead of leaving the setting out. Update Build Forge for that Unity version.
- **Default** in Compression Method is Unity's Default: no LZ4 compression (in an APK, ZIP). Inherit keeps the profile's choice.

A typical pair: `Release` with C++ Compiler Configuration **Master** and Compression Method **LZ4HC** for the store build, and `Development` with Script Debugging and Autoconnect Profiler on, C++ Compiler Configuration **Debug** and Managed Stripping Level **Minimal**.

The build window shows the selected variant's settings for the profile on its **Build Configuration** line, and the build log lists them at the start of the build.

## Restricting variants per profile

A profile supports every variant unless its Build Forge section turns on **Restrict Variants** and lists the **Allowed Variants**, at least one. The build window's variant menu and the main toolbar list the other variants as unavailable for the profile, and Apply and Build refuse them. **Variant Restriction Reason**, when set, replaces the generic hint in that error, which the build window shows in its details pane and as the disabled buttons' tooltip, and the command line prints.

An empty list, an unknown name or a name listed twice in Allowed Variants is an error. Update the list after renaming or removing a variant.

## Picking a variant

| Where | How |
|---|---|
| Build window | The **Variant** dropdown after the profile's name in the details header, shown once variants exist. The details and the CI Command show that variant, for any selected profile, and Apply, Build and Build and Run use it. The choice is remembered for the editor session; in a new session it starts at the applied variant. Until a variant is applied or picked, the dropdown reads **Pick a variant**, and Apply, Build and Build and Run wait for one. |
| Main toolbar | The apply dropdown lists the variants; picking one applies the active profile with it and makes the build window select it too. **Build** and **Build and Run** build the build window's variant, and wait for a pick like the window's. |
| Command line | `-forgeVariant Internal`, required once the project has variants: a build without it fails and lists the variants rather than picking one. Names must match a configured variant exactly, including case. An unknown or disallowed variant, an empty value, or `-forgeVariant` in a project without variants fails the build. |
| Your scripts | `ForgeBuildRunner.RunBuild(forgeProfile, "Internal")`. |

## Variants in builds

For a build, Build Forge puts the variant's define and its rule's defines, development flags and Build Configuration on the Build Profile, and the Build Configuration's IL2CPP and stripping settings in Player Settings, marks the product name, and undoes all of it after every plugin's post-build step, also when the build fails. If a plugin saves assets during the build, the variant's values can reach the Build Profile file on disk for that time; after an editor crash during a variant build, restore the Build Profile asset from version control.

Plugins see the variant as `ForgeBuildContext.Variant` (null only in a project without variants) and the resolved development mode as `ForgeBuildContext.DevelopmentBuild`.

`{Variant}` in the output path keeps the variants' builds apart: `Builds/Android/Quest/Release/…` and `Builds/Android/Quest/Internal/…`. The manifest's `BuildVariant` is the variant name, or empty when the project has none.

## Variants in the editor

**Apply** with a variant writes the variant's define and its rule's defines to the active Build Profile, so editor scripts and Play Mode compile with them. It does not write the rule's development flags or Build Configuration: they only matter to builds, the editor never compiles with `DEVELOPMENT_BUILD`, and on a committed Build Profile those flags would reach every other machine's builds of the variants that leave them to the profile.

Build Forge records exactly what Apply changed, per user, in `UserSettings/ForgeVariantDefines.asset`. Revert to Baseline, Activate and switching variants undo that change on the profile's current define list, so defines you edited in Unity's Build Profiles window since are kept. A build starts from the list without that change and puts it back afterwards.

A `BUILD_VARIANT_` define on a Build Profile that is not applied on this machine was saved while a variant was applied somewhere else, usually committed by accident. The build window, the Build Forge section and the build log report it; builds recompute the defines anyway; Activate removes it together with the rules' defines. Commit Build Profiles while nothing is applied.

Editor code that has to behave differently per variant during a build, such as a plugin, must read the variant from the build context or its `variant` parameter, not from `#if BUILD_VARIANT_…`: the editor's compiled defines reflect what is applied, not what is being built. Player code uses the defines.
