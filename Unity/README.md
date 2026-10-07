# Build Forge

A configuration-driven, extendable and predictable build system for Unity that extends the built-in Build Profiles.

## Why Build Forge?

Unity's Build Profiles handle scenes, scripting defines, and build options well. But they fall short in areas that matter for production workflows:

- **No visibility into Player Settings differences.** Build Profiles store a complete copy of all Player Settings, making it impossible to see what was intentionally changed. Build Forge uses the platform's default settings as a baseline to surface the differences, making it easy to compare what varies between profiles.
- **No per-profile settings for systems like OpenXR.** XR features, render modes, and interaction profiles are global — you can't configure them differently per Build Profile.
- **No guaranteed restoration of settings.** Unity's `IPostprocessBuildWithReport` doesn't run if the build throws an exception, leaving your project in a dirty state.
- **No build manifest.** There's no built-in way to embed build metadata (profile name, timestamp, plugin data) into the player for runtime access.
- **No build interception.** Nothing prevents a team member from building outside the established pipeline.

Build Forge addresses these gaps without duplicating what Build Profiles already do well.

## Features

### Platform Player Overrides View

Compares a Build Profile's Player Settings against the platform defaults and shows only what's different. Uses YAML-to-YAML comparison for full type coverage — no property types are skipped.

- **Colors** render as swatches
- **Object references** (textures, assets) render as thumbnails
- **Enums** show display names resolved via reflection (forward-compatible with new Unity versions)
- **Integer-backed enums** resolved via reflection on `PlayerSettings` C# properties
- **Booleans** show as Yes/No
- **Nested structures** (e.g. static/dynamic batching) are expanded into individual rows, from the profile's own platform entry
- **Display name overrides** for properties where Unity's internal name differs from the UI label (extensible dictionary)
- **Copy button** to export the diff as a markdown table

Shown inline in the Build Forge section of the Build Profile editor (under a "Player Overrides" foldout). The same view is also available as a standalone window — **Window > Build Forge > Player Overrides** — in its Single Profile mode, with a dropdown to inspect any Unity Build Profile in the project, including ones without Build Forge settings.

The window's **Matrix** mode puts every Unity Build Profile that has Player Settings of its own side by side: one row per Player Setting that differs from Project Settings in any of them, one column per profile. A Build Profile's Player Settings are a full copy, so a later change to Project Settings, such as a version bump, does not reach it; the matrix makes such stale values visible next to deliberate overrides. Each platform's profiles are led by a column with that platform's Project Settings values, since a per-platform setting has one value per platform. That keeps every cell to one value (Windows and macOS profiles get separate groups, because Unity keys some of their settings separately).

- Differing values are highlighted; values that match Project Settings are dimmed (**Show Matching Values** hides them)
- A value that every profile with Player Settings of its own holds, unlike Project Settings, is marked as possibly stale: often a Project Settings change that was not copied into them, sometimes a deliberate shared override
- Profiles without Player Settings of their own build with Project Settings; they are named in their platform's Project Settings column, or under the table when no profile on their platform has its own
- Clicking a profile's name or cell selects its Unity Build Profile; **Copy** exports the matrix as a markdown table

### Build Window

The build window automatically discovers every Build Profile with Build Forge settings in the project:

- **Two panes** laid out like Unity's Build Profiles window: a list on the left with one row per Build Profile with Build Forge settings (platform icon and name, click to select, double-click to open the inspector), details for the selection on the right. Auto-discovered via `AssetDatabase.FindAssets`, sorted by platform then name
- **Row status** — "Active" marks the profile whose Unity Build Profile is active (Unity's state), "Applied" the profile whose plugin settings are applied to the editor (Build Forge's state), and a warning or error icon carries the full issue text as tooltip
- **Details pane** with an icon header for the selected profile: the Unity Build Profile (click to ping), platform, output path (resolved for the active profile, template otherwise), scripting define, issues in full, the plugins that will run for this build in execution order, each with a one-line description of what it will do for a local or CI build (skipped ones greyed with the reason), and the CI command with a copy button
- **Activate \<profile\>** — makes the profile's Unity Build Profile active, like Unity's Switch Profile, after reverting another profile's applied plugin settings to their baseline so nothing stale remains. The profile that is still applied, when Unity was switched away from it without Build Forge, keeps its settings
- **Apply \<profile\>** — for the active profile, writes its plugin-managed settings (such as OpenXR features) and selected variant to the editor so Play Mode matches the target. Play Mode always runs on Standalone's XR settings, so for a profile of another platform, Apply also copies that platform's XR loaders, Initialize XR on Startup and OpenXR feature on/off states to Standalone (**Project Settings > Build Forge > Play Mode Follows Applied Profile**, on by default). Once applied, **Re-apply** stays available to pick up configuration edits, including removing overrides. Drift reports editor changes since the last apply; it does not detect configuration edits. A stored baseline guarantees applying never stacks, and Revert to Baseline undoes it. Failed restores retain their baselines for retry and stop Apply or Activate from proceeding. When a restore cannot succeed at all, for example because the package of the plugin that wrote the setting was removed, the dialog after a failed Revert to Baseline offers **Stop Tracking**: those settings keep their current values and Build Forge no longer restores them. If Unity was switched to another Build Profile without Build Forge while something was applied, the window warns and offers Apply; if a platform profile became active (Unity's Platforms list, `-buildTarget`, a deleted `Library/`), it warns and points to Revert to Baseline. Either way, activating the applied profile again keeps its settings. Build is available for the active profile
- **Validation warnings** — plugins report issues (e.g. missing XR provider) shown per profile
- **Build summary** — logs time, size, and status

**Window > Build Forge > Build**

### Main Toolbar

Two dropdowns in Unity's main toolbar (Unity 6.3's main toolbar API), together one toolbar element named **Build Forge**:

- **Build Profile** — the active Unity Build Profile, or "Platform profile" when Unity is on one of those. Pick another Build Profile with Build Forge settings to activate it, exactly like the build window's **Activate** (another profile's applied plugin settings are reverted first, so nothing stale remains)
- **Apply** — shows what is applied to the active profile, worded like the build window's Editor line: "Not applied", or "Applied" with the applied variant in parentheses ("Applied (Internal)", "Applied (default variant)") and ", drifted" when editor settings changed since. It applies the active profile exactly like the window's **Apply**: pick a variant when the project has variants (the build window then selects that variant too), otherwise **Apply**, or **Re-apply** once applied. **Revert to Baseline** undoes whatever is applied, as in the window, including another profile's settings left applied when Unity switched profiles without Build Forge; the tooltip then carries the window's warning. Shown when there is something to apply (plugin settings such as OpenXR features, or variants) or to revert

Profiles that cannot be activated and variants the profile does not allow are listed disabled, and both dropdowns are disabled in Play Mode, while scripts compile or assets import, and during builds. They are shown by default, although Unity starts package toolbar elements hidden. Hide them like any toolbar element: right-click > **Hide**, or **Build Forge** in the toolbar's ⋮ menu, which also shows them again. Build Forge keeps the choice per user in `UserSettings/ForgeUserPreferences.asset`, so it survives a layout reset. Unity remembers where toolbar elements were placed; hold Ctrl and drag to move them. Unity 6.6 cuts toolbar labels at 120 px, so a long profile or variant name ends in "…" there; hover for the full label.

### Plugin System

Extend the build process with custom pre-build and post-build steps. Plugins are discovered automatically via `[ForgePlugin]` attribute and `TypeCache` — no manual registration.

```csharp
[ForgePlugin]
public class MyPlugin : IForgePlugin
{
    public string DisplayName => "My Plugin";
    public int Order => 650; // between the bundled plugins' multiples of 100
    public bool IsApplicable(BuildProfile profile) => true;

    public void OnPreBuild(ForgeBuildContext context)
    {
        // Apply custom settings before the build
    }

    public void OnPostBuild(ForgeBuildContext context)
    {
        // Restore settings — guaranteed to run even if the build fails
    }

    public void OnDrawProfileGUI(ForgeProfile forgeProfile)
    {
        // Draw per-profile configuration UI in the Build Forge section of the Build Profile editor
    }

    public void OnDrawSettingsGUI()
    {
        // Draw global plugin settings in Project Settings > Build Forge
    }
}
```

Plugin settings are plain `[Serializable]` classes stored on the profile (or globally in `ForgeSettings`); they serialize as ordinary YAML, so profile changes are readable one-line diffs:

```csharp
[Serializable]
class MyConfig { public bool enabled; public int retries = 3; }

var config = forgeProfile.GetPluginConfig<MyConfig>("MyPlugin"); // stored instance, or a default that is not stored
config.retries = 5;
forgeProfile.SetPluginConfig("MyPlugin", config);                 // stores and marks the profile dirty
```

Plugins can also contribute data to the build manifest by implementing `IForgeManifestContributor`, and add a step to an Android build's generated Gradle project by implementing `IForgeGradleProcessor`: Build Forge calls it after the vendors' own Gradle steps (Unity callback order 1000000; Meta's runs at 99999), in plugin order.

- **Per-profile configuration** via `OnDrawProfileGUI` — rendered inline in the Build Forge section of the Build Profile editor, each applicable plugin gets its own section
- **Per-profile enable switch** via `IsEnabled`/`SetEnabled` — return non-null from `IsEnabled` and Build Forge renders the "Enabled" checkbox, grays out the plugin GUI, marks the foldout "(disabled)", and skips the plugin's build hooks when off. Plugins without a switch keep the `null` default
- **Description** — optional one-line summary shown as a tooltip when hovering the plugin's name in plugin lists (profile inspector, Project Settings, Build window)
- **Not-applicable reason** via `NotApplicableReason` — a short sentence shown in the inspector when `IsApplicable` returns false (defaults to a generic message)
- **Build description** via `DescribeBuild(profile, isCI)` — one line saying what the plugin will do in a build of that profile, shown next to it in the build window's "Plugins for this build" list (e.g. "crash reporting off (CI only; local build)")
- **Global settings** via `OnDrawSettingsGUI` — rendered in Project Settings > Build Forge under the plugin's enable/disable toggle

### Variants and XR Loader Selection

Named variants can control development mode, additional scripting defines and
product marking; profiles can restrict which variants are available. The optional
XR Loaders plugin selects ordered providers or explicitly disables XR per profile.
Projects whose profiles share the same loader configuration can continue using
ordinary XR Management settings, including fully OpenXR projects.
See the manual's [Build variants](Documentation~/variants.md) and
[XR Loaders](Documentation~/plugins/xr-loaders.md) pages for configuration, and
[Development~/VARIANTS-AND-XR.md](Development~/VARIANTS-AND-XR.md) for plugin
contracts and validation coverage.

### OpenXR Per-Profile Settings

The included OpenXR plugin configures XR settings and features per Build Profile:

- **All OpenXR settings** auto-discovered via SerializedObject (render mode, depth submission, latency optimization, foveated rendering, etc.) — forward-compatible with new settings in future OpenXR versions
- **Feature overrides** — Enable, Disable, or No Override per feature; or **Only Listed Features**, an allow-list: the ticked features on, every other visible feature off, and hidden features (vendor SDKs' own) left to their packages
- **Interaction profile overrides** — optional, hidden by default. Most projects should enable all supported interaction profiles globally in XR Plug-in Management and let the OpenXR runtime select the right one at launch. Per-profile overrides are available for cases like excluding vendor-specific profiles from a build. Enable in **Project Settings > Build Forge > OpenXR > Interaction Profiles**.
- **Disabled checkbox** per feature showing the effective state at a glance
- Only shown for platforms that have OpenXR settings configured

Settings are captured before the build, applied, and restored in a `finally` block. When a feature is explicitly enabled, its sub-settings can be edited via the native feature editor rendered inline in the Build Forge section. Every visible property of the edited feature is then pinned in the profile — one readable YAML line each — and applied at build time (and when the profile is applied to the editor). The inspector hides settings that a feature enabled in the profile's build also declares, because the feature can overwrite them during the build (Unity's Meta Quest build step copies symmetric projection and a few others from MetaQuestFeature); set them on the feature instead. A hand-written override of one is flagged with a warning. A profile that disables the feature can override the setting, since a disabled feature's build steps don't run.

**Feature Groups are not supported.** Unity's OpenXR Feature Groups (e.g., "Meta Quest Support") are keyed by `BuildTargetGroup`, not Build Profile — multiple profiles sharing a platform (Quest 2 and Quest 3 are both Android) share the same group state. Build Forge works at the individual feature level, which is strictly more powerful. See ARCHITECTURE.md for the full rationale. A group ticked in XR Plug-in Management force-enables its required features, so a profile's Disable override on one of them stops the build with an error naming the group; untick the group and enable its features per profile instead.

**Meta Quest Build Profiles.** Unity's Meta Quest profile type works with Build Forge like a plain Android profile: it builds for Android, with Android's OpenXR features and settings. In Unity 6000.6, URP also strips Quest-only shader variants for it, so its builds can be smaller. Unity's OpenXR package changes the shared Android features around such a profile:

- OpenXR 1.16 enables the Oculus Touch, Meta Quest Touch Pro and Touch Plus interaction profiles on every domain reload while a Meta Quest profile is active, so a profile's Disable override on one of them holds in builds but not in the editor. Build Forge warns about such an override (it exists only with Interaction Profiles enabled).
- OpenXR 1.18 enables those three and Meta Quest Support once, when the editor loads with a Meta Quest profile active, and turns them off on the first load without one. It records this in `ProjectSettings/BuildProfileUtilityOpenXR.asset` (`isMetaQuestInitialized`), and only an interactive editor runs it. A build of a Meta Quest profile therefore enables them itself while the file says OpenXR has not, as in a CI checkout where it was committed as 0 or not at all, and restores the settings afterwards, so CI builds get what the editor would. A plain Android profile warns while the file says OpenXR has enabled them, because the next editor load turns them off.

In an OpenXR 1.18 project, switching between a Meta Quest profile and another Android profile therefore changes `OpenXR Package Settings.asset` and that file. Give each Android profile an Enable or Disable override for Meta Quest Support, so that its builds don't depend on which profile was active last.

**Known limitation**: "Additional Graphics Queue (Vulkan)" and "Offscreen Rendering Only (Vulkan)" are not overridable per-profile. These settings live on Unity's internal `OpenXREditorSettings` class, which is not part of the public API. They're written to the player boot config at build time by Unity's `OpenXRBuildProcessor`. A future Unity OpenXR update exposing these publicly would allow Build Forge to support them.

Conditional on `com.unity.xr.openxr` being installed. The plugin assembly uses `defineConstraints` and `versionDefines` in its `.asmdef` — it is automatically excluded when OpenXR is not present. No manual scripting defines needed.

### Android Keystore Env

Overrides Android keystore signing parameters from environment variables at build time — the third tier in a three-tier signing model:

1. **Unity PlayerSettings** — global keystore path, passwords, key alias
2. **Unity Build Profile** — per-profile Player Settings overrides
3. **Build Forge env var overrides** — reads from environment variables, applied on pre-build, restored on post-build

Four parameters can be overridden, each from a configurable environment variable:

| Parameter | Default Env Var |
|---|---|
| Keystore Path | `ANDROID_KEYSTORE_PATH` |
| Keystore Password | `ANDROID_KEYSTORE_PASSWORD` |
| Key Alias | `ANDROID_KEY_ALIAS` |
| Key Password | `ANDROID_KEY_PASSWORD` |

Default env var names are configured globally in **Project Settings > Build Forge > Android Keystore Env**. Per-profile settings inherit from global by default; each field can be switched to **Override** (custom env var name) or cleared (skip that parameter). If an env var is not set or the name is empty, the existing value from tiers 1/2 is left untouched.

Opt-in per profile via the **Enabled** toggle (off by default). When enabled, unambiguous misconfigurations fail the build before it starts instead of degrading silently:

- An env var that is **set but empty** (a CI secret that failed to populate)
- A custom keystore in use with an **empty keystore path**
- Signing env vars present while **no custom keystore is in play** — the output would be silently debug-signed

Passwords and key alias are deliberately *not* required to be non-empty: they can legitimately come from Player Settings, and gradle fails loudly on a bad value.

Only applicable to Android builds. Passwords are never logged — only the env var *names* appear in the console.

### XR Vendor Filter

One Android project often builds for several headsets (Quest, Pico, Steam Frame), and every installed XR vendor SDK ends up in every Android build: Meta's Platform SDK loader and Pico's in a Steam Frame APK, and Meta's Quest manifest entries in all of them. Meta's Core SDK patches the manifest of every Android build in its Gradle step (the Oculus VR launch category always, supported devices and focus awareness whenever its project config targets a Quest), and Meta's Platform SDK adds its Horizon OS supplement unconditionally. The XR Vendor Filter keeps the SDKs of the packages a profile excludes out of its builds:

- **Libraries are prevented**, not removed: the excluded packages' Android libraries (`.aar`, `.jar`, `.so`, Java and Kotlin sources, `.androidlib`) are never copied into the Gradle project, because Build Forge sets Unity's include-in-build delegate on them after the vendors' own pre-build steps. Managed assemblies stay, so game code that references them still compiles. Code that starts a vendor SDK on the wrong headset must check the platform itself.
- **Manifest entries are removed** after the vendors' Gradle steps, since nothing a profile can set prevents them: `com.oculus.*`, `com.meta.*` and Horizon OS elements when a Meta package is excluded, `com.pico.*` and `com.picovr.*` for Pico. Extra prefixes cover packages without built-in rules.
- **Check for Leftovers** (on by default) then fails the build if an excluded library or a removed entry is still in the Gradle project, including the manifests inside the remaining libraries, which Gradle merges later.

The inspector lists the installed XR vendor SDKs that ship Android libraries by vendor (Meta, Pico, Steam), and folds the other packages that ship them, Unity's OpenXR and XR Plug-in Management among them. Only applicable to Android builds; the build manifest records the excluded packages.

### Android Manifest

Unity reads an Android build's custom main manifest from one path, `Assets/Plugins/Android/AndroidManifest.xml`, for every Android profile. The Android Manifest plugin gives a profile its own: the build puts the profile's file there with Custom Main Manifest on, and restores the project's file and setting afterwards. The vendors' Gradle steps then patch the profile's manifest as usual, and the XR Vendor Filter removes their entries from it.

### Build Runner

Orchestrates builds through Unity 6's profile-aware `BuildPipeline.BuildPlayer(BuildPlayerWithProfileOptions)` with a `try/catch/finally` pattern:

1. Removes the previous build's files from the destination when the profile's **Remove Previous Build Output** option is on (the default), after validating the profile and variant, before plugin discovery or preparation
2. Runs plugin `OnPreBuild` hooks (e.g. OpenXR applies per-profile settings)
3. Writes the build manifest to a Library temp folder that the build pipeline adds to the player's StreamingAssets (the project's Assets folder is never touched)
4. Calls `BuildPipeline.BuildPlayer()` with the Build Profile — scenes, player settings, scripting defines, and build options are handled natively by the profile
5. In `finally`: runs plugin `OnPostBuild` in reverse order, cleans up manifest
6. After a successful build from the editor, shows the built file in the file browser (Explorer, Finder, or the Linux file manager), like Unity's own Build button. Never in CI or batch mode; disable in **Project Settings > Build Forge > Output > Reveal Build In File Browser**

With **Remove Previous Build Output** on, every build first removes the artifact file it is about to write (the APK, AAB or Windows/Linux executable), then the files the previous build of the same profile and variant produced in the destination, including old filenames after a rename and supporting files such as `Game_Data` or the IL2CPP backup folder, and the folders that build created once they are empty. After the build, Build Forge records what it produced: every file and folder that is new in the destination since just before the build, plus the entries of Unity's build report there that the build rewrote. The comparison is what catches files packages write themselves, which the report never lists. The record is per user, in `UserSettings/ForgeBuildOutputs.asset`; nothing is written into the build folder. The destination is the artifact's parent directory for APK/AAB and desktop players, and the output path itself for directory outputs such as WebGL, iOS and Android exports. If the destination changes, the previous build's files are removed from where they were recorded, unless that place is missing, offline, or inside another Unity project or Git checkout that does not contain this project; a file there that cannot be deleted is only reported. Records are tied to the project folder's path, so a copied or moved project never cleans the original's recorded output; if the copy's output path resolves to the very same artifact file, that file is still replaced as usual.

Files that were already in the destination are kept, and so are files changed since the previous build, files another profile or variant recorded there while they still match that record, and anything behind a link or inside a nested Unity project or Git checkout. Writing an APK straight to the Desktop no longer clears the Desktop: the next build removes that APK, what the previous build produced, and anything else saved to the Desktop while that build ran. With no record yet, such as a first build or a fresh CI workspace, only the artifact file is removed; a macOS `.app` bundle and directory outputs are cleaned only from the record, so a failed build without one can leave the old output behind. A destination that contains the project or repository root, lies inside a Unity project's own folders or Git metadata, is itself a Unity project or checkout, overlaps the package source or the Unity editor installation, or is a drive root aborts the build, as do a destination that cannot be read, a file in it that cannot be deleted (usually because the player is still running) and a folder where the artifact file goes (typically an old Android export folder that Android Studio added files to). While a destination is missing or offline (a disconnected share, an ejected drive, a folder behind an unmounted link), nothing is removed and its record is kept until a build records its own output; after a destination change the new destination's record replaces it.

Still use a **dedicated destination directory per profile and variant**: a file that lands in a shared folder while a build runs is recorded as that build's output and removed by the next one if nobody changed it. In CI, files that were already in the destination when a build ran without a record are never removed later; keep the destination inside a workspace the pipeline cleans, persist `UserSettings/ForgeBuildOutputs.asset` between runs (records are tied to the project folder's path, so this only helps when every run checks the project out to the same path), or clean the destination in the job.

**Remove Previous Build Output** is on by default, including for existing profiles. Turn it off in the profile's Build Forge settings only when previous output must be retained, such as files staged by an earlier packaging step. The choice applies to editor and CI builds and all variants of that profile. With it off, Build Forge removes and records nothing; Unity and plugins can still modify existing files, and an old artifact can survive a failed attempt. Keep it on for release jobs unless the surrounding pipeline deliberately manages those files.

In Jenkins, still run uploads only after a successful build. Unity can fail before calling Build Forge (for example during script compilation), and a failed build can leave partial new output. Destination cleanup cannot cover those cases.

Build Forge attempts to restore plugin settings after both successful builds and managed build exceptions; restoration errors fail CI. This improves on Unity's native `IPostprocessBuildWithReport`, which does not run on build failure. An Editor crash or forced process termination can prevent restoration; see [Timeouts, cancellation and one build per checkout](Documentation~/ci.md#timeouts-cancellation-and-one-build-per-checkout).

Builds can also be started from your own editor scripts:

```csharp
using BuildForge.Editor.Core;

var forgeProfile = AssetDatabase.LoadAssetAtPath<ForgeProfile>("Assets/Settings/Release.asset");
var report = ForgeBuildRunner.RunBuild(forgeProfile); // returns Unity's BuildReport
```

`RunBuild` throws `InvalidOperationException` if the profile's Unity Build Profile is not the active Build Profile — activate it first (`BuildProfile.SetActiveBuildProfile`), for the same reason the CLI fails fast (see CI section).

Build Forge runs one profile at a time. Multiple-platform or batch builds belong in an external orchestrator that drives separate Unity instances — see the Roadmap.

### Build Manifest

A JSON file that ends up at `StreamingAssets/BuildForge/BuildManifest.json` in the built player (it is written to `Library/` and handed to the build pipeline, never to the project's Assets folder), readable at runtime:

```csharp
// Async (recommended — non-blocking on Android, required on WebGL)
var manifest = await BuildManifestLoader.LoadAsync();

// Synchronous (blocks briefly on Android, fine on other platforms; not supported on WebGL)
var manifest = BuildManifestLoader.Load();

Debug.Log($"Profile: {manifest.BuildProfileName}");
Debug.Log($"Built at: {manifest.BuildTimestamp}");
Debug.Log($"Development build: {manifest.DevelopmentBuild}, managed code: {manifest.ManagedCodeVariant}"); // variant is empty before Unity 6.6

var openxr = manifest.GetSection("OpenXR");
if (openxr != null)
    Debug.Log($"Features: {openxr.Get("enabledFeatures")}");
```

The OpenXR section also lists each feature pin of the features the profile enables, keyed by feature type and property path, e.g. `UnityEngine.XR.OpenXR.Features.MetaQuestSupport.MetaQuestFeature.m_symmetricProjection`.

The result is cached after the first load; concurrent `LoadAsync` callers share one in-flight load. The manifest is automatically cleaned up from the project after the build completes. Plugins can contribute custom sections via `IForgeManifestContributor`.

### Build Interception

Prevents builds that bypass Build Forge. Any `BuildPipeline.BuildPlayer()` call not initiated through Build Forge is blocked with a `BuildFailedException` directing the user to the Build Forge Build Window.

**Enabled by default** — installing the package changes how builds start. Disable in **Project Settings > Build Forge > Intercept Builds**.

### Scripting Define per Build Profile

Build Forge keeps a `BUILD_PROFILE_<NAME>` scripting define in each referenced Unity Build Profile's own Scripting Defines list (upper-cased name, runs of non-alphanumerics collapsed to one underscore: "Quest 3" gives `BUILD_PROFILE_QUEST_3`; letters outside ASCII are kept, so "Übung" gives `BUILD_PROFILE_ÜBUNG`). Unity applies that list whenever the profile is active, in the editor and in the build, so code can branch at compile time per profile:

```csharp
#if BUILD_PROFILE_QUEST_3
    // Quest 3 only
#endif
```

The define is written whenever Build Forge touches the profile (profile inspector, build window, profile switch) and replaced after a rename; user defines in the list are left alone. Writing to the active profile triggers one recompile. Disable in **Project Settings > Build Forge > Profiles > Maintain Build Profile Defines**. At build time a missing or stale define is only reported, never written.

Sanitizing can make two names coincide ("Quest 3" and "Quest-3" both give `BUILD_PROFILE_QUEST_3`), which would make an `#if` true for both profiles. Build Forge warns about that in the build window and the profile inspector; rename one of the profiles.

At runtime, the build manifest carries the profile name (`BuildManifestLoader`, see Build Manifest below).

### Build Variants

A variant is a project-wide name that adds a scripting define, `BUILD_VARIANT_<NAME>`. Optional rules also configure Unity development options, additional defines, product marking, the version string (for example `{Version}.{BuildNumber}d`, filled in with the Build Number plugin's number during the build) and a Build Configuration (IL2CPP, stripping, compression, and Android's link-time optimization and debug symbols); profiles can restrict which variants they support. See the manual's [Build variants](Documentation~/variants.md) page for the rules, and [Development~/VARIANTS-AND-XR.md](Development~/VARIANTS-AND-XR.md) for the restoration guarantees and real-player verification workflow. Existing projects without rules retain their original behavior. For example, an internal build can compile in debug tools:

1. Add the name in **Project Settings > Build Forge > Build Variants** (for example `Internal`, which gives `BUILD_VARIANT_INTERNAL`; same sanitizing as the profile define).
2. Wrap the internal code in `#if BUILD_VARIANT_INTERNAL`.
3. Pick the variant in the build window's action bar for Build or Apply, or pass `-forgeVariant "Internal"` in CI. The default build (no variant chosen, or `-forgeVariant default`) gets `BUILD_VARIANT_DEFAULT` as soon as any variant exists, so it can be addressed directly too; with no variants configured no `BUILD_VARIANT_` define is set at all. "default" is therefore reserved and cannot be a variant name, and names may not contain `<` or `>`.

For a build, the define and the variant's rule settings (development flags, Build Configuration, aliases) are put on the Build Profile, and in Player Settings for the IL2CPP and stripping settings, for the duration of the build and restored afterwards. A plugin that saves assets mid-build can write them to disk, so after an editor crash during a variant build, restore the Build Profile asset from version control. For editor iteration the define and the rule's aliases are written while the variant is applied and removed by Activate or Revert to Baseline; the rule's development flags and Build Configuration only apply to builds, so a Build Profile committed while a variant is applied does not turn other machines' Default builds into development builds. A `BUILD_VARIANT_` define on a Build Profile that is not applied on your machine means it was saved while a variant was applied elsewhere: the build window, the inspector and the build log warn, and Activate removes it with the rules' aliases. Use `{Variant}` in the output path so variant builds do not overwrite the default build: the default template has it, and once any variant exists the default build expands it to `Default`, so `Builds/Android/Quest/Default/…` and `Builds/Android/Quest/Internal/…` are siblings (with no variants configured the empty segment collapses). The manifest's `BuildVariant` follows the same rule: `Internal`, `Default`, or empty when the project defines no variants. A profile supports every variant unless its Build Forge settings turn on **Restrict Variants** and list the allowed ones.

By default a variant build is also **marked in its product name** for the duration of the build: `Game (Internal)` as the app display name and window title, `Game_Internal.apk` as the file name (via `{ProjectName}`), and `ProductName` in the manifest, so an internal build is hard to distribute by mistake. The default build is never marked. The application identifier is not changed, so an internal build replaces the installed public build on a device rather than sitting beside it. Disable in **Project Settings > Build Forge > Build Variants > Mark Variant Builds**.

### CI / Command Line Builds

Build from the command line or CI using the standalone Unity CLI:

```sh
unity run "./Client" --non-interactive -- -nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Build -activeBuildProfile "Assets/Settings/ReleaseBuildProfile.asset"
```

Copy the command from the build window's **CI Command** field for the selected profile and variant: **One workspace per job**, or **Shared workspace** for a workspace that also builds other platforms' profiles. Run it from the repository root. Subfolder project paths start with `./` so Unity CLI treats them as paths rather than registered project names; a project at the repository root uses `.`. These relative paths work across Windows, macOS and Linux. Outside Git, the command uses the absolute project path. Unity CLI reads `ProjectSettings/ProjectVersion.txt` and finds the matching installed Editor. No executable-path resolver or `com.unity.pipeline` package is required.

- Provision the standalone `unity` CLI on PATH, an activated matching Editor and the profile's platform modules under the CI account. The tested CLI is **1.0.0-beta.11** with Unity **6000.3.23f1** on Windows. Verify `unity --version` under the agent account, pin that CLI version in the agent image and revalidate before updating it. Use the standalone CLI selected by PATH; Unity Hub may bundle a different version internally. Clear `UNITY_EDITOR_VERSION` unless an intentional version override is required. `--non-interactive` prevents prompts; installation and licensing remain agent setup steps.
- CLI supplies `-batchmode`, `-projectPath` and `-quit`. Do not forward these after `--`; reserved flags are rejected. Build Forge's entry point completes the build and all cleanup synchronously, then explicitly exits with the result. Required plugin work must finish before its hook returns; delayed callbacks and `async void` cannot be used for completion or restoration.
- `-nographics` omits the graphics device; `-silent-crashes` suppresses crash dialogs; `-logFile -` sends the Editor log to CI stdout. Replace `-` with a path to retain a separate log file. `-cacheServerWaitForUploadCompletion` waits for pending Accelerator uploads at shutdown. Verify Accelerator connectivity under the actual CI account.
- `-activeBuildProfile` selects the **Unity Build Profile** before compilation. It must have Build Forge settings, exactly one set; otherwise the build fails. Those settings supply output paths, signing, plugin configuration and content policy.
- A workspace that also builds profiles of other platforms, such as a job with a profile parameter, activates the profile first, in a run of its own: `-executeMethod BuildForge.CommandLine.Activate -forgeBuildProfile "<path>"`, then the build command, as the **Shared workspace** command shows. Across a platform change, Unity 6000.3.23's `-activeBuildProfile` compiles the new profile's defines for the previous platform first, which fails when code under them needs a platform-only assembly. See the manual's Command line and CI page.
- Optional `-forgeVariant "Internal"` selects a variant; an unknown or unsupported variant fails, and so does an empty value, such as an unset CI variable produces. Omit it for Default, or pass `-forgeVariant Default` explicitly.
- Optional `-forgeVersion "1.2.3"` and `-forgeVersionCode 42` override Unity's bundle version and Android version code for this build. They are restored afterwards, including on failure. Unity's `-version` flag prints the Editor version and exits; it is not a build-version override.
- Build Forge's arguments match case-insensitively. An argument starting with `-forge` that Build Forge does not take (a typo), a Build Forge argument without a value, and a `-forgeCI` value or non-empty `FORGE_CI` other than true/false, 1/0 or yes/no fail the build with exit code 1 before it starts. `-versionCode` was renamed `-forgeVersionCode` and fails the same way.
- A successful build and cleanup exits zero. Treat **every nonzero launcher result as failure**, and compress or publish only after zero. The CLI maps Editor failures to exit `6` (verified with beta.9 and beta.11); the original Editor code remains in the error message. Cleanup errors fail CI even when a player artifact was produced.
- For a bounded job, add `--timeout <seconds>` before `--`, allowing sufficient time for imports, the full build and cache uploads. A forced timeout or process crash cannot guarantee cleanup; discard or recover the affected workspace before reuse. Run only one Unity process per checkout.

**Known Windows cancellation issue:** with Unity CLI **1.0.0-beta.9** and
**beta.10**, cancelling `unity run` can leave its Editor running and prevent the
next build from opening the same project. The generated command stays as shown
above. See the manual's [Command line and CI](Documentation~/ci.md#timeouts-cancellation-and-one-build-per-checkout)
page for recovery, and [Development~/CI-CANCELLATION.md](Development~/CI-CANCELLATION.md)
for the measured behavior and the unresolved Jenkins cleanup question.

**Why Build Forge uses `unity run`**

Build Forge needs both `-activeBuildProfile` at Editor startup, so the selected profile's settings and scripting defines apply before compilation, and `-executeMethod BuildForge.CommandLine.Build` to run Forge's build pipeline. Although `unity build` supports custom execute methods, its native-profile and custom-method builds are separate strategies ([Unity CLI reference](https://github.com/Unity-Technologies/skills/blob/main/skills/unity-cli/references/build-run-test.md#build)).

Verified with standalone CLI **1.0.0-beta.9** on Windows on **2026-09-16**, and unchanged in **1.0.0-beta.11** on **2026-09-29**:

- Combining `--profile` with `--execute-method` fails: `--profile and --execute-method cannot be combined. Pass one build strategy.`
- Using `--target` and `--execute-method`, then forwarding `-activeBuildProfile` through `--args`, also fails: `Forwarded argument '-activeBuildProfile' conflicts with a reserved Unity flag managed by this command.`

Both restrictions are enforced by the CLI before the Editor starts; invocations targeting the Unity 6000.3.0f1 and 6000.6.0f1 test projects produced the same errors. `unity run` allows both Editor arguments, so the generated command selects the native profile and executes Build Forge together. Recheck these restrictions before considering a switch to `unity build` with a future CLI version.

Run Unity Test Framework tests separately with `unity test`, which manages their completion.

Direct Editor invocations remain supported for existing automation:

```sh
Unity -batchmode -nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Build -projectPath "Client" -activeBuildProfile "Assets/Settings/ReleaseBuildProfile.asset"
```

Replace `Unity` with the installed Editor executable. An explicit `-quit` is unnecessary for this direct form because Forge exits after cleanup. `ForgeBuildRunner.StaticCIArguments` is retained for existing callers; `GetCICommand` now generates the CLI form.

For multi-platform or batch builds, drive separate Unity invocations from the CI job. Neither form enables automatic source rewriting with `-accept-apiupdate`; resolve and commit API migrations before building.

## Installation

Add the package to your Unity project in the Package Manager from the git URL `https://github.com/CortopiaStudios/BuildForge.git?path=/Unity` (the package is in the repository's `Unity` folder). Append `#` and a tag or commit to pin a version. Or copy the `Unity` folder into your project as `Packages/com.cortopiastudios.buildforge/`.

Requires Unity 6000.3 or later. The package depends on two of Unity's built-in modules, JSON Serialize (the build manifest) and UnityWebRequest (reading the manifest on Android and WebGL), and the Package Manager enables them in a project that had them disabled.

## Setup

1. **Add Build Forge settings to a Unity Build Profile**: select the Build Profile asset and use Assets > Build Forge > Add Build Forge Settings (also in the Project window's context menu, and as **Add Build Forge Settings** in the Build Profile's "Build Forge" section). The settings are stored inside the Build Profile's `.asset`, so they share its name and file, and renaming, duplicating or deleting the Build Profile carries them. Each Build Profile has at most one; variants that differ only in plugin settings need their own Unity Build Profile
2. **Edit them** in the "Build Forge" section of the Build Profile, last in Unity's Build Profiles window and in the Inspector when the Build Profile asset is selected, which ends with **Remove Build Forge Settings**; also Assets > Build Forge > Edit Build Forge Settings or **Open in Inspector** in the build window, which show them on their own in the Inspector.
3. **Configure output path** (optional) — supports `{ProjectName}`, `{ProfileName}` (the Unity Build Profile's name), `{Target}` and `{Variant}` placeholders (default: `Builds/{Target}/{ProfileName}/{Variant}/{ProjectName}`; `{Variant}` is `Default` for the default build once variants exist and collapses when none are configured). A profile stores a path only once you customize it; otherwise it follows the default, and Reset returns to it. The window, the output path, the scripting define and the manifest all use the Unity Build Profile's name
4. **Configure plugins** (e.g. OpenXR feature overrides) directly in the Build Forge section. Edits are written to the Build Profile's `.asset` file immediately, so the YAML always matches the inspector (opt out in **Project Settings > Build Forge > Profiles > Save Profiles On Edit**)
5. **Build** via Window > Build Forge > Build — select the profile, click "Activate \<profile\>" if it isn't active already, optionally "Apply \<profile\>" to iterate in Play Mode, then Build

### Fresh Clones

Unity keeps the active Build Profile in `Library/`, and Build Forge keeps what is applied, its variant ledger and the record of previous build output in `UserSettings/`. Neither folder is committed (both are in Unity's standard `.gitignore`), so a fresh clone opens on Unity's default platform profile with nothing applied, and imports every asset for that platform. Build Forge itself needs no first-open step; its build window enables Build once a profile is activated.

- **Import once, for the right platform:** open the project the first time with `-activeBuildProfile "Assets/Settings/Build Profiles/<Profile>.asset"` (Unity Hub: the project's ⋯ menu > Add command line arguments). The profile is active and its defines compile when the editor opens, and the only import runs for its platform; `-buildTarget <target>` also imports once, with that platform's platform profile active. Remove the argument afterwards; the Hub saves it with the project. Verified on 6000.3.0f1 and 6000.6.0f1.
- **Apply** the profile in the Build Forge window if Play Mode should match its plugin settings.
- **Commit settings only while nothing is applied.** Applying writes version-controlled assets (XR settings, a variant's defines on the Build Profile), and no other machine or CI can tell those values from the baseline. The build window reminds you while a profile is applied, and a variant define committed this way is reported on other machines (see [Build Variants](#build-variants)).

### Output Path Sanitization

The `{ProjectName}` placeholder uses `Application.productName`, which often contains spaces and special characters that cause problems with build tools, CI scripts, and path handling across platforms. By default, Build Forge sanitizes the product name when resolving `{ProjectName}`:

| Character | Replacement | Example |
|---|---|---|
| Space | `_` (underscore) | `My Game` → `My_Game` |
| `&` | `and` | `Cats & Dogs` → `Cats_and_Dogs` |
| `: ( ) ' " ! .` | removed | `Game: Remastered` → `Game_Remastered` |
| Other non-alphanumeric | `_` (underscore) | |
| Consecutive `_` or `-` | collapsed to one | `My___Game` → `My_Game` |
| Leading/trailing `_` `-` | trimmed | |

This prevents common issues: spaces breaking shell commands in CI, colons being invalid in Windows paths, and special characters causing problems with tools like `adb push`, `fastlane`, or `steamcmd`.

Toggle in **Project Settings > Build Forge > Output > Sanitize Product Name** (enabled by default).

Scenes, scripting defines, build options, and Player Settings are configured on the Unity Build Profile itself — Build Forge doesn't duplicate these. Use the Platform Player Overrides view to see what a Build Profile changes from the platform defaults.

## Project Structure

```
Runtime/
  BuildManifest.cs                         # JSON manifest, readable at runtime
  BuildManifestLoader.cs                   # Loads manifest from StreamingAssets

Editor/
  CommandLineBuild.cs                      # CI entry points (BuildForge.CommandLine.Build and Activate)
  Core/
    ForgeBuildRunner.cs                    # Build orchestrator with try/finally
    ForgeBuildInterceptor.cs               # Blocks non-Build Forge builds
    ForgeBuildContext.cs                   # Context passed to plugins (incl. IsCI)
    BuildEnvironment.cs                    # CI detection (-forgeCI / FORGE_CI / batch mode)
    BuildManifestWriter.cs                 # Writes/cleans up manifest
    UnityYamlParser.cs                    # Line-based YAML parser for Unity assets
    PlayerSettingsDiffComputer.cs          # Computes diffs between profile and platform YAML
    PlayerSettingsMatrix.cs                # All profiles' differences side by side, per platform
    SerializedPropertySnapshot.cs          # Property-path capture/apply (OpenXR feature pins)
    ForgeProfileLookup.cs                  # 1:1 Build Forge settings <-> Unity Build Profile rule
    ForgeEditorState.cs                    # Profile switch entry point (baseline, drift, revert)
    EditorStateApplier.cs                  # Pure apply/revert/drift logic over the ledger
    IForgePlugin.cs                        # Plugin interface (with Validate + GUI hooks)
    IForgeEditorApplicable.cs              # Optional plugin capability: apply to editor
    IForgeManifestContributor.cs           # Manifest contribution interface
    IForgeGradleProcessor.cs               # Optional plugin capability: a step in the Android Gradle project
    IForgePlayModeMirror.cs                # Internal: gives Play Mode (Standalone) an applied profile's platform XR
    ForgeGradleCallback.cs                 # Forwards Unity's Gradle callback to the build's plugins
    ForgePluginAttribute.cs                # Auto-discovery attribute
    ForgePluginRegistry.cs                 # TypeCache-based plugin discovery
  Configuration/
    ForgeProfile.cs                        # ScriptableObject extending Build Profile
    ForgeSettings.cs                       # Global settings (singleton)
    ForgeEditorStateStore.cs               # Per-user applied state, keyed on the Unity Build Profile (UserSettings/)
    ForgeUserPreferences.cs                # Per-user preferences for this project, e.g. the main toolbar (UserSettings/)
    DiffEntry.cs                           # Diff data model
  Plugins/                                 # One folder per plugin, its config beside it
    GitHelper.cs                           # Shared git process runner (Git Metadata, Build Number)
    GitMetadata/
      GitMetadataPlugin.cs                 # Git branch/commit/describe/tag in manifest
      GitMetadataConfig.cs                 # Per-profile git metadata config
    BuildNumber/
      BuildNumberPlugin.cs                 # Build numbers from git commits or env var
      BuildNumberConfig.cs                 # Per-profile build number config
    AndroidKeystoreEnv/
      AndroidKeystoreEnvPlugin.cs          # Override keystore settings from env vars
      AndroidKeystoreEnvConfig.cs          # Global + per-profile keystore env config
    CloudDiagnostics/
      CloudDiagnosticsPlugin.cs            # Crash reporting / symbol upload: Always, CI only, Never
      CloudDiagnosticsConfig.cs            # Per-profile crash reporting mode
    AndroidManifest/
      AndroidManifestPlugin.cs             # Per-profile custom main manifest, swapped in for the build
      AndroidManifestConfig.cs             # Per-profile manifest file
    XRVendorFilter/
      XRVendorFilterPlugin.cs              # Keep excluded vendor SDKs' libraries and manifest entries out
      XRVendorFilterConfig.cs              # Per-profile excluded packages, extra prefixes, leftover check
      XRVendors.cs                         # Known vendors' packages and manifest entries; package detection
      VendorManifestStripper.cs            # Removes vendor entries from the Gradle project, finds leftovers
    OpenXR/                                # Conditional on com.unity.xr.openxr
      OpenXRPlugin.cs                      # Feature toggle + settings overrides per profile
      OpenXRProfileConfig.cs               # Per-profile OpenXR configuration data
      OpenXRGlobalConfig.cs                # Global OpenXR plugin settings
      OpenXRSettingsState.cs               # Shared capture/apply/restore core (build + editor)
      PlayModeOpenXRFeatures.cs            # Play Mode mirror: OpenXR feature on/off states to Standalone
      OpenXREditorStateSnapshot.cs         # Baseline/expected snapshot for profile switching
    Addressables/                          # Conditional on com.unity.addressables
      AddressablesStripperPlugin.cs        # Strip groups/labels from builds
      AddressablesStripperConfig.cs        # Per-profile stripper config
      AddressablesRebuildPlugin.cs         # Rebuild Addressables before building
      AddressablesRebuildConfig.cs         # Per-profile rebuild config
    XRManagement/                          # Conditional on com.unity.xr.management
      XRLoaderSelectionPlugin.cs           # Ordered XR loader selection per profile, with its config
      PlayModeXRLoaders.cs                 # Play Mode mirror: XR loaders and Initialize XR on Startup to Standalone
  UI/
    ForgeBuildWindow.cs                    # Build window with auto-discovery
    ForgeMainToolbar.cs                    # Build Profile and apply dropdowns in the main toolbar
    ForgeProfileEditor.cs                  # Custom inspector with inline plugin UI
    ForgeSettingsProvider.cs               # Project Settings integration
    PlayerSettingsDiffView.cs              # Reusable Player Overrides diff UI
    PlayerSettingsDiffWindow.cs            # Standalone Player Overrides window
    PlayerSettingsMatrixView.cs            # The window's Matrix mode
  Icons/
    ForgeProfile.png                       # Custom icon for ForgeProfile assets and the build window, light theme
    d_ForgeProfile.png                     # The same for the dark theme (Unity's d_ naming)

Documentation~/                            # User manual (start at index.md)

Samples~/
  ManifestReader/
    ManifestDisplay.cs                     # Example: read manifest at runtime
  ExampleIntegration/
    Editor/ApplicationIdSuffixPlugin.cs    # Example plugin: application ID suffix for variant builds
    CI/                                    # Jenkinsfile, GitHub Actions workflow, PowerShell and bash wrappers

Tests/
  Editor/
    Fixtures/                              # Test data (ProjectSettings.txt)
    AndroidKeystoreEnvConfigTests.cs       # Keystore env resolution and validation
    BuildEnvironmentTests.cs               # CI detection precedence
    BuildManifestTests.cs                  # Manifest serialization tests
    BuildManifestSectionTests.cs           # Manifest plugin sections
    BuildNumberConfigTests.cs              # Build number config defaults
    CloudDiagnosticsConfigTests.cs         # Crash reporting mode resolution
    EditorStateApplierTests.cs             # Apply/revert/drift invariant with fake plugins
    EditorStateLedgerTests.cs              # Ledger key/entry helpers
    ForgePluginContractTests.cs            # Contract every bundled plugin must meet
    ForgeProfileLookupTests.cs             # 1:1 rule: resolve and shared-profile errors
    ForgeProfileOutputPathTests.cs         # Output path placeholders and extensions
    ForgeSettingsPluginToggleTests.cs     # Plugin enable/disable precedence tests
    GitHelperTests.cs                     # Git process runner tests
    MainToolbarTests.cs                    # Main toolbar menus and labels
    MangleForFilenameTests.cs             # Output path sanitization tests
    NormalizeValueTests.cs                 # Diff value normalization
    PlayerSettingsDiffComputerTests.cs     # Diff computation tests
    PlayerSettingsMatrixTests.cs           # Matrix rows, columns and stale hints
    XRVendorFilterTests.cs                 # Vendor rules, manifest stripping, leftover scan
    AndroidManifestPluginTests.cs          # Manifest swap and restore
    ForgeGradleCallbackTests.cs            # Gradle step dispatch to plugins
    PlayerSettingsProfileTests.cs          # Real Build Profile diffs (skip in a clean host)
    PluginConfigStorageTests.cs            # Config storage contract + YAML round trips
    PluginRegistryTests.cs                 # Plugin discovery tests
    PublicApiSurfaceTests.cs               # Locks the public API surface
    SerializedPropertySnapshotTests.cs     # Property capture/apply round trips
    UnityYamlParserTests.cs               # YAML parser tests with real fixture
```

## Roadmap

### Player Settings Editor with Better Categorization

Unity's Player Settings UI groups most properties under a catch-all "Other Settings" section. Build Forge could provide an alternative editor with logical groupings (Rendering, Scripting, platform-specific, etc.) when the user opts out of Build Profile Player Settings. This would only be active when the Build Profile has no Player Settings of its own, avoiding two sources of truth.

### Build Forge Settings Inheritance

Allow a Build Profile's Build Forge settings to inherit from another Build Profile's, enabling inheritance of plugin configurations and (when using Build Forge-managed Player Settings) settings overrides. A base profile could define shared settings across Dev, Staging, and Release profiles, with children overriding only what's different.

The rule: if a Unity Build Profile has its own Player Settings, Build Forge Player Settings are disabled for that profile — keeping exactly one source of truth.

### Per-Profile Package Management

See ARCHITECTURE.md for the full design discussion. The intended approach stores a full `manifest.json` snapshot per profile and applies it on profile switch. For code-only exclusion today, use `defineConstraints` on assembly definitions gated by per-profile scripting defines. To keep an XR vendor SDK's Android libraries and manifest entries out of a profile's builds, use the [XR Vendor Filter](#xr-vendor-filter).

### Project Auditor Integration

A Build Forge plugin that integrates with Unity's Project Auditor (`com.unity.project-auditor`) to run asset and code validation before each build. The plugin would compare the audit report against a saved baseline and detect regressions — new warnings, increased texture sizes, uncompressed audio, etc. Optionally fail the build if new issues are found. The manifest could include an audit summary for tracking quality over time.

### Unity CLI Command Integration

Unity's standalone CLI (2026) with the experimental `com.unity.pipeline` package exposes `[CliCommand]`-attributed static methods as terminal commands (`unity run --command`, `unity command`) and as MCP tools for AI agents (`unity mcp`). A small optional assembly — versionDefines-gated on `com.unity.pipeline`, the same pattern as the OpenXR/Addressables plugins — could expose `forge.build`, `forge.profiles`, and `forge.validate`, making Build Forge builds first-class CLI commands and agent-accessible for free. Deferred until the pipeline package stabilizes (it is explicitly experimental).

### Out-of-Process Build Orchestrator

A lightweight local CI layer that manages builds outside the Unity process. Since Unity's standalone CLI now covers editor installation/version selection, headless invocation, test running, and machine-readable output, this shrinks to a thin script over `unity`: build queuing, multi-platform fan-out across separate project clones, secrets injection (feeding the env-var-driven plugins), version bumping with atomic commits, and artifact handling.

## License

MIT — see [LICENSE.md](LICENSE.md) for details.

See [ARCHITECTURE.md](ARCHITECTURE.md) for design decisions, technical pitfalls, and how the system works internally. See [Development~/LEARNINGS.md](Development~/LEARNINGS.md) for approaches that were tried and abandoned during development.

The user manual is in [Documentation~/index.md](Documentation~/index.md).
