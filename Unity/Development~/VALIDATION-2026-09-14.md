# Variants, XR and CLI validation — 2026-09-14

All runs below used Windows and standalone Unity CLI 1.0.0-beta.9. These are
package/Windows-player checks, not Android or headset acceptance.

| Unity Editor | Full-suite OpenXR / Addressables | EditMode tests | Development → Default players | Injected failures |
|---|---|---:|---|---|
| 6000.3.0f1 | 1.16.1 / 2.10.3 | 352/352 | Passed | Passed |
| 6000.3.23f1 | 1.18.0 / 2.9.1 | 352/352 | Passed | Passed |
| 6000.6.0f1 | 1.16.1 / 2.10.3 | 352/352 | Passed | Passed |

The clean 6000.3.0f1 host without XR or Addressables passed 339 tests
with 2 expected `PlayerSettingsProfileTests` skips (no active profile).
The full hosts have active native profiles and no skipped tests.

The real player proof uses OpenXR 1.18.0 and Addressables 2.9.1 on all three
Editors. It builds and launches Windows Mono players and checks
`Debug.isDebugBuild`, compiled `DEVELOPMENT_BUILD`/variant aliases, manifest
development mode and identity, product-name marking, and saved settings after
Development → Default. Failure injection checks both variant states, restoration
of an already-applied editor Development variant, cleanup execution and preloaded
assets after a Unity player-build failure.

The EditMode suite covers invalid and duplicate rule names, invalid/reserved
defines, stale/duplicate allowed variants, shared aliases, pre-mutation rejection,
native development flags, editor Apply/Revert, XR loader ordering/restoration,
OpenXR overrides and the loader → OpenXR → content execution order.

## Reproduce

Use the normal `Tools~/run-tests.ps1` runner for a host without optional packages;
set `UNITY_EDITOR_VERSION` to the version under test. Run from a CI account with
the standalone Unity CLI on PATH, a licensed Editor and the platform modules.

For the real player proof, use a separate disposable host per Editor:

```powershell
& './Tools~/run-variant-proof.ps1' `
  -Editor 'C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Unity.exe' `
  -ProofProject 'C:/BuildForgeProof/6000.3.23f1'

unity test C:/BuildForgeProof/6000.3.23f1 --mode EditMode `
  --filter BuildForge.Tests --output C:/BuildForgeProof/6000.3.23f1/tests.xml `
  -- -nographics -activeBuildProfile Assets/Proof.asset
```

The proof runner records the selected executable's actual version in the host.
Change both paths to test a different Editor. Its pinned optional packages differ
from the older 1.16.1/2.10.3 full-suite hosts listed above.

The raw evidence for this pass (test results and logs, build and player logs, and
the proof JSON files) is retained by the maintainer and not distributed with this
package.

## Limits

macOS/Linux CLI and player execution remain unverified. Private serialized native
profile fields remain a Unity compatibility boundary: explicit development rules
are checked before mutation, and the suite verifies the Windows fields on the
versions above. Other platform modules and third-party plugins need their own
acceptance. External plugins relying on OpenXR's former order 600 must account
for its documented order 300 before Addressables generation.
