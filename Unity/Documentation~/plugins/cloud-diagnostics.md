# Cloud Diagnostics

Order 500. Sets Unity Cloud Diagnostics crash reporting (`CrashReportingSettings.enabled`) for the build and restores the project's setting afterwards.

Crash reporting is also what makes Unity upload the build's IL2CPP symbols to its symbol server during the build. The plugin therefore controls both, and they cannot be separated: a build with crash reporting off neither uploads symbols nor reports crashes.

## Settings

| Field | Meaning |
|---|---|
| Enabled | Off by default. While off, builds use the project's crash reporting setting as it is. |
| Crash Reporting | **Always**, **CI only** (the default) or **Never**. |

With **CI only**, local builds skip the symbol upload, a network round trip they do not need, and crashes of developers' builds stay out of the production diagnostics. Which builds count as CI follows [CI detection](../ci.md#ci-detection).

While enabled, the foldout shows both outcomes and the project's own setting, for example `Local build: off, CI build: on (project setting: on)`.

## Build window

`crash reporting off (CI only; local build)`, or the same with `on` and `CI build`. The plugin adds nothing to the manifest.
