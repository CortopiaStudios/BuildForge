<#
.SYNOPSIS
    Builds one Unity Build Profile with Build Forge from the command line.

.DESCRIPTION
    Runs the command the Build Forge window shows under CI Command: the standalone
    Unity CLI ("unity run") starts the editor version named in
    ProjectSettings/ProjectVersion.txt, makes the Unity Build Profile active before
    scripts compile (-activeBuildProfile), and runs Build Forge's entry point
    (BuildForge.CommandLine.Build), which builds, restores everything it changed and
    exits with the result.

    With -SharedWorkspace, it first runs the Shared workspace command the window shows:
    BuildForge.CommandLine.Activate switches the workspace to the profile in a run of
    its own. With -Editor, the script starts that Unity editor directly instead, for
    machines without the Unity CLI.

    Run it from the repository root. Works with Windows PowerShell 5.1 and PowerShell 7.

    Exit code: 0 when the build and its cleanup succeeded. Anything else is a failure:
    the Unity CLI reports a failed build as 6, a directly started editor as 1.

.PARAMETER BuildProfile
    The Unity Build Profile asset, relative to the Unity project folder.

.PARAMETER Variant
    A variant from Project Settings > Build Forge > Build Variants, or Default.

.PARAMETER ProjectPath
    The Unity project folder relative to the current directory: './Client' for a
    subfolder, '.' when the project is the repository root.

.PARAMETER Local
    Count the build as a local build (-forgeCI false) although it runs in batch mode,
    so CI-only plugin behavior, such as reading BUILD_NUMBER, does not apply.

.PARAMETER SharedWorkspace
    Activate the profile first, in a run of its own, for a workspace that also builds
    profiles of other platforms, such as a job that builds any profile. Unity 6000.3.23
    can fail to compile when -activeBuildProfile changes platform; see the manual's
    Command line and CI page. A workspace that only builds this platform's profiles does
    not need it.

.PARAMETER TimeoutSeconds
    Unity CLI only: stop each editor run after this many seconds. 0 means no limit.

.PARAMETER Editor
    Start this Unity editor executable directly instead of using the Unity CLI. It must
    be the project's editor version.

.PARAMETER LogFile
    The editor log. Default: this console for the Unity CLI, and
    <project>/Logs/BuildForge-<time>.log for a directly started editor. With
    -SharedWorkspace, the Activate run logs to a file of the same name ending in
    -activate.

.EXAMPLE
    ./Build-Player.ps1 -ProjectPath ./Client -BuildProfile 'Assets/Settings/Build Profiles/Quest.asset' -Variant Internal -Local

.EXAMPLE
    ./Build-Player.ps1 -ProjectPath ./Client -BuildProfile 'Assets/Settings/Build Profiles/Windows.asset' -SharedWorkspace

.EXAMPLE
    ./Build-Player.ps1 -BuildProfile 'Assets/Settings/Build Profiles/Windows.asset' -Editor 'C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Unity.exe'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BuildProfile,
    [string]$Variant = 'Default',
    [string]$ProjectPath = '.',
    [switch]$Local,
    [switch]$SharedWorkspace,
    [int]$TimeoutSeconds = 0,
    [string]$Editor,
    [string]$LogFile
)
$ErrorActionPreference = 'Stop'

$profileFile = Join-Path $ProjectPath $BuildProfile
if (-not (Test-Path -LiteralPath $profileFile)) {
    throw "Build Profile not found: $profileFile. Run the script from the repository root, or pass -ProjectPath."
}

# Build Forge's part of the command. -forgeVariant Default is the default build.
$forgeArguments = @('-executeMethod', 'BuildForge.CommandLine.Build',
    '-activeBuildProfile', $BuildProfile, '-forgeVariant', $Variant)
if ($Local) {
    $forgeArguments += @('-forgeCI', 'false')
}

# An inherited UNITY_EDITOR_VERSION would make the Unity CLI start another editor than
# the one ProjectVersion.txt names.
Remove-Item Env:UNITY_EDITOR_VERSION -ErrorAction SilentlyContinue

# Start-Process joins its arguments with spaces, so quote the ones that contain spaces.
function Join-Arguments([string[]]$Values) {
    $quoted = foreach ($value in $Values) {
        if ($value.Contains('"')) { throw "Arguments cannot contain quotes: $value" }
        if ($value -match '\s') { '"' + $value + '"' } else { $value }
    }
    return ($quoted -join ' ')
}

if ($Editor) {
    # Absolute paths with forward slashes, as Build Forge's own scripts hand them to Unity.
    $project = (Resolve-Path -LiteralPath $ProjectPath).Path.Replace('\', '/')
    if (-not $LogFile) {
        $logFolder = Join-Path $project 'Logs'
        New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
        $LogFile = Join-Path $logFolder ('BuildForge-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    }
    $LogFile = $LogFile.Replace('\', '/')
}
else {
    if (-not (Get-Command unity -ErrorAction SilentlyContinue)) {
        throw 'The Unity CLI ("unity") is not on the PATH. Install it, or pass -Editor with the path of a Unity editor.'
    }
    if (-not $LogFile) {
        $LogFile = '-'
    }
}

# Runs one editor with Build Forge's arguments and returns its exit code.
function Invoke-Unity([string[]]$ForgeArguments, [string]$Log) {
    if ($Editor) {
        $arguments = @('-batchmode', '-nographics', '-silent-crashes', '-logFile', $Log,
            '-cacheServerWaitForUploadCompletion', '-projectPath', $project) + $ForgeArguments

        Write-Host "$Editor $(Join-Arguments $arguments)"
        # The editor is a windowed application, so PowerShell would not wait for it on its
        # own. Wait for this process only: Start-Process -Wait would also wait for
        # processes the build leaves running, such as a Gradle daemon.
        $process = Start-Process -FilePath $Editor -ArgumentList (Join-Arguments $arguments) -WindowStyle Hidden -PassThru
        # Reading the handle now keeps it open, so ExitCode is still available after the
        # process has exited.
        $null = $process.Handle
        $process.WaitForExit()
        if ($null -eq $process.ExitCode) {
            throw 'Could not read the editor''s exit code.'
        }
        return $process.ExitCode
    }

    $arguments = @('run', $ProjectPath, '--non-interactive')
    if ($TimeoutSeconds -gt 0) {
        $arguments += @('--timeout', "$TimeoutSeconds")
    }
    $arguments += @('--', '-nographics', '-silent-crashes', '-logFile', $Log,
        '-cacheServerWaitForUploadCompletion') + $ForgeArguments

    Write-Host "unity $($arguments -join ' ')"
    # To the console, not into this function's return value.
    & unity @arguments | Out-Host
    return $LASTEXITCODE
}

if ($SharedWorkspace) {
    # The workspace also builds profiles of other platforms: switch it to this profile
    # in a run of its own, so the build's editor starts on it.
    $activateLog = $LogFile
    if ($LogFile -ne '-') {
        $extension = [IO.Path]::GetExtension($LogFile)
        $activateLog = $LogFile.Substring(0, $LogFile.Length - $extension.Length) + '-activate' + $extension
    }
    $status = Invoke-Unity -ForgeArguments @('-executeMethod', 'BuildForge.CommandLine.Activate',
        '-forgeBuildProfile', $BuildProfile) -Log $activateLog
    if ($status -ne 0) {
        Write-Host "Activate failed: exit code $status."
        if ($activateLog -ne '-') {
            Write-Host "Log: $activateLog"
        }
        exit $status
    }
}

$status = Invoke-Unity -ForgeArguments $forgeArguments -Log $LogFile

if ($status -ne 0) {
    Write-Host "Build failed: exit code $status."
    if ($LogFile -ne '-') {
        Write-Host "Log: $LogFile"
    }
    exit $status
}
Write-Host 'Build succeeded.'
if ($LogFile -ne '-') {
    Write-Host "Log: $LogFile"
}
