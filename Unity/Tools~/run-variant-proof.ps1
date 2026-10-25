param(
    [string]$Editor = 'C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Unity.exe',
    [string]$ProofProject,
    [string]$UnityCli = 'unity',
    # The proof profile gets its own Player Settings, which the builds then restore in the profile.
    [switch]$ProfilePlayerSettings
)
$ErrorActionPreference = 'Stop'
# This runner launches a Windows standalone player; it does not claim macOS/Linux proof.
if ($env:OS -ne 'Windows_NT') { throw 'The variant player proof runner requires Windows.' }
$editorVersion = [regex]::Match((Get-Item -LiteralPath $Editor).VersionInfo.ProductVersion,
    '6000\.\d+\.\d+[abf]\d+').Value
if (-not $editorVersion) { throw "Cannot read the Unity version from $Editor" }
if (-not $ProofProject) {
    $ProofProject = Join-Path $env:LOCALAPPDATA "BuildForge/variant-proof-$editorVersion$(if ($ProfilePlayerSettings) { '-profile-player-settings' })"
}
Write-Host "Variant proof: Unity $editorVersion on Windows$(if ($ProfilePlayerSettings) { ', the profile with its own Player Settings' })"
$packagePath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path.Replace('\', '/')
$proofPath = [IO.Path]::GetFullPath($ProofProject).Replace('\', '/')
if ($proofPath.Contains('"')) { throw 'The proof path cannot contain quotes.' }
foreach ($folder in @('Assets/Editor', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $proofPath $folder) | Out-Null
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VariantProof/VariantProofRuntime.cs') -Destination "$proofPath/Assets"
Get-ChildItem (Join-Path $PSScriptRoot 'VariantProof/Editor') -Filter '*.cs' | Copy-Item -Destination "$proofPath/Assets/Editor"
@{ dependencies = @{
    'com.cortopiastudios.buildforge' = "file:$packagePath"
    'com.unity.test-framework' = '1.6.0'
    'com.unity.xr.openxr' = '1.18.0'
    'com.unity.addressables' = '2.9.1'
}; testables = @('com.cortopiastudios.buildforge') } | ConvertTo-Json -Depth 4 | Set-Content "$proofPath/Packages/manifest.json"
"m_EditorVersion: $editorVersion" | Set-Content "$proofPath/ProjectSettings/ProjectVersion.txt"
function Invoke-ProofEditor([string]$Arguments, [string]$LogName) {
    $process = Start-Process -FilePath $UnityCli -WindowStyle Hidden -PassThru -ArgumentList (
        "run `"$proofPath`" --editor-path `"$Editor`" --non-interactive -- -nographics -cacheServerWaitForUploadCompletion -logFile `"$proofPath/$LogName.log`" $Arguments")
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Unity failed ($($process.ExitCode)); see $proofPath/$LogName.log" }
}
Invoke-ProofEditor "-executeMethod VariantProofSetup.Prepare$(if ($ProfilePlayerSettings) { ' -proofProfilePlayerSettings' })" 'setup'
$savedProject = (Get-FileHash "$proofPath/ProjectSettings/ProjectSettings.asset").Hash
$savedProfile = $null
foreach ($variant in @('Development', 'Default')) {
    Invoke-ProofEditor "-activeBuildProfile Assets/Proof.asset -forgeVariant $variant -executeMethod BuildForge.CommandLine.Build" "$variant-build"
    $player = Start-Process -FilePath "$proofPath/Builds/$variant/Proof.exe" -WindowStyle Hidden -PassThru -Wait -ArgumentList (
        "-batchmode -nographics --proofOutput `"$proofPath/$variant-proof.json`" -logFile `"$proofPath/$variant-player.log`"")
    if ($player.ExitCode -ne 0) { throw "$variant proof player failed" }
    $result = Get-Content "$proofPath/$variant-proof.json" -Raw | ConvertFrom-Json
    $development = $variant -eq 'Development'
    if ($result.developmentBuild -ne $development -or $result.developmentDefine -ne $development -or
        $result.unityDevelopmentDefine -ne $development -or $result.manifestDevelopment -ne $development -or
        $result.regionChina -or $result.variant -ne $variant) { throw "$variant runtime flags do not match" }
    $expectedName = if ($development) { 'Variant Proof (Development)' } else { 'Variant Proof' }
    if ($result.productName -ne $expectedName) { throw "$variant product marking does not match" }
    if ($savedProject -ne (Get-FileHash "$proofPath/ProjectSettings/ProjectSettings.asset").Hash) { throw 'Player Settings were not restored' }
    $profileHash = (Get-FileHash "$proofPath/Assets/Proof.asset").Hash
    # First activation materializes Unity's m_HasScriptingDefines bookkeeping.
    if ($savedProfile -and $savedProfile -ne $profileHash) { throw 'Native profile changed across Development -> Default' }
    $savedProfile = $profileHash
    $result | ConvertTo-Json
}
Invoke-ProofEditor '-activeBuildProfile Assets/Proof.asset -executeMethod VariantFailureProof.Run' 'failure-proof'
Get-Content "$proofPath/failure-proof.json"
# Activate of the applied profile while its Apply did not finish, after Unity switched to another
# profile on its own, in runs of their own as in CI.
Invoke-ProofEditor '-activeBuildProfile Assets/Proof.asset -proofUnfinishedApply -executeMethod VariantFailureProof.LeaveApplyUnfinished' 'unfinished-apply'
Invoke-ProofEditor '-activeBuildProfile Assets/Other.asset -executeMethod VariantFailureProof.CheckSwitchedAway' 'switched-away'
Invoke-ProofEditor '-executeMethod BuildForge.CommandLine.Activate -forgeBuildProfile Assets/Proof.asset' 'activate-unfinished'
$activateLog = Get-Content "$proofPath/activate-unfinished.log" -Raw
if (-not ($activateLog.Contains("which is still applied") -and $activateLog.Contains('nothing was reverted, but its Apply or a build did not finish'))) {
    throw 'Activate of the applied profile did not say that nothing was reverted'
}
if (-not $activateLog.Contains("the Apply of 'Proof' did not finish for")) { throw 'Activate of the applied profile did not name its unfinished Apply' }
Invoke-ProofEditor '-executeMethod VariantFailureProof.CheckAfterActivate' 'after-activate'
Get-Content "$proofPath/activate-proof.json"
Write-Host "Variant player, failure and Activate proof passed. Logs and results: $proofPath"
