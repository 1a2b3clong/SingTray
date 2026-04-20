param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$SelfContained,

    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$ReleaseRoot = ""
)

$ErrorActionPreference = "Stop"

function ConvertTo-Bool([string]$Value)
{
    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "Boolean value is required."
    }

    switch ($Value.Trim().ToLowerInvariant())
    {
        "true" { return $true }
        "false" { return $false }
        "1" { return $true }
        "0" { return $false }
        default { throw "Invalid boolean value: $Value" }
    }
}

$installerRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $installerRoot
$releaseRootPath = if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) {
    Join-Path $repoRoot "release"
} elseif ([System.IO.Path]::IsPathRooted($ReleaseRoot)) {
    $ReleaseRoot
} else {
    Join-Path $repoRoot $ReleaseRoot
}
$selfContainedFlag = ConvertTo-Bool $SelfContained
$variantName = if ($selfContainedFlag) { "self-contained" } else { "framework-dependent" }
$artifactsRoot = Join-Path $installerRoot "artifacts-$variantName"
$stagingRoot = Join-Path $installerRoot "staging-$variantName"
$outputRoot = Join-Path $releaseRootPath $variantName
$outputBaseFileName = "SingTray-$Version-$Runtime-$variantName-setup"

New-Item -ItemType Directory -Force -Path $releaseRootPath | Out-Null
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

& (Join-Path $installerRoot "publish.ps1") `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -SelfContained $SelfContained `
    -ArtifactsRoot $artifactsRoot `
    -StagingRoot $stagingRoot

$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    throw "Inno Setup compiler not found: $iscc"
}

& $iscc `
    "/DMyAppVersion=$Version" `
    "/DMyOutputDir=$outputRoot" `
    "/DMyOutputBaseFilename=$outputBaseFileName" `
    (Join-Path $installerRoot "setup.iss")

$setupFile = Join-Path $outputRoot "$outputBaseFileName.exe"
if (-not (Test-Path $setupFile)) {
    throw "Expected installer not found: $setupFile"
}

Write-Host "Built release asset: $setupFile"
