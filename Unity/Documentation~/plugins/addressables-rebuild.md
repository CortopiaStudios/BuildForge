# Addressables Rebuild

Order 800. Needs Addressables (`com.unity.addressables`) with Addressables settings created in the project.

Builds the Addressables player content right before the player build, so a build never ships content that someone forgot to rebuild. It runs after [Addressables Stripper](addressables-stripper.md), so the content is built with the stripping in place.

## Settings

| Field | Meaning |
|---|---|
| Enabled | Off by default. |
| Clean Before Rebuild | Off by default. Cleans the Addressables build cache before building the content. |

## What a build does

The plugin builds the content with Addressables' default player content build. A content build error stops the player build with the error. The plugin has nothing to restore afterwards.

Addressables can also build its content as part of the player build itself (its **Build Addressables on Player Build** setting). Use one of the two for a profile, not both.

## Manifest and build window

- No manifest section.
- Build window line: `rebuild Addressables content before the build`, or `clean and rebuild Addressables content before the build`.
