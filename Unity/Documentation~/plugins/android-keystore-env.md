# Android Keystore Env

Order 600. Applies to Android profiles only.

Sets Android signing from environment variables for the build and restores the previous values afterwards. It is the third of three places signing can come from:

1. **Project Settings > Player**: the project's keystore path, passwords and key alias.
2. **The Unity Build Profile's own Player Settings**, when it has them.
3. **Environment variables**, read by this plugin at build time. A variable that is not set leaves the value from 1 or 2 alone.

The usual setup: developers build with what Player Settings say, typically debug signing; the CI job sets the variables from its secret store and gets release signing. No password ever needs to be committed.

## Default variable names

Set project-wide in **Project Settings > Build Forge > Installed Plugins > Android Keystore Env**:

| Parameter | Default variable |
|---|---|
| Keystore Path | `ANDROID_KEYSTORE_PATH` |
| Keystore Password | `ANDROID_KEYSTORE_PASSWORD` |
| Key Alias | `ANDROID_KEY_ALIAS` |
| Key Password | `ANDROID_KEY_PASSWORD` |

Change a name there when your CI already provides the value under another variable. An empty field there means the default name.

## Profile settings

| Field | Meaning |
|---|---|
| Enabled | Off by default. |
| Keystore Path, Keystore Password, Key Alias, Key Password | Each **inherits** the project-wide name (shown greyed, with an **Override** button) or **overrides** it with a name for this profile (with an **Inherit** button). An overridden field left empty skips that parameter for this profile. |

While enabled, the foldout shows how many of the variables this editor's process can see, for example `2/4 env var(s) detected — will override at build time`.

## What a build does

A set keystore path turns on Custom Keystore and sets the path; the other variables set their values. Variables that are not set are skipped. With no variable set at all, the build logs that it uses the existing settings and changes nothing.

These misconfigurations stop the build before any setting changes:

- a variable that is **set but empty**, which usually means a CI secret did not get filled in;
- a custom keystore in use with an **empty keystore path**;
- signing variables present while **no custom keystore** would be used, which would produce a debug-signed build.

Passwords and the key alias are not required to be set: they can come from Player Settings, and Gradle fails clearly on a wrong one. The plugin logs only variable names, never values.

After the build the previous values are restored, the keystore path exactly as Player Settings stored it.

## Tips

- The key alias is usually not secret. Keep it in Player Settings and leave its variable unset, or skip it per profile.
- The variables are read from the environment of the Unity process. For an editor build, start Unity Hub or the editor with them set; a variable added afterwards is not seen.
- On Jenkins, a file credential provides the keystore as a temporary file and puts its path in a variable; on GitHub Actions, write a base64 secret to a temporary file. The [Example Integration](../../Samples~/ExampleIntegration/README.md) sample has both.

Build window line: `keystore path / keystore password / key alias / key password from $ANDROID_KEYSTORE_PATH, $ANDROID_KEYSTORE_PASSWORD, skip, $ANDROID_KEY_PASSWORD`. The plugin adds nothing to the manifest.
