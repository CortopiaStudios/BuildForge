# Example Integration

This sample shows two things a project adds around Build Forge:

- a small **project plugin**, Application ID Suffix, that appends a suffix to the Android application identifier while chosen variants are built, so an internal build installs next to the store build instead of replacing it;
- **CI recipes** that build a Unity Build Profile with Build Forge from Jenkins, GitHub Actions, PowerShell and bash.

| File | Purpose |
|---|---|
| `Editor/ApplicationIdSuffixPlugin.cs` | The plugin. |
| `Editor/ApplicationIdSuffixConfig.cs` | Its per-profile settings. |
| `Editor/BuildForge.Samples.ExampleIntegration.Editor.asmdef` | An editor-only assembly that references Build Forge. |
| `CI/Jenkinsfile` | A declarative Jenkins pipeline for a Windows agent. |
| `CI/build-player.yml` | A GitHub Actions workflow for a self-hosted Windows runner. |
| `CI/Build-Player.ps1` | A PowerShell build script, Windows PowerShell 5.1 or later. |
| `CI/build-player.sh` | The same as a bash script. |

The plugin needs Build Forge on Unity 6000.3 or later and the Android platform module. The CI recipes need the standalone Unity CLI on the build machine. See the Build Forge manual's [Command line and CI](../../Documentation~/ci.md) page for the command they run.

<!-- unverified: the CI recipes follow Build Forge's documented command line and have not been run as written on Jenkins or GitHub Actions -->

## 1. Import the sample

In the Package Manager, select Build Forge, open its **Samples** and import **Example Integration**. Unity copies it into the project under `Assets/Samples/Build Forge/<version>/Example Integration/`. <!-- unverified: Unity's import folder for package samples -->

The plugin compiles into its own editor assembly, and Build Forge finds it without registration: **Application ID Suffix** appears in **Project Settings > Build Forge > Installed Plugins** and in the Build Forge section of every Android Build Profile, off.

## 2. Prepare a Build Profile and a variant

The sample does not create Build Profiles: Unity has no public API for creating Build Profile assets, so do this by hand.

1. In Unity's Build Profiles window, add an **Android** Build Profile and name it, for example, **Quest**.
2. In its **Build Forge** section, click **Add Build Forge Settings**.
3. In **Project Settings > Build Forge > Build Variants**, add **Internal**. Optionally add a Variant Rule for `Internal` with Development Build **Enabled**.
4. Make sure Player Settings has an Android package name, for example `com.example.mygame`.

## 3. Configure the plugin

In the Quest profile's Build Forge section, expand **Application ID Suffix**:

1. Tick **Enabled**.
2. Keep the **Suffix** `.internal`, or enter another one. A suffix that starts with a dot adds a segment (`com.example.mygame.internal`); one without a dot extends the last segment (`com.example.mygameinternal`).
3. Under **Variants**, tick **Internal**.

While the profile is active, the foldout's last line shows the identifier variant builds get.

## 4. Build the variant

Open **Window > Build Forge > Build**, select **Quest**, click **Activate Quest**, set **Variant** to **Internal**, and click **Build Quest (Internal)**.

The details pane lists the plugin under **Plugins for this build** as `append '.internal' to the application identifier`, and the build log shows:

```
[Application ID Suffix] Application identifier 'com.example.mygame.internal' for this build (was 'com.example.mygame').
...
[Application ID Suffix] Restored application identifier 'com.example.mygame'.
```

The APK is at `Builds/Android/Quest/Internal/My_Game_Internal.apk`. Build Forge marks the variant's product name, so the app is called `My Game (Internal)` on the device, and the plugin gave it its own package name. Player Settings still say `com.example.mygame` after the build, and a default build is not changed.

Install it next to the store build:

```sh
adb install -r Builds/Android/Quest/Internal/My_Game_Internal.apk
```

The two apps have separate data. Services that check the package name, such as platform entitlements or push notifications, may need the test identifier registered before they work in the internal build. With a split application binary, the OBB is named after the package (`main.<version code>.com.example.mygame.internal.obb`); on the headset this was tested with, adb could create the app's OBB folder only after the app had been started once.

## 5. How the plugin works

The comments in `ApplicationIdSuffixPlugin.cs` explain each part. The points that matter for any plugin:

- **Discovery.** `[ForgePlugin]` on a class that implements `IForgePlugin` is all Build Forge needs.
- **Order 650.** The plugin runs after Android Keystore Env (600) and before the Addressables plugins (700, 800). It depends on none of them; the identifier only has to be set before the player build.
- **Applicability.** `IsApplicable` limits the plugin to Android Build Profiles, and `NotApplicableReason` says so in other profiles' sections. Unity 6000.3 has no public property for a Build Profile's platform, so the plugin reads the serialized `m_BuildTarget` field, as Build Forge itself does.
- **A per-profile switch.** `IsEnabled` returns the stored `enabled` field, so Build Forge draws the Enabled checkbox and skips the plugin where it is off. It starts off, so importing the sample changes no build.
- **Settings.** `ApplicationIdSuffixConfig` is stored in each Build Profile under the key `ExampleIntegration.ApplicationIdSuffix`, one line per field:

  ```yaml
    pluginConfigs:
    - key: ExampleIntegration.ApplicationIdSuffix
      config:
        rid: 7130472218546188289
    references:
      version: 2
      RefIds:
      - rid: 7130472218546188289
        type: {class: ApplicationIdSuffixConfig, ns: BuildForge.Samples.ExampleIntegration, asm: BuildForge.Samples.ExampleIntegration.Editor}
        data:
          enabled: 1
          suffix: .internal
          variants:
          - Internal
  ```

- **Build steps.** `OnPreBuild` reads the identifier through the static `PlayerSettings` API, which during a build answers for the active Build Profile, records the original in the build context and sets the suffixed one. It records before changing anything, because Build Forge calls `OnPostBuild` even when `OnPreBuild` throws. `OnPostBuild` sets the original back; Build Forge then saves the restored Player Settings.
- **Failing early.** An invalid suffix, or an identifier that the suffix would make invalid, throws from `OnPreBuild`, which stops the build before the player is built. `Validate` shows the same problems in the build window beforehand, together with variants the plugin lists that are no longer configured.
- **The manifest.** `IForgeManifestContributor` adds an `ApplicationIdSuffix` section with the identifier the build used, so the player can show it.
- **No editor Apply.** The plugin only matters for builds, so it does not implement `IForgeEditorApplicable`: Apply never changes the identifier the editor uses.

If the editor crashes during a build, the suffixed identifier can stay in Player Settings; restore `ProjectSettings/ProjectSettings.asset`, or the Build Profile if it has Player Settings of its own, from version control.

## 6. Adapting it

- **Move it into your own code.** Copy the two `.cs` files into one of your editor assemblies and change the namespace. The settings stored in Build Profiles name the class, its namespace and its assembly; after a rename, Unity keeps them as an unknown type until the old type returns. Use Unity's `[MovedFrom]` attribute on the class when you rename it, or configure the plugin again.
- **iOS.** The same change applies to the bundle identifier through `NamedBuildTarget.iOS`, but a build with another bundle identifier needs a provisioning profile that covers it.
- **Other per-variant changes** follow the same pattern: record the original in `OnPreBuild`, change it, restore it in `OnPostBuild`. See the manual's [Writing plugins](../../Documentation~/custom-plugins.md).

## 7. The CI recipes

All four run the commands Build Forge's build window shows under **CI Command**, from the repository root. The pipeline and the workflow build any profile in one workspace, so they run the **Shared workspace** commands: Activate, then the build. A job per profile, in a workspace of its own, can drop the Activate step; see the manual's Command line and CI page, Changing platform. Adjust the project folder (`./Client` in the examples) and the Build Profile names to your project; the recipes assume Unity's default folder for Build Profiles, `Assets/Settings/Build Profiles/`.

| Variable | Read by |
|---|---|
| `BUILD_NUMBER` | Build Number, with an Environment Variable source. Jenkins sets it; the workflow sets it from the run number. |
| `ANDROID_KEYSTORE_PATH`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_PASSWORD` | Android Keystore Env, enabled on the Android profiles. The recipes take them from the CI secret store; keep the key alias in Player Settings or add `ANDROID_KEY_ALIAS`. |

### Jenkinsfile

A declarative pipeline for a Windows agent with the label `unity && windows`. It asks for the profile and the variant, removes the previous output, activates the profile, builds, and archives the output only after a successful build.

Create three credentials in Jenkins: a secret file `android-keystore-file` with the keystore, and secret texts `android-keystore-password` and `android-key-password`. Remove the `withCredentials` block for jobs that build no Android profile.

### build-player.yml

A GitHub Actions workflow, started by hand with a profile and a variant, for a self-hosted Windows runner; GitHub's hosted runners have no Unity editor. Copy it to `.github/workflows/`. It checks out the full history for the git commit count, keeps `Library/` between runs, writes the keystore from a base64 secret to a temporary file, activates the profile, builds, and uploads the output only after a successful build.

Create the repository secrets `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD` and `ANDROID_KEY_PASSWORD`. The keystore step runs for every profile except `Windows`; adjust its condition to your Android profiles.

### Build-Player.ps1 and build-player.sh

For developers and for agents where a script is easier to maintain than a pipeline step.

```powershell
./Build-Player.ps1 -ProjectPath ./Client -BuildProfile 'Assets/Settings/Build Profiles/Quest.asset' -Variant Internal -Local
```

```sh
bash build-player.sh --project ./Client --profile "Assets/Settings/Build Profiles/Quest.asset" --variant Internal --local
```

| PowerShell | bash | Meaning |
|---|---|---|
| `-BuildProfile` | `--profile` | The Unity Build Profile asset, relative to the project. Required. |
| `-Variant` | `--variant` | A variant, or `Default` (the default). |
| `-ProjectPath` | `--project` | The Unity project folder, `.` by default. |
| `-Local` | `--local` | Passes `-forgeCI false`, so a batch build counts as a local build: Build Number with "Env Var on CI, Git Count Locally" takes the commit count, and Cloud Diagnostics with "CI only" turns crash reporting off. |
| `-SharedWorkspace` | `--shared-workspace` | Activates the profile in a run of its own before the build, for a workspace that also builds profiles of other platforms. |
| `-TimeoutSeconds` | `--timeout` | Unity CLI only: stops each editor run after that many seconds. |
| `-Editor` | `--editor` | Starts this Unity editor executable directly instead of the Unity CLI. |
| `-LogFile` | `--log` | Where the editor log goes; the console by default, or a file under the project's `Logs` folder for a directly started editor in PowerShell. With the shared-workspace option, the Activate run logs to the same name with `-activate` before the extension. |

Both exit with the build's result: 0 on success, otherwise the Unity CLI's 6 or the editor's 1.

## Removing the sample

Delete the sample's folder. Build Profiles that stored the plugin's settings keep them; Unity warns about an unknown managed type for that entry until it is removed from the Build Profile asset, or the plugin returns.
