# Command line and CI

## The command

The build window shows the commands for the selected profile and variant under **CI Command**: **One workspace per job** is the build command, and **Shared workspace** runs [Activate](#changing-platform) before it, for a workspace that also builds other platforms' profiles. For a Unity project in the repository's `Client` folder, the build command reads:

```sh
unity run "./Client" --non-interactive -- -nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Build -activeBuildProfile "Assets/Settings/Build Profiles/Quest.asset" -forgeVariant "Internal"
```

Run it from the repository root. The project path is relative to it: `./Client` for a subfolder (the `./` makes the Unity CLI read it as a path, not as a registered project name), `.` for a project at the root. Outside a Git repository the window uses the absolute project path. These relative paths work the same on Windows, macOS and Linux.

The standalone Unity CLI reads `ProjectSettings/ProjectVersion.txt`, starts the matching installed editor in batch mode, and passes everything after `--` to it:

| Argument | Purpose |
|---|---|
| `-nographics` | No graphics device on the build machine. |
| `-silent-crashes` | No crash dialogs. |
| `-logFile -` | The editor log goes to the job's output. Give a path instead to keep a separate log file. |
| `-cacheServerWaitForUploadCompletion` | Waits for pending Unity Accelerator uploads before the editor exits. |
| `-executeMethod BuildForge.CommandLine.Build` | Build Forge's entry point. |
| `-activeBuildProfile "<path>"` | Unity's own argument: makes the Unity Build Profile active at startup, before scripts compile, so its defines and settings apply to the compilation. With Unity 6000.3.23, activate first across a platform change; see [Changing platform](#changing-platform). |
| `-forgeVariant "<name>"` | The variant. Required once the project has variants; leave it out in a project without them. |

The CLI itself supplies `-batchmode`, `-projectPath` and `-quit`; do not pass them after `--`, the CLI rejects reserved flags. Build Forge's entry point finishes the build and all cleanup, then exits the editor itself with the result.

## Requirements on the build machine

- The standalone `unity` CLI on the PATH of the account the job runs as. The tested version is **1.0.0-beta.13**, with Unity **6000.3.23f1**, on Windows. Check `unity --version` under the agent's account, pin that version in the agent image, and check again before updating it. Unity Hub may bundle a different CLI version internally; the one on the PATH is what runs.
- An activated Unity editor of the project's version, with the platform modules the profiles build for (for Android also its SDK, NDK and JDK). Installing and licensing editors is part of setting up the agent; `--non-interactive` only prevents prompts.
- For a profile of a derived platform, such as Meta Quest: `-enableAllDerivedPlatforms` after `--`, or the platform enabled for the account the job runs as. Unity enables derived platforms per user, with **Enable Platform** in its Build Profiles window, which an agent's account has usually never opened. Without either, Unity would build the profile with Android's settings, so Build Forge stops the build before it starts.
- `UNITY_EDITOR_VERSION` unset, unless you mean to override the project's editor version.
- Git, with read access to the Build Forge repository if it is private; see [Installation](installation.md#private-repositories).
- A full clone if any profile uses the git commit count as its [build number](plugins/build-number.md).

## Start the licensing client before each build (Windows)

On Windows, `unity run` can keep waiting after a finished build: the log ends with Unity's whole shutdown, and the step does not return until the job times out. The CLI returns only when every process holding the editor's output has closed it. When no licensing client runs for the account, the editor starts `Unity.Licensing.Client.exe`, which inherits that output. The client exits as soon as its last connection closes, so a lone build returns normally, but while another process of the same account is connected to it, such as Unity Hub or a second editor, the step waits. Ending `Unity.Licensing.Client.exe` releases it with the build's exit code.

Before each `unity run`, start the client yourself, the way the editor would, so the editor connects to it instead of starting its own. Do it in the same step, right before `unity run`, with the project's editor version: Jenkins ends the processes a step started when that step ends, so a client started in a step of its own would already be gone. The Example Integration sample's pipeline, workflow and scripts do this.

```powershell
$version = (Get-Content Client/ProjectSettings/ProjectVersion.txt -TotalCount 1) -replace 'm_EditorVersion: ', ''
$client = Join-Path (unity editors path $version) 'Editor\Data\Resources\Licensing\Client\Unity.Licensing.Client.exe'
# Through ShellExecute, so the client inherits none of the step's handles; with
# -NoNewWindow it would hold the step's output open in the same way.
Start-Process -FilePath $client -ArgumentList '--namedPipe', "Unity-LicenseClient-$env:USERNAME" -WindowStyle Hidden
Start-Sleep -Seconds 5
```

The client runs once per account, so starting it while one runs does nothing. It waits for its first connection and exits after its last, so start it before every build, not once per agent. This was checked with Unity CLI 1.0.0-beta.12 and beta.13.

A step has also stalled on an agent that runs one build at a time, with no second process of the account; the cause is not known yet. If it happens, list the running processes and their parents before ending `Unity.Licensing.Client.exe`, so the cause can be found.

## Build Forge's arguments

| Argument | Value | Effect |
|---|---|---|
| `-forgeVariant` | A variant name | Builds that variant. Required once the project has variants: without it the build fails and lists them rather than picking one. Names must match a configured variant exactly, including case. |
| `-forgeVersion` | For example `1.2.3` | Sets `PlayerSettings.bundleVersion` for this build. |
| `-forgeVersionCode` | An integer | Sets the Android version code for this build. |
| `-forgeCI` | `true` or `false` (also `1`/`0`, `yes`/`no`) | Overrides [CI detection](#ci-detection). Without a value it means true. |
| `-forgeBuildProfile` | A Unity Build Profile's asset path | Only for `BuildForge.CommandLine.Activate`; see [Changing platform](#changing-platform). |

Build Forge's arguments match case-insensitively and are checked strictly, because an argument the entry point ignored would still produce a successful build of the wrong thing. The build fails with exit code 1 before it starts when:

- an argument starts with `-forge` but is not one of the build's, for example a typo or `-forgeBuildProfile`;
- `-forgeVariant`, `-forgeVersion` or `-forgeVersionCode` has no value: it is last on the line, followed by another `-` argument, or empty, as an unset CI variable gives;
- the variant is unknown, or not allowed for the profile;
- `-forgeVersionCode` is not an integer;
- `-forgeCI`, or the `FORGE_CI` environment variable, has a value other than the ones above;
- the former `-versionCode` is used; it was renamed `-forgeVersionCode`.

Unity's own `-version` prints the editor version and exits; it does not set the build's version.

## How the profile is chosen

`-activeBuildProfile` selects a Unity Build Profile, and Build Forge builds the Build Forge settings stored in it. The entry point fails with exit code 1 when:

- no Unity Build Profile is active, for example because `-activeBuildProfile` is missing and Unity was on a platform profile;
- the active Build Profile has no Build Forge settings, for example because they were never committed;
- the active Build Profile's platform, such as Meta Quest, is disabled in the editor, or Build Forge cannot tell whether it is; see [Requirements on the build machine](#requirements-on-the-build-machine).

## CI detection

Some plugin work is only worth doing on a build machine, such as uploading symbols. A build's `IsCI` is decided in this order:

1. `-forgeCI` on the command line, with the value `true` or `false`; bare `-forgeCI` means true.
2. The `FORGE_CI` environment variable, `true` or `false`; empty counts as unset.
3. Otherwise: batch mode is CI, an interactive editor is not.

Every command-line build therefore counts as CI unless it says otherwise. A local script that builds in batch mode and should count as a local build passes `-forgeCI false`. An interactive editor on a build machine that should count as CI gets `FORGE_CI=1`.

What depends on it among the bundled plugins: [Build Number](plugins/build-number.md) with the "Env Var on CI, Git Count Locally" source, and [Cloud Diagnostics](plugins/cloud-diagnostics.md) with "CI Only". CI builds are never shown in the file browser afterwards. Your plugins read it as `ForgeBuildContext.IsCI`.

## Exit codes and logs

- **0**: the build succeeded and every restore and cleanup step succeeded.
- **1** from the editor: Build Forge refused the arguments, could not resolve the profile or variant, the build failed or threw, or a restore or cleanup step failed. A failed restore fails the run even when the player was built, so CI does not go on with a modified project.
- The Unity CLI reports a nonzero editor exit as **6** (verified with CLI beta.9, beta.11 and beta.13); its message names the editor's own exit code.
- Unity can also fail before Build Forge runs, for example on a compile error, with its own nonzero code.

Treat every nonzero result as a failure, and archive or publish only after 0.

Build Forge's log lines start with `[Build Forge]`, plugins' with `[Build Forge/<plugin>]`. Each build ends with a summary line, such as `[Build Forge] Quest (Internal) — Succeeded | 6m 3s | 812.4 MB | 41 warnings`, and `[Build Forge] Build process complete.`

## Version overrides

`-forgeVersion` and `-forgeVersionCode` set the version and the Android version code for one build. They are written through Unity's Player Settings API, so they reach the active Build Profile's own Player Settings when it has them, and both are restored afterwards, also when the build fails.

If [Build Number](plugins/build-number.md) is enabled for the profile, it sets the version code after the command line has, so the plugin's number is the one built.

## Environment variables

| Variable | Read by |
|---|---|
| `FORGE_CI` | CI detection |
| `BUILD_NUMBER`, or the name you configure | [Build Number](plugins/build-number.md) with a variable source |
| `ANDROID_KEYSTORE_PATH`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`, or the names you configure | [Android Keystore Env](plugins/android-keystore-env.md) |

Pass secrets as environment variables from the CI system's secret store, never as command-line arguments, which end up in logs.

## Build output and artifacts

The output path is relative to the Unity project, so with the default template a build of `./Client` lands in `Client/Builds/<Target>/<Profile>/<Variant>/`.

[Remove Previous Build Output](settings.md#remove-previous-build-output) removes what the previous build of the same profile and variant produced, from a record kept in `UserSettings/`. A fresh CI workspace has no record, so only the artifact file itself is replaced, and files that were already in the destination are never removed later. Either:

- keep the destination inside a workspace the pipeline cleans,
- clean the output folder in the job before the build, or
- keep `UserSettings/ForgeBuildOutputs.asset` between runs; this helps only when every run checks the project out to the same path.

Run uploads only after a successful build: Unity can fail before Build Forge runs, and a failed build can leave partial new output.

The manifest is inside the player, for an APK at `assets/BuildForge/BuildManifest.json`; archiving the APK archives it.

## Timeouts, cancellation and one build per checkout

- For a bounded job, add `--timeout <seconds>` before `--`, with enough time for imports, the build and cache uploads.
- A forced timeout, a cancelled job or a crash can stop the editor before Build Forge restores its changes. Discard or recover the workspace before reusing it.
- With Unity CLI 1.0.0-beta.9 and beta.10 on Windows, cancelling `unity run` could leave its editor running, which then blocks the next build of the same project ("already open"). Identify that editor by its process and project path and stop it before retrying; do not kill every Unity process by name or delete the project's lock file while its editor is alive. Later CLI versions have not been checked for this.
- Run only one Unity process per checkout. Two jobs in the same workspace fight over the open project, the Library and the settings Build Forge changes during a build. Use separate checkouts for parallel builds.

On a machine whose workspace is reused, Unity's `Library/` remembers the active Build Profile by its path. When a pulled commit has renamed or moved that profile, a batch-mode start without `-activeBuildProfile` fails before any `-executeMethod` runs. The generated command always passes `-activeBuildProfile`, which avoids this; see [Troubleshooting](troubleshooting.md#failed-to-read-the-specified-build-profile).

## Building several profiles

Build Forge builds one profile per Unity process. Build several by running the command once per profile and variant, one after the other in the same checkout, or in parallel in separate checkouts. Each run switches the editor to the profile's platform, which costs an import when the platform differs from the previous run's. With Unity 6000.3.23, run [Activate](#changing-platform) first when it does.

## Changing platform

A workspace that builds profiles of more than one platform changes platform between builds: several jobs that share a workspace, one job with a profile parameter, such as the sample Jenkins pipeline and GitHub workflow, or a job that builds several profiles in a row. A workspace that only builds profiles of one platform, such as a job of its own per profile, never does, and the first build in a fresh clone works without Activate.

`-activeBuildProfile` makes Unity activate the profile while the editor starts, before scripts compile. With Unity 6000.3.23, a start that changes platform this way compiles first for the previous platform, with the new profile's scripting defines. If code under those defines needs an assembly that only exists for the new platform, such as a platform SDK, compilation fails and the editor exits before Build Forge runs, still on the previous profile. The log ends with `Scripts have compiler errors.`, and the same command works in a workspace that is already on the profile's platform. Unity 6000.3.0 and 6000.6.4 switch before compiling and don't have the problem; other versions weren't checked.

In a shared workspace, activate the profile first, in a run of its own. The build window's **Shared workspace** command is this line followed by the build command:

```sh
unity run "./Client" --non-interactive -- -nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Activate -forgeBuildProfile "Assets/Settings/Build Profiles/Quest.asset"
```

The editor starts on the profile it was last on, so its scripts compile as before. Activate then switches platform and defines together, like the build window's Activate, and exits. The build command that follows names the profile that is now active, so the editor has nothing to switch at startup. Activating the profile that is already active changes nothing, so a job can run Activate before every build. Run the build only after Activate succeeded: as a separate CI step, or chained, for example with `&&` in bash or cmd.

`-forgeBuildProfile` is Activate's only argument and takes the Unity Build Profile's asset path. Activate exits with 0, or with 1 and the reason in the log when:

- `-forgeBuildProfile` is missing or has no value;
- there is no Unity Build Profile at the path, or it has no Build Forge settings;
- another argument starting with `-forge` is passed; the build's arguments belong on the build command;
- Activate itself fails, for example because an earlier restore failed; see [Troubleshooting](troubleshooting.md#build-forge-could-not-restore-some-settings).

Activate starts without `-activeBuildProfile`, so in a reused workspace it fails before it runs when a pulled commit has renamed or moved the profile the workspace was last on; see [Troubleshooting](troubleshooting.md#failed-to-read-the-specified-build-profile).

## Calling the editor directly

Existing automation can launch the editor itself instead of the Unity CLI:

```sh
Unity -batchmode -nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Build -projectPath "Client" -activeBuildProfile "Assets/Settings/Build Profiles/Quest.asset"
```

Replace `Unity` with the path of the editor executable that matches the project's version. `-quit` is not needed, since Build Forge exits after cleanup. Once Build Forge runs, the editor exits with Build Forge's code, 0 or 1.

Neither form passes `-accept-apiupdate`, which would let Unity's API updater rewrite source code on the build machine; commit API migrations and build from them.

## Why `unity run` and not `unity build`

Build Forge needs both `-activeBuildProfile` at editor startup and its own `-executeMethod`. The CLI's `unity build` command treats native Build Profile builds and custom-method builds as separate strategies: combining `--profile` with `--execute-method` is rejected, and so is forwarding `-activeBuildProfile` through `--args` (checked with CLI beta.9, beta.11 and beta.13). `unity run` passes both, so the generated command uses it.

## Examples

The **Example Integration** sample's `CI` folder has complete files for these; the excerpts here show the essentials. On Windows they leave out the step that starts Unity's licensing client before each `unity run` ([why](#start-the-licensing-client-before-each-build-windows)), which the sample's files have.

### Jenkins pipeline (Windows agent)

```groovy
stage('Activate') {
    steps {
        bat '''
            set "UNITY_EDITOR_VERSION="
            unity run "./Client" --non-interactive -- -nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Activate -forgeBuildProfile "Assets/Settings/Build Profiles/%PROFILE_NAME%.asset"
            if errorlevel 1 exit /b %ERRORLEVEL%
        '''
    }
}
stage('Build') {
    steps {
        withCredentials([
            file(credentialsId: 'android-keystore-file', variable: 'ANDROID_KEYSTORE_PATH'),
            string(credentialsId: 'android-keystore-password', variable: 'ANDROID_KEYSTORE_PASSWORD'),
            string(credentialsId: 'android-key-password', variable: 'ANDROID_KEY_PASSWORD')
        ]) {
            bat '''
                set "UNITY_EDITOR_VERSION="
                unity run "./Client" --non-interactive -- -nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Build -activeBuildProfile "Assets/Settings/Build Profiles/%PROFILE_NAME%.asset" -forgeVariant "%VARIANT_NAME%"
                if errorlevel 1 exit /b %ERRORLEVEL%
            '''
        }
    }
}
```

The job builds any profile in one workspace, so it activates first; see [Changing platform](#changing-platform). Jenkins sets `BUILD_NUMBER` for every build. Archive in `post { success { … } }`, so a failed build archives nothing, and keep `disableConcurrentBuilds()` so two builds never share the workspace.

### Jenkins freestyle job (Windows agent)

An **Execute Windows batch command** step, from the workspace root:

```bat
@echo off
set "UNITY_EDITOR_VERSION="
unity run "./Client" --non-interactive -- -nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Build -activeBuildProfile "Assets/Settings/Build Profiles/Quest.asset"
if errorlevel 1 exit /b %ERRORLEVEL%
```

### GitHub Actions (self-hosted Windows runner)

GitHub's hosted runners have no Unity editor, so this needs a self-hosted runner with the CLI and an activated editor.

```yaml
- uses: actions/checkout@v4
  with:
    fetch-depth: 0   # the git commit count build number needs the full history
- name: Activate the profile
  shell: powershell
  env:
    PROFILE_NAME: ${{ inputs.profile }}
  run: |
    Remove-Item Env:UNITY_EDITOR_VERSION -ErrorAction SilentlyContinue
    unity run ./Client --non-interactive -- -nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Activate -forgeBuildProfile "Assets/Settings/Build Profiles/$env:PROFILE_NAME.asset"
    exit $LASTEXITCODE
- name: Build
  shell: powershell
  env:
    BUILD_NUMBER: ${{ github.run_number }}
    PROFILE_NAME: ${{ inputs.profile }}
    VARIANT_NAME: ${{ inputs.variant }}
  run: |
    Remove-Item Env:UNITY_EDITOR_VERSION -ErrorAction SilentlyContinue
    unity run ./Client --non-interactive -- -nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Build -activeBuildProfile "Assets/Settings/Build Profiles/$env:PROFILE_NAME.asset" -forgeVariant $env:VARIANT_NAME
    exit $LASTEXITCODE
```

The workflow builds any profile in the runner's one workspace, so it activates first; see [Changing platform](#changing-platform). Inputs reach the script through environment variables rather than `${{ }}` inside `run`, so a crafted input cannot inject commands.

### Scripts for local builds

The sample's `Build-Player.ps1` (Windows PowerShell 5.1 and later) and `build-player.sh` (bash) wrap the same command with options for the variant, `-forgeCI false`, Activate first in a shared workspace (`-SharedWorkspace`, `--shared-workspace`), a time limit, and launching an editor directly on machines without the CLI:

```powershell
./Build-Player.ps1 -ProjectPath ./Client -BuildProfile 'Assets/Settings/Build Profiles/Quest.asset' -Variant Internal -Local
```

```sh
bash build-player.sh --project ./Client --profile "Assets/Settings/Build Profiles/Quest.asset" --variant Internal --local
```

When a PowerShell script launches the editor directly, wait for it with `Start-Process -PassThru` and `WaitForExit()`. `Start-Process -Wait` also waits for processes the build leaves running, such as a Gradle daemon, and can hang long after Unity has exited.

## From your own editor scripts

```csharp
using BuildForge.Editor.Configuration;
using BuildForge.Editor.Core;
using UnityEditor.Build.Profile;

var buildProfile = BuildProfile.GetActiveBuildProfile();
var forgeProfile = buildProfile != null ? buildProfile.GetComponent<ForgeProfile>() : null;
var report = ForgeBuildRunner.RunBuild(forgeProfile, "Internal"); // RunBuild(forgeProfile) in a project without variants
var command = ForgeBuildRunner.GetCICommand(forgeProfile, "Internal");
```

`RunBuild` returns Unity's `BuildReport`. It throws `InvalidOperationException` when the profile's Unity Build Profile is not the active one, when its platform is disabled in the editor or Build Forge cannot tell whether it is, when a plugin that is on failed to load or plugin settings can't be loaded, when the variant is invalid, or, on CI, when the variant rule's [Version](variants.md#version) can't be filled in; activate the profile first, in the build window or with `BuildProfile.SetActiveBuildProfile`. When the player was built but a restore or cleanup step after it failed, `RunBuild` throws `ForgeBuildRestoreException` once every other step has run, so a script doesn't take a possibly modified project for a clean build: its `Report` is the player's report, which says Succeeded, and `Failures` lists the failed steps. A player build that fails is reported by the returned report. `ForgeBuildRunner.IsForgeBuildInProgress` tells other build callbacks whether the current build is Build Forge's.
