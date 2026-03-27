[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "1.0.0",

    [Parameter(Mandatory = $false)]
    [string]$RuntimeIdentifier = "win-x64",

    [Parameter(Mandatory = $false)]
    [switch]$SelfContained
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$publishDir = Join-Path $repoRoot "publish\$RuntimeIdentifier"
$updaterPublishDir = Join-Path $repoRoot "publish\updater\$RuntimeIdentifier"
$installerProject = Join-Path $repoRoot "installer\AlfaNet.WireGuardWatchdog.Setup.wixproj"
$serviceProject = Join-Path $repoRoot "AlfaNet.WireGuardWatchdog.csproj"
$updaterProject = Join-Path $repoRoot "updater\AlfaNet.WireGuardWatchdog.Updater.csproj"

$publishArguments = @(
    "publish",
    $serviceProject,
    "-c", "Release",
    "-r", $RuntimeIdentifier,
    "--output", $publishDir
)

if ($SelfContained) {
    $publishArguments += "--self-contained"
    $publishArguments += "true"
}
else {
    $publishArguments += "--self-contained"
    $publishArguments += "false"
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "Fallo dotnet publish."
}

& dotnet publish $updaterProject -c Release -r $RuntimeIdentifier --self-contained false -p:PublishSingleFile=true --output $updaterPublishDir
if ($LASTEXITCODE -ne 0) {
    throw "Fallo dotnet publish del updater."
}

& dotnet build $installerProject "-p:ProductVersion=$Version" "-p:PublishDir=$publishDir" "-p:UpdaterPublishDir=$updaterPublishDir" -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Fallo la compilacion del MSI."
}

Write-Host "MSI generado."
Write-Host "Proyecto instalador: $installerProject"
Write-Host "Payload publicado: $publishDir"
Write-Host "Updater publicado: $updaterPublishDir"
