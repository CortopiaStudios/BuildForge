# Variants and XR loader selection

Configure project-wide names in **Project Settings > Build Forge > Build Variants**.
The selector and `-forgeVariant` still choose exactly one name. `Default` is the
unnamed build, not a second selector or an independent dimension.

Optional **Variant Rules** match a configured name or `Default`. Development Build
and Mark Product Name each support Inherit, Disabled and Enabled. Additional
Scripting Defines are aliases owned by the rules: changing variant removes aliases
from the other rules while retaining unrelated symbols. Put variant-owned aliases
in these rules rather than global Player Settings. Without rules, existing variant
behavior is unchanged.

Rule names must exactly match a configured variant or `Default`. Duplicate rules,
unknown or empty names, duplicate aliases within a rule, and invalid scripting
defines are errors. Each alias must be one C# preprocessor identifier;
`BUILD_VARIANT_` and `BUILD_PROFILE_` prefixes belong to Build Forge. Different
rules may share an alias. After renaming/removing a variant, update its rules and
any profile's Allowed Variants list. Invalid configuration is shown in Project
Settings/the profile inspector and blocks Apply and CI before settings change.

Disabling Development Build clears Unity's development, profiler connection, deep
profiling, script debugging and wait-for-debugger options. Enabling sets development
mode while retaining the profile's chosen debugging options, unless the rule's Build
Configuration sets them. A rule's Build Configuration also sets IL2CPP, stripping,
compression and Android link-time optimization and debug symbols for its builds,
each Inherit by default; the per-platform Player Settings are restored as Unity's
serialized maps, so ProjectSettings.asset is unchanged afterwards. Resolution happens
before plugins, content generation and manifest creation. Native profile flags and
`BuildPlayerWithProfileOptions.options` both receive the effective mode.

Restrict Variants in a profile's Build Forge settings limits named variants. Default is always
available. An optional restriction explanation appears with invalid selections.
Builds and editor Apply reject unsupported combinations before plugin changes.

Editor Apply writes the variant's defines to the native profile for Play Mode and
script compilation, never its development flags: those do not reach editor
compilation, and on a committed profile they would reach every machine without the
committer's ledger, where a Default build inherits them. Build Forge records only
what it changed, per user under UserSettings. Revert to Baseline, a variant switch
and builds undo exactly that change on the profile's current values, so edits made
in Unity's Build Profiles window after Apply are kept; a define the user changed
since keeps the user's value. Builds apply the rule's flags, snapshot temporary
flags, defines and product names and restore them in `finally`, including a
previously applied editor variant. As with other build hooks, a killed editor
process cannot execute `finally`.

A `BUILD_VARIANT_` define on a native profile that is not applied on this machine
can only have been saved while a variant was applied elsewhere, typically
committed. The build window, the profile inspector and the build log report it,
and Activate removes it together with the aliases the rules own, as builds do.
Builds recompute the defines either way.

`ForgeBuildContext.Variant` is the build-time source of truth;
`DevelopmentBuild` exposes the resolved mode. Additive overloads of
`IForgePlugin.Validate`, `DescribeBuild`, and `IForgeEditorApplicable.ApplyToEditor`
receive the selected variant. Old implementations still work. Editor hooks must
use that parameter rather than conditional compilation in the editor assembly.

Addressables Stripper keeps profile exclusions and adds the selected variant's
group/label exclusions. Addressables rebuilding is still required for exclusions
to affect the bundles. Exclusions are stored under the variant's name, so after
renaming or removing a variant the ones left under the old name stop every build
of the profile until they are moved to a variant or removed in its Addressables
Stripper settings; otherwise the renamed variant would ship the excluded content.

The optional **XR Loaders** plugin requires XR Management. Enable it per profile
and select saved loader assets in order. An enabled empty list disables XR; a
disabled plugin leaves the current configuration alone. Apply/Revert and builds
snapshot loader GUIDs, restore their order, and reject missing or duplicate
references. OpenXR validation reads this intended selection before it is applied.
The plugin is optional even for XR projects: when every profile for a Unity build
target uses the same loader list, ordinary XR Management settings are sufficient.
Enable it to enforce a list or to vary loaders/XR availability between profiles
sharing a build target.

Loader selection runs at order 100, OpenXR at 200, Addressables Stripper at 700
and Addressables Rebuild at 800. Custom plugins that inspect XR settings or
generate content should run after 200; plugins that select providers must run
before OpenXR. Post-build cleanup runs in reverse order.

To control controller interaction profiles per Build Profile, enable **Interaction
Profiles** in the global OpenXR plugin settings. This remains opt-in. Validation
explains when a stored interaction override is outside that scope. The OpenXR
manifest retains `enabledFeatures`/`disabledFeatures` as configured overrides and
adds `effectiveEnabledFeatures` from the settings after application. Check the
effective list when validating a player configuration. A managed feature that
refuses its Enable/Disable override aborts the build before content generation.

## Verification

`Tools~/run-variant-proof.ps1` creates a small Windows host (6000.3.23f1 by
default), builds and launches Development then Default, and injects pre-build
failures. Pass `-Editor <path>` to check another installed Editor; the script reads
that executable's version and pins the host accordingly. Use a separate
`-ProofProject <path>` for each Editor. The PowerShell proof launches `.exe`
players and is Windows-only; it is not cross-platform validation.
It checks actual player development mode, compiled aliases, manifest identity,
product marking, saved settings restoration and a previously applied Development
editor variant. The optional XR and Addressables test assemblies run in hosts with
those dependencies installed.

[The 2026-09-14 validation report](VALIDATION-2026-09-14.md) records that pass's test
and real-player results, package versions, reproduction commands and remaining
platform limits.
