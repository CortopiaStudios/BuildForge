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
#   support, or the minimum versions the editor names where those are newer,
#   BUILDFORGE_PACKAGES=newest at the newest versions they were tested with,
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

# 1 when version Left is newer than Right, -1 when older, 0 when equal; a release is newer than its
# pre-releases. (PowerShell names are case-insensitive, so the parts can't be called $a and $b.)
function Compare-PackageVersion([string]$Left, [string]$Right) {
    $leftParts = @($Left -split '-', 2)
    $rightParts = @($Right -split '-', 2)
    $core = ([version]$leftParts[0]).CompareTo([version]$rightParts[0])
    if ($core -ne 0) { return [Math]::Sign($core) }
    if ($leftParts.Count -eq 1 -and $rightParts.Count -eq 1) { return 0 }
    if ($leftParts.Count -eq 1) { return 1 }
    if ($rightParts.Count -eq 1) { return -1 }
    return [Math]::Sign([string]::CompareOrdinal($leftParts[1], $rightParts[1]))
}

$Packages = [ordered]@{}
if ($PackageSet) {
    foreach ($Package in $PackageSets[$PackageSet].GetEnumerator()) { $Packages[$Package.Key] = $Package.Value }
}
# A newer editor may not compile the oldest versions the plugins support (Addressables 2.7.4 and
# OpenXR 1.16.0 fail on 6000.6.6f1), so the oldest set starts each package at the minimum version
# the editor's Package Manager manifest names for it, where that is newer: 6000.6.6f1 names
# Addressables 2.11.2, OpenXR 1.18.0 and XR Plug-in Management 4.7.0. A project can still pin an
# older version, which may or may not compile there.
$Raised = @()
if ($PackageSet -eq 'oldest') {
    $Editor = $null
    try {
        $Editor = (unity editors -i --json --no-banner 2>$null | Out-String | ConvertFrom-Json).data |
            Where-Object { $_.version -eq $EditorVersion } | Select-Object -First 1
    } catch { }
    $EditorManifest = $null
    if ($Editor) {
        $EditorDir = Split-Path $Editor.location
        $EditorManifest = @(
            (Join-Path $EditorDir "Data/Resources/PackageManager/Editor/manifest.json"),
            (Join-Path $EditorDir "../Resources/PackageManager/Editor/manifest.json"),
            (Join-Path $Editor.location "Contents/Resources/PackageManager/Editor/manifest.json")
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $EditorManifest) {
        Write-Host "The oldest set needs editor $EditorVersion installed: it reads the minimum package versions that editor names."
        exit 2
    }
    $Required = (Get-Content $EditorManifest -Raw | ConvertFrom-Json).packages
    foreach ($Name in @($Packages.Keys)) {
        $Minimum = $Required.$Name.minimumVersion
        if ($Minimum -and (Compare-PackageVersion $Minimum $Packages[$Name]) -gt 0) {
            $Raised += "  $Name ${Minimum}: the minimum $EditorVersion names (the plugins support $($Packages[$Name]))"
            $Packages[$Name] = $Minimum
        }
    }
}

# Assets/ must exist — Unity's project validation requires it, even empty.
New-Item -ItemType Directory -Force -Path (Join-Path $HostDir "Assets") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $HostDir "Packages") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $HostDir "ProjectSettings") | Out-Null

$PackagePath = $PackageDir -replace '\\', '/'
$OptionalPackages = ""
foreach ($Package in $Packages.GetEnumerator()) {
    $OptionalPackages += ",`n    `"$($Package.Key)`": `"$($Package.Value)`""
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

# Build Forge's settings and per-user state go every run, so a run starts from the defaults whatever
# an earlier one, or a test that didn't put a saved setting back, left in the host.
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $HostDir "ProjectSettings\ForgeSettings.asset")
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $HostDir "UserSettings\Forge*")

# Rewritten every run so a stale pin from an earlier run cannot stick.
$ProjectVersionFile = Join-Path $HostDir "ProjectSettings\ProjectVersion.txt"
"m_EditorVersion: $EditorVersion" | Set-Content $ProjectVersionFile

Write-Host "Test host: $HostDir (editor $EditorVersion$(if ($PackageSet) { ", $PackageSet packages" }))"
$Raised | ForEach-Object { Write-Host $_ }
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
    foreach ($Package in $Packages.GetEnumerator()) {
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
