# Addressables Stripper

Order 700. Needs Addressables (`com.unity.addressables`) with Addressables settings created in the project (**Window > Asset Management > Addressables > Groups**).

Leaves Addressables groups and labelled entries out of a build, for example spoiler content in a public demo. It has no per-profile switch; a profile without exclusions is not affected.

## Settings

| Field | Meaning |
|---|---|
| Exclude Groups | A toggle per Addressables group. Read-only groups are not listed. |
| Exclude Labels | A toggle per Addressables label. |
| Additional Exclusions by Variant | A toggle for each configured variant, shown once variants exist. A ticked one shows group and label toggles that are excluded in addition, for that variant's builds only. |

## What a build does

- An excluded group gets **Include in Build** off for the duration of the build. From Addressables 4.1 that is the group's own setting, which every kind of group has, Content Directory groups included. Before 4.1 it is the setting of the group's **Content Packing & Loading** schema, so a group without that schema can't be excluded, and its exclusion stops the build.
- Entries carrying an excluded label are moved into a temporary group that is not included in the build, named `BuildForge Excluded (do not delete)`. Removing only the label would leave the entry in its group, still packed and loadable by address. Entries in excluded or read-only groups stay where they are.

After the build, the groups' flags are restored and the entries moved back; the temporary group is removed. An entry that cannot be moved back is left in the temporary group, with a warning in the log.

These changes are saved during the build, by Addressables and by the build's own save. After an editor crash or a killed build, the excluded groups stay excluded and the moved entries stay in the temporary group, where every later build, of any profile, would leave them out, so builds stop until the record of that build is cleared. Check `git status` and `git diff`, restore the files under `Assets/AddressableAssetsData/` that the build changed and keep your own edits, then clear the record with **Clear** in the build window; see [The editor crashed during a build](../troubleshooting.md#the-editor-crashed-during-a-build). Don't delete the temporary group: that deletes the entries in it.

An excluded asset that a shipped asset references is still packed as a dependency; Addressables packs dependencies whatever group they are in.

## A content build must run during the build

The exclusions exist only while the build runs, so content built before it, for example from the Addressables Groups window, still contains everything. The build therefore needs a content build of its own, and stops with an explanation when neither of these is set:

- the [Addressables Rebuild](addressables-rebuild.md) plugin enabled for the profile, which runs right after this one;
- Addressables' **Build Addressables on Player Build** set to build with the player, in the Addressables settings asset.

The per-user Addressables preference is not taken into account, because what a build contains must not depend on the machine.

## Without Addressables settings

With the Addressables package installed but no Addressables settings in the project, for example after `Assets/AddressableAssetsData` was deleted or never committed, no exclusion can take effect, while Unity's player build still copies the content built before into the player. A build whose variant has exclusions therefore stops before anything changes. The foldout says so and lists the stored exclusions with **Remove**, for a project that no longer uses Addressables; creating the settings again in **Window > Asset Management > Addressables > Groups** brings the group and label lists back.

## Renamed or removed groups and labels

Exclusions are stored by name. After a group or label is renamed or removed, an exclusion of the old name would exclude nothing, and the content it excluded would ship. A build that uses such an exclusion therefore stops before anything changes, and the foldout lists it as not found, with **Remove**.

## Renamed or removed variants

Exclusions are stored under the variant's name. After a variant is renamed or removed, exclusions left under the old name would apply to no build, and the renamed variant would ship the content they excluded. Every build of the profile therefore stops until they are dealt with: the foldout lists them with **Move to…**, to give them to a variant that has no exclusions yet, and **Remove**.

## Manifest and build window

- Manifest section `Addressables`, with `excludedGroups` and `excludedLabels`, separated by `;`, when the build excluded something.
- Build window line: `exclude 2 group(s) and 1 label(s) from the build`, counting the variant's additional exclusions.
