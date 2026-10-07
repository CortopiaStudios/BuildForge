# Addressables Stripper

Order 700. Needs Addressables (`com.unity.addressables`) with Addressables settings created in the project (**Window > Asset Management > Addressables > Groups**).

Leaves Addressables groups and labelled entries out of a build, for example spoiler content in a public demo. It has no per-profile switch; a profile without exclusions is not affected.

## Settings

| Field | Meaning |
|---|---|
| Exclude Groups | A toggle per Addressables group. Read-only groups are not listed. |
| Exclude Labels | A toggle per Addressables label. |
| Additional Exclusions by Variant | A toggle for `Default` and each configured variant. A ticked one shows group and label toggles that are excluded in addition, for that variant's builds only. |

## What a build does

- An excluded group gets **Include in Build** off for the duration of the build.
- Entries carrying an excluded label are moved into a temporary group that is not included in the build, named `BuildForge Excluded (temporary, safe to delete)`. Removing only the label would leave the entry in its group, still packed and loadable by address. Entries in excluded or read-only groups stay where they are.

After the build, the groups' flags are restored and the entries moved back; the temporary group is removed. An entry that cannot be moved back is left in the temporary group, with a warning in the log.

An excluded asset that a shipped asset references is still packed as a dependency; Addressables packs dependencies whatever group they are in.

## A content build must run during the build

The exclusions exist only while the build runs, so content built before it, for example from the Addressables Groups window, still contains everything. The build therefore needs a content build of its own, and stops with an explanation when neither of these is set:

- the [Addressables Rebuild](addressables-rebuild.md) plugin enabled for the profile, which runs right after this one;
- Addressables' **Build Addressables on Player Build** set to build with the player, in the Addressables settings asset.

The per-user Addressables preference is not taken into account, because what a build contains must not depend on the machine.

## Renamed or removed variants

Exclusions are stored under the variant's name. After a variant is renamed or removed, exclusions left under the old name would apply to no build, and the renamed variant would ship the content they excluded. Every build of the profile therefore stops until they are dealt with: the foldout lists them with **Move to…**, to give them to a variant that has no exclusions yet, and **Remove**.

## Manifest and build window

- Manifest section `Addressables`, with `excludedGroups` and `excludedLabels`, separated by `;`, when the build excluded something.
- Build window line: `exclude 2 group(s) and 1 label(s) from the build`, counting the variant's additional exclusions.
