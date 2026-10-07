#!/usr/bin/env bash
# Builds one Unity Build Profile with Build Forge from the command line.
#
# Runs the command the Build Forge window shows under CI Command: the standalone
# Unity CLI ("unity run") starts the editor version named in
# ProjectSettings/ProjectVersion.txt, makes the Unity Build Profile active before
# scripts compile (-activeBuildProfile), and runs Build Forge's entry point
# (BuildForge.CommandLine.Build), which builds, restores everything it changed and
# exits with the result. With --shared-workspace, it first runs the Shared
# workspace command the window shows: BuildForge.CommandLine.Activate switches the
# workspace to the profile in a run of its own. With --editor, the script starts
# that Unity editor directly instead, for machines without the Unity CLI.
#
# Run it from the repository root:
#   bash build-player.sh --profile "Assets/Settings/Build Profiles/Quest.asset" [options]
#
# Options:
#   --profile <path>     Unity Build Profile asset, relative to the Unity project (required)
#   --variant <name>     Build Forge variant, or Default (default: Default)
#   --project <path>     Unity project folder, relative to the current directory (default: .)
#   --local              count the build as local (-forgeCI false) although it runs in batch mode
#   --shared-workspace   activate the profile first, in a run of its own, for a workspace that
#                        also builds profiles of other platforms, such as a job that builds any
#                        profile; Unity 6000.3.23 can fail to compile when -activeBuildProfile
#                        changes platform (see the manual's Command line and CI page)
#   --timeout <seconds>  Unity CLI only: stop each editor run after this many seconds
#   --editor <path>      start this Unity editor executable directly instead of the Unity CLI;
#                        it must be the project's editor version
#   --log <file>         where the editor log goes (default: -, this terminal); with
#                        --shared-workspace, the Activate run logs to <file> with -activate
#                        before its extension
#
# Exit code: 0 when the build and its cleanup succeeded. Anything else is a
# failure: the Unity CLI reports a failed build as 6, a directly started editor as 1.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: bash build-player.sh --profile <Build Profile asset> [--variant <name>] [--project <path>]
                            [--local] [--shared-workspace] [--timeout <seconds>]
                            [--editor <Unity executable>] [--log <file>]
Run it from the repository root. See the comment at the top of this file for the options.
EOF
}

need_value() {
  if [ "$#" -lt 2 ] || [ -z "$2" ]; then
    echo "$1 needs a value." >&2
    exit 2
  fi
}

profile=""
variant="Default"
project="."
local_build=false
shared_workspace=false
timeout=""
editor=""
log="-"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --profile) need_value "$@"; profile="$2"; shift 2 ;;
    --variant) need_value "$@"; variant="$2"; shift 2 ;;
    --project) need_value "$@"; project="$2"; shift 2 ;;
    --local) local_build=true; shift ;;
    --shared-workspace) shared_workspace=true; shift ;;
    --timeout) need_value "$@"; timeout="$2"; shift 2 ;;
    --editor) need_value "$@"; editor="$2"; shift 2 ;;
    --log) need_value "$@"; log="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [ -z "$profile" ]; then
  echo "--profile is required." >&2
  usage >&2
  exit 2
fi
if [ ! -f "$project/$profile" ]; then
  echo "Build Profile not found: $project/$profile. Run the script from the repository root, or pass --project." >&2
  exit 2
fi

# Build Forge's part of the command. -forgeVariant Default is the default build.
forge_args=(-executeMethod BuildForge.CommandLine.Build -activeBuildProfile "$profile" -forgeVariant "$variant")
if [ "$local_build" = true ]; then
  forge_args+=(-forgeCI false)
fi

# An inherited UNITY_EDITOR_VERSION would make the Unity CLI start another editor
# than the one ProjectVersion.txt names.
unset UNITY_EDITOR_VERSION

if [ -z "$editor" ]; then
  if ! command -v unity >/dev/null 2>&1; then
    echo 'The Unity CLI ("unity") is not on the PATH. Install it, or pass --editor with the path of a Unity editor.' >&2
    exit 2
  fi
  cli_args=(run "$project" --non-interactive)
  if [ -n "$timeout" ]; then
    cli_args+=(--timeout "$timeout")
  fi
fi

# Runs one editor with Build Forge's arguments: run_unity <log> <arguments...>.
# Its status is the editor's exit code.
run_unity() {
  local log_file="$1"
  shift
  if [ -n "$editor" ]; then
    echo "$editor -batchmode -nographics -silent-crashes -logFile $log_file -cacheServerWaitForUploadCompletion -projectPath $project $*"
    "$editor" -batchmode -nographics -silent-crashes -logFile "$log_file" -cacheServerWaitForUploadCompletion \
      -projectPath "$project" "$@"
  else
    echo "unity ${cli_args[*]} -- -nographics -silent-crashes -logFile $log_file -cacheServerWaitForUploadCompletion $*"
    unity "${cli_args[@]}" -- -nographics -silent-crashes -logFile "$log_file" -cacheServerWaitForUploadCompletion "$@"
  fi
}

if [ "$shared_workspace" = true ]; then
  # The workspace also builds profiles of other platforms: switch it to this profile
  # in a run of its own, so the build's editor starts on it.
  activate_log="$log"
  if [ "$log" != "-" ]; then
    if [[ "${log##*/}" == *.* ]]; then
      activate_log="${log%.*}-activate.${log##*.}"
    else
      activate_log="$log-activate"
    fi
  fi
  set +e
  run_unity "$activate_log" -executeMethod BuildForge.CommandLine.Activate -forgeBuildProfile "$profile"
  status=$?
  set -e
  if [ "$status" -ne 0 ]; then
    echo "Activate failed: exit code $status." >&2
    exit "$status"
  fi
fi

set +e
run_unity "$log" "${forge_args[@]}"
status=$?
set -e

if [ "$status" -ne 0 ]; then
  echo "Build failed: exit code $status." >&2
  exit "$status"
fi
echo "Build succeeded."
