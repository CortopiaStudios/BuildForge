# Build Forge — Learnings & Dead Ends

This document preserves institutional memory from Build Forge's development. It records approaches that were tried and abandoned, so future contributors (or the original authors returning after a break) don't unknowingly repeat the same work. The git history was wiped for the first release, 0.9.0 — this document is the only record of these dead ends.

See ARCHITECTURE.md for how the system works *now*.

## Abandoned Approaches

### Player Settings Override System

**What it was**: A full subsystem (~40 files at its peak) that let users override individual Player Settings per Build Forge Profile. Included a property browser window for discovering and adding overrides, typed editor fields (dropdowns for enums, toggles for bools, object pickers for assets), curated groupings by category and platform (Rendering, Scripting, iOS, Android, etc.), platform-affinity inference so non-applicable properties were hidden, and a capture/restore mechanism that applied overrides on pre-build and restored them on post-build.

**Why it was removed**: Replicating Unity's whole Player Settings system to get per-field overrides seemed too much work while Unity 6's Build Profiles offered a shortcut: they can already override Player Settings. It was not removed for lack of value. Unity's overrides are a full copy of every setting rather than the fields that differ, which is a real pain: what a profile changes is opaque, and its copy goes stale when Project Settings change. Keeping both override systems would also have meant two sources of truth. Using Unity's overrides has its own cost: Build Forge builds and maintains the visibility Unity doesn't provide, the diff view (which survived) and the Player Overrides matrix.

**The rule if it ever returns**: Build Forge Player Settings should only be available when the Unity Build Profile has no Player Settings of its own — exactly one source of truth.

### In-Process Multi-Build

**What it was**: A multi-select UI in the build window (checkboxes, Select All/None), sequential build execution in a single Unity process, a custom log file (`Logs/BuildForge.log`) with per-build sections, duplicate-output-path detection, platform-switch warnings, and a combined summary at the end.

**Why it was removed**: Building multiple profiles in one Unity process requires switching the active Build Profile between builds. When profiles target different platforms, this triggers a domain reload (asset reimport + script recompilation), which destroys all in-process state — build runner fields, plugin capture data, the log file handle. The `IsForgeBuildInProgress` flag was a static bool that reset on domain reload, so the interceptor would block the second build.

Even with `SessionState` persistence (added later), the fundamental problem remains: package changes between builds (e.g., one profile needs Addressables, another doesn't) require a domain reload that makes in-process orchestration unreliable.

**Where this belongs**: An out-of-process orchestrator that launches separate Unity instances per build. Each instance gets a clean domain, its own asset import state, and its own process lifetime. Build Forge runs one profile at a time; multi-profile is an external concern.

### VYaml Parser

**What happened**: A spec-compliant YAML parser ([VYaml](https://github.com/hadashiA/VYaml), MIT licensed) was vendored to replace regex-based YAML extraction. Its C# 11/12 features were laboriously downgraded to C# 9 (file-scoped namespaces, `scoped` keyword, raw string literals). It compiled, but could not parse Unity's serialized YAML — which is non-standard in ways that break compliant parsers:

- Unterminated quoted scalars (e.g., single-quoted values that span lines without proper YAML continuation)
- Tagged object references (`--- !u!114 &1`) as document markers
- Non-standard flow mappings in certain contexts

The parser threw `YamlTokenizerException` on real `ProjectSettings.asset` files. No amount of pre-processing could make Unity's YAML compliant, because the non-compliance is structural, not syntactic.

**The lesson**: Unity's YAML is a Unity-specific format that happens to look like YAML. A line-based parser that extracts top-level key-value pairs by indentation (the current `UnityYamlParser`) is the correct tool. It's simple, robust against format quirks, and handles the one thing Build Forge needs — flat property extraction — without trying to model the full YAML tree. Don't try to upgrade it to a "proper" parser.

### Native OpenXR PackageSettingsEditor via Locator Override

**Goal**: Replace Build Forge's custom override UI for OpenXR settings with Unity's native `PackageSettingsEditor`, gaining access to feature category grouping, target device toggles, cog dialogs, and automatic compatibility with new OpenXR features.

**Approach**: Use `IPackageSettings2.OverrideSettingsLocatorFunc` (via reflection — `OpenXRPackageSettings` is internal) to redirect `OpenXRSettings.GetSettingsForBuildTargetGroup` calls to a deep-copied `OpenXRSettings` instance. Draw `PackageSettingsEditor` on the global `OpenXRPackageSettings` — it would query through the locator and operate on the copy.

**What worked**:
- Reflection access to `OpenXRPackageSettings.Instance`, `m_SettingsLocatorFunc`, and `GetSettingsForBuildTargetGroupFromPackageSettings`
- The locator override correctly intercepted settings lookups (verified with a sentinel)
- Deep copying `OpenXRSettings` + features (`Instantiate` is shallow for features — the `features` array must be replaced with individually-instantiated copies via reflection on the internal `features` field)

**Why it failed**: `FeatureHelpersInternal.GetAllFeatureInfo` (called by `PackageSettingsEditor`) treats an overridden locator as a signal that the settings instance is a MockRuntime test instance. It then filters features by name containing `"MockRuntime"` and calls `AssetDatabase.AddObjectToAsset(newFeature, openXrSettings)` for every `[OpenXRFeature]` type that doesn't match — which fails because the copy isn't a persistent asset.

The locator override is internal MockRuntime infrastructure, not a general "draw editor on a copy" hook.

**Current approach**: Native `Editor.CreateEditor` on individual `OpenXRFeature` copies (`Instantiate(feature)` is a proper deep copy at the individual feature level). Build Forge draws its own override UI for the top-level `OpenXRSettings` properties but delegates feature sub-settings to Unity's native editor. The native editor's `ApplySettingsOverride` side effect (pushing copy values to real settings on every repaint) is mitigated by capturing/restoring real settings state around `OnInspectorGUI`.

### OpenXR Feature Settings UI Reconstruction

**What happened**: Before discovering the native-editor-on-copies approach, a long iteration (~15 commits) attempted to manually reconstruct the per-feature settings UI using `EditorGUILayout` primitives. This involved progressively more desperate indentation strategies:

1. `EditorGUI.indentLevel++` — didn't visually indent Unity's popup controls
2. Increased `EditorGUIUtility.labelWidth` — broke label alignment
3. `EditorGUI.Popup` with manually offset rects — broke dropdown positioning
4. Vertical groups with left margins — nested incorrectly
5. `PrefixLabel` bypass — misaligned labels and values

Each attempt fixed one control type and broke another. The fundamental problem: IMGUI has no layout model for "indent this subsection's mixed control types consistently." Unity's own feature editors solve this by being full custom editors that control their own layout.

**The lesson**: Don't reconstruct native Unity inspector UI. If Unity has a custom editor for a type, instantiate a copy and call `CreateEditor`/`OnInspectorGUI`. The copy isolates your state; the native editor handles layout. This principle applies broadly, not just to OpenXR.

### Per-Version Build Profile Defaults Table

**What it was**: A hardcoded `Dictionary<string, Dictionary<string, string>>` keyed by Unity version (`"6000.3"` → 12 property/value pairs). When the diff view compared a Build Profile against `ProjectSettings.asset`, it suppressed entries that matched these known "initialization differences" — properties where Unity writes different default values to a new Build Profile than what the global settings hold.

Maintaining it required running a capture tool (`Window > Build Forge > Capture Build Profile Defaults`) on every new Unity version, selecting a manually-created fresh Build Profile, and pasting the resulting C# dictionary into source code.

An ambitious runtime derivation system was then built: it used Unity's internal `BuildProfileModuleUtil.CreatePlayerSettingsFromGlobal` (via reflection) to create a transient in-memory Build Profile, extracted its player-settings YAML via `SerializedObject`, diffed it against `ProjectSettings.asset`, cached the result in a `ScriptableSingleton`, and fell back to the hardcoded table if reflection failed. This worked but was solving the wrong problem.

**The root cause was a YAML quote-escaping bug**: Build Profiles store each player-settings line as a YAML single-quoted scalar, which doubles any single quote inside the content (`'` → `''`). The diff view's extractor captured the doubled quotes and compared them against the un-doubled values in `ProjectSettings.asset`, producing mismatches for any value containing a single quote (e.g., `keystoreName: '{inproject}: '`). These false diffs were the only reason the defaults table existed.

**The fix**: Un-escape `''` → `'` when extracting Build Profile YAML. With that one-line fix, every false positive disappeared. The entire defaults infrastructure (hardcoded table, runtime deriver, ScriptableSingleton cache, capture tool, and two test files — ~700 lines total) was deleted.

The remaining false-diff sources were each fixed at the root:
- `{instanceID: 0}` vs `{fileID: 0}` — canonicalize during normalization, not via a suppression list
- Metadata fields (`productGUID`, `serializedVersion`, `m_Script`) — structural filter that never changes between Unity versions

**The lesson**: When false positives appear in a comparison tool, fix the comparison before building a suppression system. Suppression tables grow, rot, and mask new bugs. Root-cause fixes eliminate entire categories of problems.

## Hard-Won Technical Gotchas

These are current-code issues that took real debugging time to discover. They are also documented as pitfalls in ARCHITECTURE.md, but the narrative context of *how* they were discovered is preserved here.

### YAML Single-Quote Un-Escaping

Unity wraps each player-settings line in a YAML single-quoted scalar: `- line: '|   keystoreName: ''{inproject}: '''`. The `''` is YAML's escape for a literal `'` inside a single-quoted string. If you extract the content and don't un-escape, values containing quotes compare unequal to the same value in `ProjectSettings.asset` — producing false diffs that look real. The fix is `.Replace("''", "'")` on extraction. This was the root cause of the entire per-version defaults table (see above).

### Null-Reference Representation Mismatch

`ProjectSettings.asset` serializes null object references as `{fileID: 0}`. Build Profile YAML uses `{instanceID: 0}`. Both mean null. Without canonicalizing one to the other during normalization, any property containing a null reference (including *nested* in a struct, like `m_AndroidBanners` → `banner: {instanceID: 0}`) shows as a diff. The top-level null check isn't enough — the canonicalization must happen inside the whitespace-collapsed string so nested refs are caught: `Regex.Replace(collapsed, @"\{instanceID:\s*0\}", "{fileID: 0}")`.

### Float Parsing and Locale

`float.TryParse("0.5")` fails silently on comma-decimal locales (sv-SE, de-DE — relevant since the author is Swedish). Color swatches in the diff view broke, and OpenXR settings snapshots serialized on one locale couldn't be deserialized on another. Always use `NumberStyles.Float, CultureInfo.InvariantCulture` for any float that touches serialization or cross-machine data.

### Static Fields Reset on Domain Reload

`ForgeBuildRunner.IsForgeBuildInProgress` was a static bool. When a build targets a different platform than the active one, `BuildPipeline.BuildPlayer` switches the active target, which triggers a domain reload. The static resets to `false`, causing the interceptor to block Build Forge's own build and the startup cleanup to delete the manifest mid-build. Fix: `SessionState.GetBool/SetBool` (survives domain reloads, clears on editor restart).

### Git Process Deadlock Pattern

`process.StandardOutput.ReadToEnd()` called before `process.WaitForExit(timeout)` makes the timeout meaningless — `ReadToEnd` blocks until the child closes stdout, which might be never (credential prompt, fsmonitor hook stall). Meanwhile, `RedirectStandardError = true` without draining stderr risks a pipe-buffer deadlock: the child blocks writing stderr while the parent blocks reading stdout. Fix: read stdout and stderr asynchronously (`ReadToEndAsync()`), then `WaitForExit(timeout)`, then `Kill()` on timeout, and always `Dispose()`.

### EditorGUI.DisabledScope and Enum Crash

`SerializedProperty.enumValueIndex` returns `-1` when the stored integer value doesn't match any enum member (common after a package upgrade changes the enum). Indexing `enumDisplayNames[-1]` throws `IndexOutOfRangeException` on every repaint, breaking the entire inspector. Guard: `enumIdx >= 0 && enumIdx < enumNames.Length`.
