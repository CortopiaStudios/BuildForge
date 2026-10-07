#!/usr/bin/env bash
# Runs the Build Forge EditMode test suite via the Unity CLI without a
# maintained wrapper project. Generates a minimal disposable host project that
# references this package (kept between runs so the Library import cache
# persists), then runs `unity test` against it.
#
# Usage: Tools~/run-tests.sh [extra `unity test` args...]
#   e.g. Tools~/run-tests.sh --allow-install
#        Tools~/run-tests.sh --filter UnityYamlParserTests
#
# Host project location: ~/.cache/buildforge/test-host
#   (override with the BUILDFORGE_TEST_HOST environment variable)
# Editor version: UNITY_EDITOR_VERSION if set, else the OLDEST installed
#   6000.3.x editor — the support floor, so an API that only exists in a
#   later patch is caught (via `unity editors -i`), else FALLBACK_EDITOR_VERSION.
#   Run again with UNITY_EDITOR_VERSION set to the newest installed editor.
#   (pass --allow-install to have the CLI install it).
#
# Exit code comes from `unity test`: 0 = all passed, 6 = tests ran and failed.
set -euo pipefail

FALLBACK_EDITOR_VERSION="6000.3.0f1"

PACKAGE_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
HOST_DIR="${BUILDFORGE_TEST_HOST:-$HOME/.cache/buildforge/test-host}"

# Oldest installed 6000.3.x editor (the support floor). Schema-agnostic on purpose: scan the JSON
# text for version tokens rather than assuming property names.
detect_installed_editor() {
  unity editors -i --json --no-banner 2>/dev/null \
    | grep -oE '6000\.3\.[0-9]+[abf][0-9]+' | sort -uV | head -n 1 || true
}

EDITOR_VERSION="${UNITY_EDITOR_VERSION:-$(detect_installed_editor)}"
EDITOR_VERSION="${EDITOR_VERSION:-$FALLBACK_EDITOR_VERSION}"

# Assets/ must exist — Unity's project validation requires it, even empty.
mkdir -p "$HOST_DIR/Assets" "$HOST_DIR/Packages" "$HOST_DIR/ProjectSettings"

cat > "$HOST_DIR/Packages/manifest.json" <<EOF
{
  "dependencies": {
    "com.cortopiastudios.buildforge": "file:$PACKAGE_DIR",
    "com.unity.test-framework": "1.5.1"
  },
  "testables": ["com.cortopiastudios.buildforge"]
}
EOF

# Rewritten every run so a stale pin from an earlier run cannot stick.
printf 'm_EditorVersion: %s\n' "$EDITOR_VERSION" \
  > "$HOST_DIR/ProjectSettings/ProjectVersion.txt"

echo "Test host: $HOST_DIR (editor $EDITOR_VERSION)"
set +e
unity test "$HOST_DIR" --mode EditMode --output "$HOST_DIR/test-results.xml" "$@" \
  -- -logFile "$HOST_DIR/editor.log"
status=$?
set -e

if [ "$status" -ne 0 ] && [ -f "$HOST_DIR/editor.log" ]; then
  echo ""
  echo "--- last 60 lines of $HOST_DIR/editor.log ---"
  tail -n 60 "$HOST_DIR/editor.log"
fi

exit "$status"
