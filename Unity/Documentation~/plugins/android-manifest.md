# Android Manifest

Order 900. Gives an Android profile its own main manifest. Only applicable to Android profiles.

Unity reads an Android build's custom main manifest from one path, `Assets/Plugins/Android/AndroidManifest.xml`, and only while **Player Settings > Publishing Settings > Custom Main Manifest** is on. Every Android profile shares that file. With a manifest set here, a build of the profile:

1. puts the profile's file at `Assets/Plugins/Android/AndroidManifest.xml`, and turns Custom Main Manifest on in the Player Settings the build reads (the profile's own when it has them);
2. builds, with the vendors' Gradle steps patching the profile's manifest as usual;
3. puts the project's file and setting back. If the project had no file, the file is removed again, with any `.meta` file and folder created for it.

The restore runs even when the build fails.

## Settings

| Field | Meaning |
|---|---|
| Main Manifest | The profile's own manifest, an XML file anywhere in the project. None builds with the project's manifest. |

The build stops when the file is not an Android manifest (its root element must be `<manifest>`). The build window also warns when the field names the project's own `Assets/Plugins/Android/AndroidManifest.xml`.

## Build window and manifest

`main manifest Assets/Settings/Build Profiles/Frame AndroidManifest.xml`, or `the project's main manifest`. The build manifest gets an `AndroidManifest` section with `mainManifest` when one is set.

## With the XR Vendor Filter

The [XR Vendor Filter](xr-vendor-filter.md) removes the entries XR vendors write into every Android build's manifest. That includes the profile's own, so a Steam Frame manifest doesn't need to list what to leave out. Use Android Manifest for what the profile needs that the others don't: its own permissions, features, intent filters or metadata.
