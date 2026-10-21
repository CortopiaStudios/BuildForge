# Runs the Build Forge EditMode test suite via the Unity CLI without a
# maintained wrapper project. Generates a minimal disposable host project that
# references this package (kept between runs so the Library import cache
# persists), then runs `unity test` against it.
#
# Usage: Tools~/run-tests.ps1 [extra `unity test` args...]
#   e.g. Tools~/run-tests.ps1 --allow-install
#        Tools~/run-tests.ps1 --filter UnityYamlParserTests
#
# Optional packages: the OpenXR, XR Loaders, Addressables Stripper and Addressables
#   Rebuild plugins, and their tests, compile only with their packages installed, so
#   the plain host leaves them out. BUILDFORGE_PACKAGES=oldest adds OpenXR,
#   XR Plug-in Management and Addressables at the oldest versions the plugins
#   support, BUILDFORGE_PACKAGES=newest at the newest versions they were tested with,
#   each in a host of its own; the run prints the versions Unity resolved.
#
# Host project location: %LOCALAPPDATA%\BuildForge\test-host, or test-host-oldest
#   or test-host-newest with a package set
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

# Keep in step with run-tests.sh, the asmdef floors and the manual's Tested with list.
$PackageSets = @{
    oldest = [ordered]@{ 'com.unity.xr.openxr' = '1.16.0'; 'com.unity.xr.management' = '4.5.3'; 'com.unity.addressables' = '2.7.4' }
    newest = [ordered]@{ 'com.unity.xr.openxr' = '1.18.0'; 'com.unity.xr.management' = '4.7.0'; 'com.unity.addressables' = '4.1.0' }
}
$PackageSet = $env:BUILDFORGE_PACKAGES
if ($PackageSet -and -not $PackageSets.Contains($PackageSet)) {
    Write-Host "BUILDFORGE_PACKAGES must be 'oldest' or 'newest', not '$PackageSet'."
    exit 2
}

$PackageDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$HostDir = if ($env:BUILDFORGE_TEST_HOST) { $env:BUILDFORGE_TEST_HOST }
           elseif ($PackageSet) { Join-Path $env:LOCALAPPDATA "BuildForge\test-host-$PackageSet" }
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
$OptionalPackages = ""
if ($PackageSet) {
    foreach ($Package in $PackageSets[$PackageSet].GetEnumerator()) {
        $OptionalPackages += ",`n    `"$($Package.Key)`": `"$($Package.Value)`""
    }
}
@"
{
  "dependencies": {
    "com.cortopiastudios.buildforge": "file:$PackagePath",
    "com.unity.test-framework": "1.5.1"$OptionalPackages
  },
  "testables": ["com.cortopiastudios.buildforge"]
}
"@ | Set-Content (Join-Path $HostDir "Packages/manifest.json")

# Rewritten every run so a stale pin from an earlier run cannot stick.
$ProjectVersionFile = Join-Path $HostDir "ProjectSettings\ProjectVersion.txt"
"m_EditorVersion: $EditorVersion" | Set-Content $ProjectVersionFile

Write-Host "Test host: $HostDir (editor $EditorVersion$(if ($PackageSet) { ", $PackageSet packages" }))"
# Forward slashes for everything handed to Unity: the editor's absolute-path
# detection rejects backslash paths ("Couldn't set project path" with the CWD
# prepended to an already-absolute path).
$HostDirCli = $HostDir -replace '\\', '/'
$EditorLog = Join-Path $HostDir "editor.log"
unity test $HostDirCli --mode EditMode --output "$HostDirCli/test-results.xml" @args `
    -- -logFile "$HostDirCli/editor.log"
$Status = $LASTEXITCODE

# What Unity resolved, which an editor can raise above a pin.
$LockFile = Join-Path $HostDir "Packages/packages-lock.json"
if ($PackageSet -and (Test-Path $LockFile)) {
    $Resolved = (Get-Content $LockFile -Raw | ConvertFrom-Json).dependencies
    Write-Host "Packages ($PackageSet):"
    foreach ($Package in $PackageSets[$PackageSet].GetEnumerator()) {
        $Version = $Resolved.($Package.Key).version
        $Note = if (-not $Version) { "not resolved" } elseif ($Version -ne $Package.Value) { "$Version, pinned $($Package.Value)" } else { $Version }
        Write-Host "  $($Package.Key) $Note"
    }
}

if ($Status -ne 0 -and (Test-Path $EditorLog)) {
    Write-Host ""
    Write-Host "--- last 60 lines of $EditorLog ---"
    Get-Content $EditorLog -Tail 60
}

exit $Status
