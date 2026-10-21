# Writing plugins

A plugin is a class that takes part in every build of the profiles it applies to: it can change settings before the player build, restore them afterwards, store per-profile and project-wide configuration, draw UI in the Build Forge section, warn in the build window, and add data to the manifest. The bundled plugins use the same interface.

The **Example Integration** sample contains a complete plugin, Application ID Suffix, with most parts this page describes; it has no Gradle step, applies nothing to the editor and has no project-wide settings.

## A minimal plugin

```csharp
using System;
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor;
using UnityEditor.Build.Profile;

[Serializable]
public sealed class BundleVersionSuffixConfig
{
    public bool enabled;
    public string suffix = "-dev";
}

[ForgePlugin]
public sealed class BundleVersionSuffixPlugin : IForgePlugin
{
    const string Key = "MyStudio.BundleVersionSuffix";
    const string OriginalKey = Key + ".Original";

    public string DisplayName => "Bundle Version Suffix";
    public string Description => "Append a suffix to the version string of development builds.";
    public int Order => 450; // after Build Number (400)

    public bool IsApplicable(BuildProfile profile) => true;

    public bool? IsEnabled(ForgeProfile forgeProfile) => Config(forgeProfile).enabled;

    public void SetEnabled(ForgeProfile forgeProfile, bool enabled)
    {
        var config = Config(forgeProfile);
        config.enabled = enabled;
        forgeProfile.SetPluginConfig(Key, config);
    }

    public void OnPreBuild(ForgeBuildContext context)
    {
        if (!context.DevelopmentBuild)
            return;
        // Record first: OnPostBuild also runs when this method throws.
        context.SetProperty(OriginalKey, PlayerSettings.bundleVersion);
        PlayerSettings.bundleVersion += Config(context.ForgeProfile).suffix;
    }

    public void OnPostBuild(ForgeBuildContext context)
    {
        var original = context.GetProperty<string>(OriginalKey);
        if (original != null)
            PlayerSettings.bundleVersion = original;
    }

    public void OnDrawProfileGUI(ForgeProfile forgeProfile)
    {
        var config = Config(forgeProfile);
        var suffix = EditorGUILayout.TextField("Suffix", config.suffix);
        if (suffix == config.suffix)
            return;
        config.suffix = suffix;
        forgeProfile.SetPluginConfig(Key, config);
    }

    static BundleVersionSuffixConfig Config(ForgeProfile forgeProfile) =>
        forgeProfile.GetPluginConfig<BundleVersionSuffixConfig>(Key);
}
```

## Where the code goes

Put plugins in an editor assembly: an assembly definition with **Editor** as its only platform that references `BuildForge.Editor`, and `BuildForge.Runtime` if the plugin contributes to the manifest. An `Editor` folder without an assembly definition works too, since Build Forge's assemblies are auto-referenced.

The package targets C# 9. Plugins can use only Build Forge's public API, listed below. That is all the bundled plugins use too: each of them has an assembly of its own, built like yours. Where Unity's own API lacks something, such as the build target of a Build Profile that is not active, `BuildProfileUtility` has it.

## How plugins are found

Build Forge finds every class with `[ForgePlugin]` that implements `IForgePlugin`, through Unity's `TypeCache`; no registration is needed. Abstract classes are skipped, and a class with the attribute that does not implement the interface is skipped with a warning.

- Each plugin class needs a public parameterless constructor. Build Forge creates one instance and reuses it for every build until the next domain reload, so reset per-build state at the start of `OnPreBuild`, or keep it in the build context (see below).
- **Installing a plugin must not change any build.** Until a profile turns the plugin on or configures it, the plugin leaves builds as they were. Give a plugin that acts by itself [a per-profile switch](#the-per-profile-switch) that starts off, or have it act only on what a profile configures, as the bundled plugins do. A project's own plugin, meant for every build of that project, can do without.
- Plugins are enabled for the whole project by default, which makes them available to every profile. `[ForgePlugin(EnabledByDefault = false)]` makes a plugin start disabled until someone enables it in **Project Settings > Build Forge > Installed Plugins**. Use it for a plugin that can't follow the rule above.
- A plugin turned off there is hidden from the Build Forge section and skipped by builds, but can still restore editor settings it applied before.

## The interface

| Member | Default | Called |
|---|---|---|
| `string DisplayName` | Required | Everywhere the plugin is named. |
| `string Description` | `null` | Tooltip of the plugin's name in plugin lists. |
| `int Order` | `0` | Sorting; lower runs first. See [Execution order](#execution-order). |
| `bool IsApplicable(BuildProfile)` | Required | Whether the plugin can work with this Unity Build Profile at all. |
| `string NotApplicableReason(BuildProfile)` | `null` | Shown in the foldout when `IsApplicable` is false. |
| `bool? IsEnabled(ForgeProfile)` | `null` | The per-profile switch; `null` means the plugin has none. |
| `void SetEnabled(ForgeProfile, bool)` | Does nothing | Persists the switch; required when `IsEnabled` is not null. |
| `void OnPreBuild(ForgeBuildContext)` | Required | Before the player build, in order. |
| `void OnPostBuild(ForgeBuildContext)` | Required | After the player build, in reverse order, also when the build failed. |
| `IReadOnlyList<string> Validate(ForgeProfile)` and `Validate(ForgeProfile, string variant)` | No warnings | Warnings in the build window. |
| `string DescribeBuild(ForgeProfile, bool isCI)` and `DescribeBuild(ForgeProfile, bool isCI, string variant)` | `null` | One line in the build window's plugin list. |
| `void OnDrawProfileGUI(ForgeProfile)` | Nothing | The plugin's foldout in the Build Forge section. |
| `void OnDrawSettingsGUI()` | Nothing | Under the plugin's toggle in Project Settings > Build Forge. |

The variant overloads call the plain ones by default, so implement whichever you need. Three optional interfaces add more: `IForgeManifestContributor`, `IForgeEditorApplicable` and `IForgeGradleProcessor`.

### The per-profile switch

Return non-null from `IsEnabled` and Build Forge handles the switch: it draws the **Enabled** checkbox, calls `SetEnabled` when it changes, greys out the plugin's settings and marks the foldout **(disabled)** while it is off, and skips the plugin's build steps for that profile. The plugin's own code needs no check. Start the switch off in a new configuration, so that adding the plugin to a project changes no existing build; see [How plugins are found](#how-plugins-are-found).

## Execution order

| Order | Bundled plugin |
|---|---|
| 100 | XR Loaders |
| 200 | OpenXR |
| 300 | Git Metadata |
| 400 | Build Number |
| 500 | Cloud Diagnostics |
| 600 | Android Keystore Env |
| 700 | Addressables Stripper |
| 800 | Addressables Rebuild |
| 900 | Android Manifest |
| 1000 | XR Vendor Filter |

Pre-build steps run from the lowest order to the highest, post-build steps in reverse, so a plugin that changes something early restores it late. Give a plugin a value between the bundled multiples of 100 that places it after what it depends on, for example 450 to read the build number Build Number set, or 650 to run before the Addressables plugins. Values from 1 to 99 run before every bundled plugin, values above 1000 after all of them, and the default of 0 runs first. Plugins with the same order run in no defined order. Note the reason for the value next to it in the code.

Plugins that select XR providers must run before 200; plugins that inspect XR settings or generate content should run after 200.

The selected variant's settings are in place before the first plugin runs and are undone after the last one has finished.

## The build, from a plugin's point of view

- The profile being built is always the active Unity Build Profile. Unity's static `PlayerSettings` API reads and writes the settings the build uses: the profile's own Player Settings when it has them, otherwise Project Settings. Build Forge saves the restored Player Settings after all post-build steps.
- Record what you change before changing it, and restore exactly that in `OnPostBuild`. Build Forge calls a plugin's `OnPostBuild` also when its own `OnPreBuild` threw halfway, so restore only what was changed.
- An exception in `OnPreBuild` stops the build; the post-build steps of every plugin that started still run. An exception in `OnPostBuild` is logged, the other post-build steps still run, and a command-line build fails.
- Finish all work before returning. The command line exits as soon as the build and cleanup are done, so `async void`, `EditorApplication.delayCall` and coroutines cannot be used for required work or restoration.
- When you change an asset the build reads from disk, such as XR settings, save it before returning, and save the restored state in `OnPostBuild`.
- Do not keep Unity object references from before the player build for use afterwards: the build unloads unused assets and can destroy them. Keep GUIDs or paths and load the objects again in `OnPostBuild`.
- A killed or crashed editor runs no post-build step. Changes that reach disk during the build then stay; that is what version control is for.

### ForgeBuildContext

Plugins receive the context; they cannot create one.

| Member | Value |
|---|---|
| `BuildProfile` | The Unity Build Profile being built. |
| `ForgeProfile` | Its Build Forge settings. |
| `BuildTarget`, `BuildTargetGroup` | The platform. |
| `OutputPath` | The resolved output path, with extension. |
| `Variant` | The variant name, or null in a project without variants. |
| `VariantDefine` | `BUILD_VARIANT_<NAME>`, or null in a project without variants. |
| `DevelopmentBuild` | Whether this is a development build, after the variant's rule. |
| `IsCI` | Whether the build counts as a CI build; see [CI detection](ci.md#ci-detection). Outside a build, `BuildEnvironment.IsCI` gives the same answer, for warnings and descriptions. |
| `BuildNumber` | The build's number, or null. A plugin that assigns the build number sets it in `OnPreBuild`, as Build Number does; a variant rule's `{BuildNumber}` and the manifest's `buildNumber` read it. |
| `Succeeded` | In `OnPostBuild`: whether the player build succeeded. |
| `SetProperty(key, value)`, `GetProperty<T>(key, default)` | Values kept for the duration of one build, shared by all plugins. Prefix keys with your plugin's name. |

## Storing configuration

Per profile, a plugin stores one `[Serializable]` class under a key:

```csharp
var config = forgeProfile.GetPluginConfig<MyConfig>("MyStudio.MyPlugin");
config.retries = 5;
forgeProfile.SetPluginConfig("MyStudio.MyPlugin", config);
```

- `GetPluginConfig` returns the stored instance, or a new default instance that is **not** stored; reading never changes the asset.
- After changing it, call `SetPluginConfig` again, which marks the Build Forge settings changed; with **Save Profiles On Edit** on, the Build Forge section saves them right after the edit. Code outside the section saves with `AssetDatabase.SaveAssetIfDirty(forgeProfile)`. Passing null removes the configuration, and so does passing one whose values are all those of a new instance, since it would read the same: a setting changed and then changed back leaves the asset as it was.
- Do not share one instance between profiles, and read it again rather than caching it.
- The class must be a `[Serializable]` class that does not derive from `UnityEngine.Object`; `SetPluginConfig` throws otherwise. Unity does not serialize dictionaries; use lists.
- Pick a key that will not collide with other plugins, for example with your studio's or project's name in front.

The configuration is stored as a managed reference, so each field is a readable line in the Build Profile asset and a change is a one-line diff. Unity writes every serialized field, including ones the current settings do not use.

Renaming or moving the class, its namespace or its assembly makes stored data an unknown type: Unity keeps it and logs a warning, and it comes back when the type returns. Until then, builds of the profiles that hold it stop, and so do builds of every profile when it is in Project Settings ([Troubleshooting](troubleshooting.md#build-forge-cannot-load-the-plugin-settings-)). The same goes for the class of a `[SerializeReference]` field in your configuration. Use Unity's `[MovedFrom]` attribute when you rename it.

Project-wide settings work the same way through `ForgeSettings.instance.GetGlobalPluginConfig<T>(key)` and `SetGlobalPluginConfig(key, config)`, which saves `ProjectSettings/ForgeSettings.asset` immediately. Draw them in `OnDrawSettingsGUI`.

## UI

`OnDrawProfileGUI` draws with IMGUI (`EditorGUILayout`) inside the plugin's foldout, both in the Build Forge section of Unity's Build Profile editor and in the Build Forge settings Inspector. Build Forge draws the Enabled checkbox above it and greys the plugin's controls while it is disabled or not applicable. Call `SetPluginConfig` after a change.

Keep the drawing code free of side effects beyond the configuration: do not build, switch profiles or save other assets from it, and defer such actions with `EditorApplication.delayCall`.

## Warnings and build descriptions

`Validate` returns short sentences, shown in the build window for profiles where the plugin is enabled and applicable. They do not stop builds: throw from `OnPreBuild` for a problem that must. The Build Number plugin does both for an unusable environment variable: it warns in the window and fails the build.

`DescribeBuild` returns one line about what the plugin will do in a build of the profile, shown next to it in the build window. `isCI` is the CI detection that build will see, so a plugin whose behavior differs between local and CI builds can say which applies.

Both have variant overloads; the build window passes its selected variant.

## Contributing to the manifest

Implement `IForgeManifestContributor`:

```csharp
public void ContributeToManifest(BuildManifest manifest, ForgeBuildContext context)
{
    var section = new ManifestSection("MyPlugin");
    section.Add("contentVersion", "42");
    manifest.AddSection(section);
}
```

It is called after every plugin's `OnPreBuild`, for the plugins that run in the build, in plugin order, while **Write Build Manifest** is on. Players read the section with `manifest.GetSection("MyPlugin")?.Get("contentVersion")`.

## Adding a step to the Android Gradle project

Implement `IForgeGradleProcessor` to change the Gradle project Unity generates for an Android build, for example to edit a manifest or a Gradle file:

```csharp
public void OnPostGenerateGradleAndroidProject(ForgeBuildContext context, string path)
{
    // path is the unityLibrary module; the launcher module is next to it.
    var manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
    // ...
}
```

Build Forge's own Gradle callback calls it. That is Unity's `IPostGenerateGradleAndroidProject` at callback order 1000000, after the vendors' steps: Meta's SDK writes its manifest entries at 99999. It calls only the plugins that run in the build, in plugin order, and only for builds Build Forge runs. An exception fails the build, with the plugin's name in the message. The [XR Vendor Filter](plugins/xr-vendor-filter.md) has the highest bundled order, 1000, so its leftover check sees the result of every other plugin's step.

Unlike a Unity `IPostGenerateGradleAndroidProject` of your own, the step gets the build's `ForgeBuildContext`: the profile, the variant and the CI detection.

## Applying settings to the editor

Implement `IForgeEditorApplicable` when a plugin's settings are worth having in Play Mode, as OpenXR features are. Plugins that only matter for builds, such as build numbers, signing or symbol upload, must not implement it.

| Member | Contract |
|---|---|
| `string CaptureEditorState(BuildTargetGroup group)` | A JSON snapshot of everything the plugin may write for the group, or null when there is nothing to manage. Build Forge compares snapshot strings to detect drift, so make them deterministic, for example with sorted lists. |
| `void ApplyToEditor(ForgeProfile profile, BuildTargetGroup group)` and the overload with `string variant` | Writes the profile's settings for the group and saves them. |
| `void RestoreEditorState(BuildTargetGroup group, string json)` | Writes a snapshot back and saves it. Throws when it can't, preferably before writing anything. |
| `string MergeEditorState(BuildTargetGroup group, string baselineJson, string expectedJson, string currentJson)` | Optional. The snapshot that undoes the Apply when the settings changed after it, as in a git pull: for each setting, the baseline's value where the current value is still the one the Apply left, and the current value otherwise. Return null, as the default does, when the plugin can't tell them apart. |

On Apply, Build Forge calls `CaptureEditorState` for the baseline, saves it, calls `ApplyToEditor`, and captures again for drift detection. Revert to Baseline, Re-apply, Activate and a build undo the Apply with `RestoreEditorState`: with the baseline while the settings are as the Apply left them, otherwise with what `MergeEditorState` returns, so changes made after the Apply survive. When it returns null, Build Forge restores the baseline and warns that this may undo those changes. Throw from `CaptureEditorState` or `ApplyToEditor` for a problem that must stop Apply, as from `OnPreBuild` for one that must stop a build: Build Forge then restores what the Apply wrote, whole, and nothing stays applied. A capture right after `ApplyToEditor` that throws or returns null stops the Apply too. Throw from `RestoreEditorState` when the snapshot can't be written: Build Forge keeps the setting for another attempt or Stop Tracking, and a build that couldn't take the applied settings out doesn't run. Write nothing before throwing if you can, so a stopped build leaves the editor as it was. After an Apply or a build that did not finish, Build Forge restores the baseline whole, without `MergeEditorState`; see [When an Apply or a build did not finish](concepts.md#when-an-apply-or-a-build-did-not-finish).

Build Forge can call these while another Build Profile is active: Apply writes the settings before it activates the profile, and Activate reverts another profile's settings before switching. The static Player Settings API may then point at another profile, so use APIs that take the build target group or a `NamedBuildTarget`. Do not keep references to the profile between calls.

For the profile being built, a build uses `OnPreBuild` and `OnPostBuild`; around the build, Build Forge uses these methods to take an applied profile's settings out and put them back. A plugin that implements both usually shares one capture, apply and restore core between them, as the OpenXR plugin does.

## Variants in plugins

Read the variant from `ForgeBuildContext.Variant` or the `variant` parameter of the overloads. Do not use `#if BUILD_VARIANT_…` in editor code: the editor's compiled defines reflect what is applied, not the build in progress. Player code uses the defines.

## Reading Build Forge settings from other editor code

Build Forge settings live inside the Build Profile asset, as a component:

```csharp
var active = BuildProfile.GetActiveBuildProfile();               // null on a platform profile
var settings = active != null ? active.GetComponent<ForgeProfile>() : null;
```

For any Build Profile asset, `buildProfile.GetComponent<ForgeProfile>()` does the same. Searching with `AssetDatabase.FindAssets("t:ForgeProfile")` and `LoadAssetAtPath<ForgeProfile>` finds nothing; search for `t:BuildProfile` and ask each for its component instead.

`ForgeSettings.instance.IsPluginEnabled(typeof(SomePlugin))` tells whether a plugin is on in **Project Settings > Build Forge > Installed Plugins**; the plugin's own `IsEnabled` decides per profile.

Unity build callbacks, such as `IPostGenerateGradleAndroidProject` for changes to the generated Android project, run inside Build Forge's build with the built profile active, so they can read the profile's settings this way. `ForgeBuildRunner.IsForgeBuildInProgress` tells them whether the build is Build Forge's.

## Public API

| Area | Types |
|---|---|
| Plugin contract | `IForgePlugin`, `IForgeManifestContributor`, `IForgeEditorApplicable`, `IForgeGradleProcessor`, `ForgePluginAttribute`, `ForgeBuildContext` |
| Configuration | `ForgeProfile`, `ForgeSettings` |
| Utilities | `BuildProfileUtility` (a Build Profile's build target, whether it is a Dedicated Server; both throw when Unity stores no such value in the profile), `BuildEnvironment` (`IsCI`), `GitHelper` (runs git in the project) |
| Builds | `ForgeBuildRunner` (`RunBuild`, `GetCICommand`, `IsForgeBuildInProgress`), `BuildForge.CommandLine` (the `-executeMethod` entry point) |
| Runtime | `BuildManifest`, `ManifestSection`, `ManifestEntry`, `BuildManifestLoader` |

Everything else in the package is internal and may change between releases.

## Testing plugins

Keep a plugin's decisions in methods that take plain values, such as a method that turns an identifier and a suffix into the new identifier, and test those with Unity Test Framework EditMode tests. Build steps themselves are best tested with a real build of a small profile, from the build window or the command line, and a look at the log and the manifest. Turn off the Console's **Clear on Build** to keep the pre-build log lines, which the build otherwise clears.
