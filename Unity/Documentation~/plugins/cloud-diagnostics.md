# Cloud Diagnostics

Order 500. Sets both of Unity's crash reporting settings for the build, and restores them afterwards:

- **Crash reporting**: Unity Cloud Diagnostics crash reporting (`CrashReportingSettings.enabled`), a project setting.
- **Diagnostics Data** (Unity 6.2 and later): makes the built player send crash reports to Unity, in a project linked to Unity Cloud. Its default is in **Project Settings > Services > Diagnostics**, and each Unity Build Profile can override it. The plugin sets the Build Profile's own Diagnostics Data to Enabled or Disabled for the build, so the build gets the plugin's choice whatever the profile and Project Settings say, and the profile keeps its own value.

A build with these settings changed rewrites `ProjectSettings/UnityConnectSettings.asset`, with values that hold only for the build or for the editor session, such as `m_Enabled` off in a build machine's editor. The plugin puts the file back byte for byte afterwards, so the build leaves nothing to commit or revert.

## Crash reporting and symbol upload

During an Android build, Unity uploads the build's IL2CPP symbols, so that crash reports show function names and C# lines instead of addresses. Either setting starts the upload, as long as the build's Debug Symbols aren't None, and Unity has no setting for the upload alone. A build with both off, as the plugin makes it, neither uploads symbols nor reports crashes.

The upload uses the Unity account the editor is signed in to, or a token in the `USYM_UPLOAD_AUTH_TOKEN` environment variable. A build machine usually has neither, so Unity logs "Access token is empty. Native symbols will not be uploaded for this build." and the build goes on without the upload. To upload from CI, see Unity's [Upload debugging symbols from CI/CD pipelines](https://docs.unity.com/en-us/cloud/developer-data/upload-symbol-files-ci): a service account gets a new token for each build.

The plugin finds a Build Profile's Diagnostics Data by the name Unity stores it under, as Unity has no API for it. If a later Unity version stores it elsewhere, builds of profiles with the plugin on stop and say so, rather than go out with Diagnostics Data as the profile has it.

Checked in Unity 6000.3.0f1 and 6000.6.6f1.

## Settings

| Field | Meaning |
|---|---|
| Enabled | Off by default. While off, builds use Unity's settings as they are. |
| Crash Reporting | **Always**, **CI Only** (the default) or **Never**: both settings on for every build, on for CI builds only, or off. |

With **CI Only**, local builds neither upload symbols nor report crashes, so developers' crashes stay out of the reports. Which builds count as CI follows [CI detection](../ci.md#ci-detection).

While enabled, the foldout shows both outcomes and Unity's settings as they are now, for example `Local build: off, CI build: on (now: crash reporting off, Diagnostics Data on)`.

## Build window

`crash reporting and Diagnostics Data off (CI only; local build)`, or the same with `on` and `CI build`. The plugin adds nothing to the manifest.
