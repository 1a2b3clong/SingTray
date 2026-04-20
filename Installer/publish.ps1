param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$SelfContained = "true",
    [string]$ArtifactsRoot = "",
    [string]$StagingRoot = ""
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

$selfContainedFlag = ConvertTo-Bool $SelfContained

$root = Split-Path -Parent $PSScriptRoot
$artifacts = if ([string]::IsNullOrWhiteSpace($ArtifactsRoot)) { Join-Path $PSScriptRoot "artifacts" } else { $ArtifactsRoot }
$clientOut = Join-Path $artifacts "client"
$serviceOut = Join-Path $artifacts "service"
$staging = if ([string]::IsNullOrWhiteSpace($StagingRoot)) { Join-Path $PSScriptRoot "staging" } else { $StagingRoot }

Remove-Item $artifacts -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force -Path $clientOut | Out-Null
New-Item -ItemType Directory -Force -Path $serviceOut | Out-Null
New-Item -ItemType Directory -Force -Path $staging | Out-Null

dotnet publish (Join-Path $root "SingTray.Client\SingTray.Client.csproj") -c $Configuration -r $Runtime --self-contained $selfContainedFlag -o $clientOut
dotnet publish (Join-Path $root "SingTray.Service\SingTray.Service.csproj") -c $Configuration -r $Runtime --self-contained $selfContainedFlag -o $serviceOut

Copy-Item (Join-Path $clientOut "*") $staging -Recurse -Force
Copy-Item (Join-Path $serviceOut "*") $staging -Recurse -Force

Write-Host "Staging output prepared at $staging"
