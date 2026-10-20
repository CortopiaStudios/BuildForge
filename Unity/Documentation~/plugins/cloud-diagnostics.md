# Cloud Diagnostics

Order 500. Sets Unity Cloud Diagnostics crash reporting (`CrashReportingSettings.enabled`) for the build and restores the project's setting afterwards.

## Crash reporting and symbol upload

During an Android build, Unity uploads the build's IL2CPP symbols, so that crash reports show function names and C# lines instead of addresses. Unity has no setting for the upload alone. Two settings start it, as long as the build's Debug Symbols aren't None:

- crash reporting, which this plugin sets;
- **Diagnostics Data** (Unity 6.2 and later), in **Project Settings > Services > Diagnostics**, which a Build Profile can override. It counts only in a project linked to Unity Cloud, and it also makes the built player send crash reports to Unity. This plugin leaves it as it is.

With crash reporting off, a build therefore still uploads its symbols while Diagnostics Data is on, and its player still reports crashes through Diagnostics. Turn Diagnostics Data off as well to stop both.

The upload uses the Unity account the editor is signed in to, or a token in the `USYM_UPLOAD_AUTH_TOKEN` environment variable. A build machine usually has neither, so Unity logs "Access token is empty. Native symbols will not be uploaded for this build." and the build goes on without the upload. To upload from CI, see Unity's [Upload debugging symbols from CI/CD pipelines](https://docs.unity.com/en-us/cloud/developer-data/upload-symbol-files-ci): a service account gets a new token for each build.

Checked in Unity 6000.3.0f1 and 6000.6.6f1.

## Settings

| Field | Meaning |
|---|---|
| Enabled | Off by default. While off, builds use the project's crash reporting setting as it is. |
| Crash Reporting | **Always**, **CI Only** (the default) or **Never**. |

With **CI Only**, local builds have crash reporting off, so crashes of developers' builds stay out of its reports, and, while Diagnostics Data is off, those builds skip the symbol upload. Which builds count as CI follows [CI detection](../ci.md#ci-detection).

While enabled, the foldout shows both outcomes and the project's own setting, for example `Local build: off, CI build: on (project setting: on)`.

## Build window

`crash reporting off (CI only; local build)`, or the same with `on` and `CI build`. The plugin adds nothing to the manifest.
