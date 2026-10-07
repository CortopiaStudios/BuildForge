# Player Overrides window

A Unity Build Profile can have Player Settings of its own, instead of building with **Project Settings > Player**. Unity stores them as a complete copy of the Player Settings, which makes it hard to see what the profile changes on purpose, and a later change to Project Settings, such as a version bump, does not reach the copy.

Build Forge does not override Player Settings itself; a profile that needs different Player Settings uses Unity's own. The Player Overrides window shows the differences:

- **Matrix** mode compares every Build Profile that has Player Settings of its own with Project Settings, side by side.
- **Single Profile** mode shows what one Build Profile changes.

Open it with **Window > Build Forge > Player Overrides**. It opens in Matrix mode the first time and then in the mode you used last; the toolbar switches between the two.

Both modes compare the files on disk: the Build Profile assets (Build Forge saves a Build Profile with pending edits before reading it) and `ProjectSettings/ProjectSettings.asset`. After changing Project Settings, save the project and click **Refresh**.

## Matrix mode

One row per Player Setting that differs from Project Settings in at least one profile, in Project Settings order. One column per Unity Build Profile with Player Settings of its own, grouped by platform. Each platform's group starts with a **Project Settings** column holding that platform's values, so every cell holds one value, also for per-platform settings. Unity keys some settings separately for Windows and macOS, so those get separate groups.

| You see | Meaning |
|---|---|
| A highlighted cell | The profile's value differs from its platform's Project Settings value. |
| A dimmed value | The profile's value matches Project Settings. Turn off **Show Matching Values** to leave these cells empty. |
| A cell in the second highlight color | **Possibly stale**: every profile with Player Settings of its own holds the same value, unlike Project Settings. Often a Project Settings change that was not copied into the profiles; sometimes a deliberate shared override. Only marked with two or more such profiles. |
| "used by …" under a Project Settings column | Profiles of that platform without Player Settings of their own; they build with these values. |
| A line under the table | Profiles on platforms where no profile has Player Settings of its own. |

The legend under the table names the two highlight colors. Hover a cell for both values; the tooltip of a possibly stale cell says why it is marked.

Click a profile's name or one of its cells to select that Unity Build Profile and show it in the Project window. The search field filters rows by display name or property path. **Copy** puts the whole matrix on the clipboard as a Markdown table, every row whatever the filter, with property paths and raw values, followed by the profiles that build with Project Settings.

A typical use: after a version bump in Project Settings, the profiles' copies still hold the old version. If every profile with Player Settings of its own holds it, the matrix marks `bundleVersion` as possibly stale; otherwise the old values show as highlighted differences.

## Single Profile mode

A dropdown selects any Unity Build Profile in the project, also ones without Build Forge settings. The table lists the Player Settings the profile changes, as **Property**, **Platform Default** (the Project Settings value) and **Unity Build Profile Override**. A profile without Player Settings of its own says so, as does a copy identical to Project Settings.

The same view is in the Build Forge settings Inspector (Assets > Build Forge > Edit Build Forge Settings) as the **Player Overrides** foldout, with the number of differences in its title.

**Copy** exports the table as Markdown, `Property | Platform Default | Unity Build Profile Override`, with property paths and raw values.

## How values are shown and compared

- Colors are drawn as swatches, asset references as object fields, enums by their display names (also integer-backed ones, found through `PlayerSettings`), booleans as Yes and No.
- Per-platform settings, such as the application identifier or the scripting backend, are compared for the profile's own platform only; differences for other platforms are ignored. A Dedicated Server profile is compared on Unity's Server entries.
- Static and dynamic batching become separate rows, read from the profile's platform entry.
- A per-platform entry that one side does not have shows as `(default)`. The comparison reads only the serialized values, so an absent entry and an explicit value can differ although Unity would use the same value for both.
- Fields that never mean an override, such as `productGUID`, are left out. Unity writes null references and quotes differently in Build Profiles and Project Settings; the comparison treats those spellings as equal, so they never show as differences.
