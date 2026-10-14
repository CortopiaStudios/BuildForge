# forge: design note

Status: planned, not started. Work begins after Build Forge 1.0.

`forge` is a command-line tool for build machines and developers. It knows a Unity project's Build Forge setup from the project's files: which Build Profiles exist, which variants each allows and where each build lands. It launches builds and uploads their output to stores, from nothing more than a profile name and, where needed, a variant.

## Why

The main reason is to break out the glue around builds into reusable code that the planned Build Forge server can use later: finding a project's profiles and outputs, starting builds and uploading them. Building it as a command-line tool first gives that code real use and tests before the server exists.

Today that glue lives in every CI job: each one hard-codes output paths, store settings and the quirks of the store tools. With `forge`, a job names a profile and a variant, and the tool works out the rest.

`forge` is not tied to a CI system. Jenkins, GitHub Actions and the future server run the same commands, and so does a developer at their own machine.

## Decisions

| Topic | Decision |
|---|---|
| Location | A top-level `CLI/` folder, next to `Unity/` and the planned `Server/`. It is not part of the package: projects install only `Unity/` (`?path=/Unity`). |
| Language and runtime | C# on .NET 10, the current long-term support release, so the tool shares code with the package and the server can use the same language. |
| Executable | `forge`, one executable with subcommands in the style of the Unity CLI. |
| Structure | A class library holds the logic; `forge` is a thin front end over it, so the server can use the library directly instead of running `forge` as a separate process. |
| Code shared with the package | A Unity-free folder in the package that the library also compiles. |
| Finding a profile | A Build Profile asset path, or a name looked up among the project's Build Profiles that have Build Forge settings. |
| Finding a build's output | Worked out from the project's files, without starting Unity. |
| Distribution | Executables for Windows, macOS and Linux, built by GitHub Actions when a tag is pushed and attached to that release. Unsigned for now. |
| Samples | The Example Integration sample's Jenkinsfile and GitHub workflow call `forge`. Its `Build-Player.ps1` and `build-player.sh` are removed. |
| Build window | The CI Command rows show the `forge build` form and the `unity run` form. |
| Timing | After 1.0. |

## Commands

A first version, as a sketch. Options follow the build window's CI Command.

| Command | Does |
|---|---|
| `forge profiles [--project <path>]` | Lists the project's Build Profiles with Build Forge settings, their platforms and the variants each allows. |
| `forge output --profile <name or path> [--variant <name>]` | Prints where that build lands: the output folder and the main file. |
| `forge build --profile <name or path> [--variant <name>] [--shared-workspace] [--timeout <seconds>]` | Runs Unity through the Unity CLI with Build Forge's arguments and exits with the build's result. |
| `forge upload meta --profile <name or path> [--variant <name>] ...` | Uploads the build to the Meta Quest store. |
| `forge upload steam --profile <name or path> [--variant <name>] ...` | Uploads the build to Steam. |

`forge profiles` and `forge output` are read-only. They answer the server's first questions, and they test the file reading against real projects before anything depends on it.

### forge build

`forge build` runs the command the build window generates today:

```sh
unity run "<project>" --non-interactive -- -nographics -silent-crashes -logFile - -cacheServerWaitForUploadCompletion -executeMethod BuildForge.CommandLine.Build -activeBuildProfile "<profile asset path>" -forgeVariant "<variant>"
```

- It resolves a profile name to its asset path first, since the editor's `-activeBuildProfile` takes paths only.
- With `--shared-workspace` it runs `BuildForge.CommandLine.Activate` first and builds only if that succeeded, as described in the CI documentation.
- `--timeout` passes on to `unity run --timeout`.
- It exits with the build's result. The Unity CLI's own codes (exit 6 when Unity exits with 1) are mapped to documented `forge` codes.
- It is where a fix for the CI command stalling after a finished build belongs (`Unity/ARCHITECTURE.md`, "The CI command can stall after a finished build"); until then the manual documents starting the licensing client before each `unity run`. Which fix depends on what holds the step open on a CI agent that runs one build at a time, which is still being investigated.

### Uploads

- Meta Quest store: through Meta's platform command-line utility (`ovr-platform-util`), with the APK and, for a split build, its OBB.
- Steam: through `steamcmd` with app and depot build scripts, with the build's folder as the content root.

Credentials come from environment variables and are never logged, as with the Android Keystore Env plugin. Where the rest of the upload settings live is open; see below.

## Code shared with the package

The shared code lives in a folder in the package with its own assembly definition: no engine references, C# 9 like the rest of the package. Unity compiles it as part of Build Forge; the `forge` library compiles the same files. The editor and the tool then run the same logic.

- **Ready to share:** `Editor/Core/UnityYamlParser.cs` already uses only the .NET base library.
- **To split out first:** the output path template, variant resolution and product name marking (in `ForgeProfile.GetResolvedOutputPath` and `BuildVariants`), product name sanitizing, the profile define sanitizing (`BuildProfileDefines`), and reading a Build Profile's embedded Player Settings (in `UI/PlayerSettingsDiffView.cs`). Today they sit in classes that also use Unity's API, so this is a refactor of package code.
- Shared types stay internal, with `InternalsVisibleTo` for Build Forge's own editor assembly, so the package's public API doesn't grow. The library offers its own public API to the front end and, later, the server.

## Finding a profile

`--profile` takes a Build Profile asset path, or a name:

- A name is looked up among the project's Build Profiles that have Build Forge settings, in any folder.
- If several match, `forge` stops and lists their paths, and the user passes a path instead.

Names aren't guaranteed unique. Unity allows two Build Profiles with the same file name in different folders, and Build Forge only warns when two names give the same `BUILD_PROFILE_` define. The editor's `-activeBuildProfile` takes paths only: a bare name aborts batch mode with "Failed to read the specified build profile". The Unity CLI's `unity build --profile <name>` is only a shorthand for `Assets/Settings/Build Profiles/<name>.asset`, and Build Forge can't use `unity build` anyway.

## Finding a build's output

`forge` works the output path out from the files, without starting Unity. That is sound because Build Forge fixes the path when a build starts, before any plugin runs, and plugins can't change it (`ForgeBuildContext.OutputPath` is read-only).

Inputs, all in the project's files:

| Input | From |
|---|---|
| Output path template | The Build Forge block in the Build Profile asset; empty means the package's default, `Builds/{Target}/{ProfileName}/{Variant}/{ProjectName}` |
| `{ProfileName}` and `{Target}` | The Build Profile's name and its `m_BuildTarget` |
| `{ProjectName}` | The product name from the profile's own Player Settings when it has them, otherwise from `ProjectSettings/ProjectSettings.asset`; then variant marking and sanitizing |
| `{Variant}` and marking | `ProjectSettings/ForgeSettings.asset`: the variants, their rules, Mark Variant Builds, Sanitize Product Name |
| File extension | The platform; for Android the profile's App Bundle and Export Project settings |

Limits:

- A project script that changes the product name before the build would make the answer wrong.
- Files a build adds beside the main one, such as an OBB, are found on disk next to the resolved path rather than predicted.
- Tests in the package compare the editor's live result with the file-based one on the same project, and the library has tests against sample project files.

## Versions

The default template, the file formats and Build Forge's arguments belong to a package version, and projects pin different Build Forge tags.

- `forge` reads the project's Build Forge version from `Packages/packages-lock.json`.
- Each release of `forge` comes from the same tag as the package. The policy for other versions is open: support a range, or refuse versions it doesn't know.

## Distribution

- A GitHub Actions workflow builds self-contained executables on tag push and attaches them to the release. Build machines download the one matching the Build Forge version their project pins.
- Planned targets: Windows x64, macOS on Apple Silicon and Intel, Linux x64.
- Unsigned for now. Windows SmartScreen may warn when someone downloads `forge.exe` in a browser; a script that downloads it on a build machine usually isn't flagged.
- Apple Silicon runs only signed executables, though an ad-hoc signature is enough. Check that a macOS executable built on a Linux runner runs there, or build the macOS executables on a macOS runner.
- Other tools also install a `forge` command (Foundry, Laravel Forge's CLI). On a machine that has one, whichever comes first on the PATH wins.

## Changes in the package

- The shared, Unity-free folder and the refactor described above.
- The Example Integration sample: the Jenkinsfile and GitHub workflow call `forge`; `Build-Player.ps1` and `build-player.sh` are removed.
- The build window's CI Command rows: a `forge build` line beside the `unity run` line.
- The CI documentation: the section on scripts for local builds is rewritten around `forge`.

## Open questions

1. **Upload settings.** App IDs, release channels, Steam depots and the names of the credential variables: in the Build Forge profile, in a file in the repository, or in the CI job. The choice decides whether the package needs new settings.
2. **Version policy.** Which Build Forge versions a `forge` release supports.
3. **A build result file.** The package could also write a small JSON file at the end of each build, listing exactly what it produced. It isn't needed to find outputs, but it could serve archiving and checking uploads.
4. **The stall fix.** What `forge build` does about it depends on the open question in ARCHITECTURE.md's "The CI command can stall after a finished build".

## Order of work

1. The shared, Unity-free reading code in the package, with the editor-comparison tests.
2. The library, `forge profiles`, `forge output` and `forge build`, and the release workflow.
3. The upload settings design, then `forge upload meta` and `forge upload steam`.
4. The samples, the build window's CI Command rows and the CI documentation.
