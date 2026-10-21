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
# Optional packages: the OpenXR, XR Loaders, Addressables Stripper and Addressables
#   Rebuild plugins, and their tests, compile only with their packages installed, so
#   the plain host leaves them out. BUILDFORGE_PACKAGES=oldest adds OpenXR,
#   XR Plug-in Management and Addressables at the oldest versions the plugins
#   support, or the oldest the editor allows where it requires newer ones,
#   BUILDFORGE_PACKAGES=newest at the newest versions they were tested with,
#   each in a host of its own; the run prints the versions Unity resolved.
#
# Host project location: ~/.cache/buildforge/test-host, or test-host-oldest or
#   test-host-newest with a package set
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

# Keep in step with run-tests.ps1, the asmdef floors and the manual's Tested with list.
PACKAGE_SET="${BUILDFORGE_PACKAGES:-}"
case "$PACKAGE_SET" in
  "") PACKAGES=() ;;
  oldest) PACKAGES=(com.unity.xr.openxr=1.16.0 com.unity.xr.management=4.5.3 com.unity.addressables=2.7.4) ;;
  newest) PACKAGES=(com.unity.xr.openxr=1.18.0 com.unity.xr.management=4.7.0 com.unity.addressables=4.1.0) ;;
  *) echo "BUILDFORGE_PACKAGES must be 'oldest' or 'newest', not '$PACKAGE_SET'."; exit 2 ;;
esac

PACKAGE_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
HOST_DIR="${BUILDFORGE_TEST_HOST:-$HOME/.cache/buildforge/test-host${PACKAGE_SET:+-$PACKAGE_SET}}"

# Oldest installed 6000.3.x editor (the support floor). Schema-agnostic on purpose: scan the JSON
# text for version tokens rather than assuming property names.
detect_installed_editor() {
  unity editors -i --json --no-banner 2>/dev/null \
    | grep -oE '6000\.3\.[0-9]+[abf][0-9]+' | sort -uV | head -n 1 || true
}

EDITOR_VERSION="${UNITY_EDITOR_VERSION:-$(detect_installed_editor)}"
EDITOR_VERSION="${EDITOR_VERSION:-$FALLBACK_EDITOR_VERSION}"

# Succeeds when version $1 is newer than $2; a release is newer than its pre-releases.
version_newer() {
  [ "$1" = "$2" ] && return 1
  local a="${1%%-*}" b="${2%%-*}"
  if [ "$a" = "$b" ]; then
    case "$1" in *-*) ;; *) return 0 ;; esac
    case "$2" in *-*) ;; *) return 1 ;; esac
    [ "$(printf '%s\n%s\n' "${1#*-}" "${2#*-}" | LC_ALL=C sort | tail -n 1)" = "${1#*-}" ]
    return
  fi
  [ "$(printf '%s\n%s\n' "$a" "$b" | sort -V | tail -n 1)" = "$a" ]
}

# A newer editor doesn't compile Unity's oldest packages (6000.6.6f1 requires Addressables 2.11.2,
# OpenXR 1.18.0 and XR Plug-in Management 4.7.0), so the oldest set starts each package at the
# editor's own minimum version where that is newer: the oldest a project on that editor can have.
RAISED=()
if [ "$PACKAGE_SET" = oldest ]; then
  # The "location" that follows the editor's "version" in the CLI's JSON, its \\ made /.
  location=$(unity editors -i --json --no-banner 2>/dev/null | awk -v want="\"$EDITOR_VERSION\"" '
    /"version":/ { current = $2; sub(/,$/, "", current) }
    /"location":/ && current == want { sub(/^[^:]*: *"/, ""); sub(/",? *$/, ""); gsub(/\\\\/, "/"); print; exit }' || true)
  editor_manifest=""
  if [ -n "$location" ]; then
    for candidate in "$(dirname "$location")/Data/Resources/PackageManager/Editor/manifest.json" \
                     "$(dirname "$location")/../Resources/PackageManager/Editor/manifest.json" \
                     "$location/Contents/Resources/PackageManager/Editor/manifest.json"; do
      if [ -f "$candidate" ]; then editor_manifest="$candidate"; break; fi
    done
  fi
  if [ -z "$editor_manifest" ]; then
    echo "The oldest set needs editor $EDITOR_VERSION installed: it reads the package versions that editor requires."
    exit 2
  fi
  for i in "${!PACKAGES[@]}"; do
    name="${PACKAGES[$i]%%=*}"; floor="${PACKAGES[$i]#*=}"
    # The package's "minimumVersion", read up to the end of its own block.
    minimum=$(awk -v key="\"$name\": {" 'found && /}/ { exit } found && /"minimumVersion"/ { gsub(/[",]/, "", $2); print $2; exit } index($0, key) { found = 1 }' "$editor_manifest")
    if [ -n "$minimum" ] && version_newer "$minimum" "$floor"; then
      RAISED+=("  $name $minimum: the oldest $EDITOR_VERSION allows (the plugins support $floor)")
      PACKAGES[$i]="$name=$minimum"
    fi
  done
fi

# Assets/ must exist — Unity's project validation requires it, even empty.
mkdir -p "$HOST_DIR/Assets" "$HOST_DIR/Packages" "$HOST_DIR/ProjectSettings"

OPTIONAL_PACKAGES=""
for package in ${PACKAGES[@]+"${PACKAGES[@]}"}; do
  OPTIONAL_PACKAGES+=$(printf ',\n    "%s": "%s"' "${package%%=*}" "${package#*=}")
done

cat > "$HOST_DIR/Packages/manifest.json" <<EOF
{
  "dependencies": {
    "com.cortopiastudios.buildforge": "file:$PACKAGE_DIR",
    "com.unity.test-framework": "1.5.1"$OPTIONAL_PACKAGES
  },
  "testables": ["com.cortopiastudios.buildforge"]
}
EOF

# Build Forge's settings and per-user state go every run, so a run starts from the defaults whatever
# an earlier one, or a test that didn't put a saved setting back, left in the host.
rm -f "$HOST_DIR/ProjectSettings/ForgeSettings.asset" "$HOST_DIR"/UserSettings/Forge*

# Rewritten every run so a stale pin from an earlier run cannot stick.
printf 'm_EditorVersion: %s\n' "$EDITOR_VERSION" \
  > "$HOST_DIR/ProjectSettings/ProjectVersion.txt"

echo "Test host: $HOST_DIR (editor $EDITOR_VERSION${PACKAGE_SET:+, $PACKAGE_SET packages})"
for line in ${RAISED[@]+"${RAISED[@]}"}; do echo "$line"; done
set +e
unity test "$HOST_DIR" --mode EditMode --output "$HOST_DIR/test-results.xml" "$@" \
  -- -logFile "$HOST_DIR/editor.log"
status=$?
set -e

# What Unity resolved, which an editor can raise above a pin.
LOCK_FILE="$HOST_DIR/Packages/packages-lock.json"
if [ -n "$PACKAGE_SET" ] && [ -f "$LOCK_FILE" ]; then
  echo "Packages ($PACKAGE_SET):"
  for package in "${PACKAGES[@]}"; do
    name="${package%%=*}"; pinned="${package#*=}"
    # The "version" line right after the package's own key.
    version=$(awk -v key="\"$name\": {" 'found && /"version"/ { gsub(/[",]/, "", $2); print $2; exit } index($0, key) { found = 1 }' "$LOCK_FILE")
    if [ -z "$version" ]; then note="not resolved"
    elif [ "$version" != "$pinned" ]; then note="$version, pinned $pinned"
    else note="$version"; fi
    echo "  $name $note"
  done
fi

if [ "$status" -ne 0 ] && [ -f "$HOST_DIR/editor.log" ]; then
  echo ""
  echo "--- last 60 lines of $HOST_DIR/editor.log ---"
  tail -n 60 "$HOST_DIR/editor.log"
fi

exit "$status"
