param(
    [string]$Version = "local",

    [ValidateSet("framework", "self-contained")]
    [string]$Mode = "framework",

    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Assert-PathExists([string]$Path, [string]$Message)
{
    if (-not (Test-Path -LiteralPath $Path)) {
        throw $Message
    }
}

function Assert-DirectoryHasFiles([string]$Path, [string]$Message)
{
    Assert-PathExists $Path $Message
    $hasFiles = Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction Stop | Select-Object -First 1
    if (-not $hasFiles) {
        throw $Message
    }
}

function Publish-Project([string]$ProjectPath, [string]$OutputPath, [bool]$SelfContained, [string]$Configuration, [string]$Runtime)
{
    New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

    dotnet publish $ProjectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained $SelfContained `
        -o $OutputPath
}

function Copy-DirectoryContent([string]$SourceRoot, [string]$DestinationRoot)
{
    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null
    Copy-Item (Join-Path $SourceRoot "*") $DestinationRoot -Recurse -Force
}

$installerRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $installerRoot
$artifactsRoot = Join-Path $installerRoot "artifacts"
$stagingRoot = Join-Path $installerRoot "staging"
$outputRoot = Join-Path $installerRoot "output"

$modeArtifactsRoot = Join-Path $artifactsRoot $Mode
$modeStagingRoot = Join-Path $stagingRoot $Mode
$modeOutputRoot = Join-Path $outputRoot $Mode

$clientArtifacts = Join-Path $modeArtifactsRoot "client"
$serviceArtifacts = Join-Path $modeArtifactsRoot "service"
$clientStaging = Join-Path $modeStagingRoot "client"
$serviceStaging = Join-Path $modeStagingRoot "service"
$outputBaseFileName = "SingTray-$Version-$Runtime-$Mode-setup"

$selfContained = $Mode -eq "self-contained"

Remove-Item -LiteralPath $modeArtifactsRoot -Recurse -Force -ErrorAction SilentlyContinue
Publish-Project (Join-Path $repoRoot "SingTray.Client\SingTray.Client.csproj") $clientArtifacts $selfContained $Configuration $Runtime
Publish-Project (Join-Path $repoRoot "SingTray.Service\SingTray.Service.csproj") $serviceArtifacts $selfContained $Configuration $Runtime
Assert-DirectoryHasFiles $clientArtifacts "Artifacts for mode '$Mode' are missing or empty: $clientArtifacts"
Assert-DirectoryHasFiles $serviceArtifacts "Artifacts for mode '$Mode' are missing or empty: $serviceArtifacts"

Remove-Item -LiteralPath $modeStagingRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $clientStaging | Out-Null
New-Item -ItemType Directory -Force -Path $serviceStaging | Out-Null

Copy-DirectoryContent $clientArtifacts $clientStaging
Copy-DirectoryContent $serviceArtifacts $serviceStaging

Assert-DirectoryHasFiles $clientStaging "Client staging directory for mode '$Mode' is empty: $clientStaging"
Assert-DirectoryHasFiles $serviceStaging "Service staging directory for mode '$Mode' is empty: $serviceStaging"
Assert-DirectoryHasFiles $modeStagingRoot "Staging directory for mode '$Mode' is empty: $modeStagingRoot"

New-Item -ItemType Directory -Force -Path $modeOutputRoot | Out-Null

$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
Assert-PathExists $iscc "Inno Setup compiler not found: $iscc"

& $iscc `
    "/DMyAppVersion=$Version" `
    "/DMyOutputDir=$modeOutputRoot" `
    "/DMyOutputBaseFilename=$outputBaseFileName" `
    "/DMyStagingDir=$modeStagingRoot" `
    (Join-Path $installerRoot "setup.iss")

$setupFile = Join-Path $modeOutputRoot "$outputBaseFileName.exe"
Assert-PathExists $setupFile "Expected installer not found: $setupFile"

Write-Host "Built release asset: $setupFile"
