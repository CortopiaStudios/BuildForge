# Runs the Build Forge EditMode test suite via the Unity CLI without a
# maintained wrapper project. Generates a minimal disposable host project that
# references this package (kept between runs so the Library import cache
# persists), then runs `unity test` against it.
#
# Usage: Tools~/run-tests.ps1 [extra `unity test` args...]
#   e.g. Tools~/run-tests.ps1 --allow-install
#        Tools~/run-tests.ps1 --filter UnityYamlParserTests
#
# Host project location: %LOCALAPPDATA%\BuildForge\test-host
#   (override with the BUILDFORGE_TEST_HOST environment variable)
# Editor version: UNITY_EDITOR_VERSION if set, else the OLDEST installed
#   6000.3.x editor — the support floor, so an API that only exists in a
#   later patch is caught (via `unity editors -i`), else $FallbackEditorVersion.
#   Run again with UNITY_EDITOR_VERSION set to the newest installed editor.
#   (pass --allow-install to have the CLI install it).
#
# Exit code comes from `unity test`: 0 = all passed, 6 = tests ran and failed.
$ErrorActionPreference = "Stop"

$FallbackEditorVersion = "6000.3.0f1"

$PackageDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$HostDir = if ($env:BUILDFORGE_TEST_HOST) { $env:BUILDFORGE_TEST_HOST }
           else { Join-Path $env:LOCALAPPDATA "BuildForge\test-host" }

# Oldest installed 6000.3.x editor (the support floor). Schema-agnostic on purpose: scan the JSON
# text for version tokens rather than assuming property names.
function Get-InstalledEditorVersion {
    try {
        $raw = unity editors -i --json --no-banner 2>$null | Out-String
        $found = [regex]::Matches($raw, '6000\.3\.\d+[abf]\d+') |
            ForEach-Object { $_.Value } | Sort-Object -Unique
        if ($found) {
            return ($found |
                Sort-Object { [int]($_ -replace '^6000\.3\.(\d+).*$', '$1') } |
                Select-Object -First 1)
        }
    } catch { }
    return $null
}

$EditorVersion = if ($env:UNITY_EDITOR_VERSION) { $env:UNITY_EDITOR_VERSION }
                 else { Get-InstalledEditorVersion }
if (-not $EditorVersion) { $EditorVersion = $FallbackEditorVersion }

# Assets/ must exist — Unity's project validation requires it, even empty.
New-Item -ItemType Directory -Force -Path (Join-Path $HostDir "Assets") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $HostDir "Packages") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $HostDir "ProjectSettings") | Out-Null

$PackagePath = $PackageDir -replace '\\', '/'
@"
{
  "dependencies": {
    "com.cortopiastudios.buildforge": "file:$PackagePath",
    "com.unity.test-framework": "1.5.1"
  },
  "testables": ["com.cortopiastudios.buildforge"]
}
"@ | Set-Content (Join-Path $HostDir "Packages/manifest.json")

# Rewritten every run so a stale pin from an earlier run cannot stick.
$ProjectVersionFile = Join-Path $HostDir "ProjectSettings\ProjectVersion.txt"
"m_EditorVersion: $EditorVersion" | Set-Content $ProjectVersionFile

Write-Host "Test host: $HostDir (editor $EditorVersion)"
# Forward slashes for everything handed to Unity: the editor's absolute-path
# detection rejects backslash paths ("Couldn't set project path" with the CWD
# prepended to an already-absolute path).
$HostDirCli = $HostDir -replace '\\', '/'
$EditorLog = Join-Path $HostDir "editor.log"
unity test $HostDirCli --mode EditMode --output "$HostDirCli/test-results.xml" @args `
    -- -logFile "$HostDirCli/editor.log"
$Status = $LASTEXITCODE

if ($Status -ne 0 -and (Test-Path $EditorLog)) {
    Write-Host ""
    Write-Host "--- last 60 lines of $EditorLog ---"
    Get-Content $EditorLog -Tail 60
}

exit $Status
