[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$PublishPath,

    [Parameter(Mandatory = $false)]
    [string]$BaseUrl = "https://www.alfagestion.com.ar/wireguard-watchdog",

    [Parameter(Mandatory = $false)]
    [switch]$MarkAsMandatory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$resolvedPublishPath = if ([string]::IsNullOrWhiteSpace($PublishPath)) {
    Join-Path $repoRoot "publish\win-x64"
}
else {
    (Resolve-Path -LiteralPath $PublishPath).Path
}

if (-not (Test-Path -LiteralPath $resolvedPublishPath)) {
    throw "No se encontró la carpeta publish en '$resolvedPublishPath'."
}

$exeName = "AlfaNet.WireGuardWatchdog.exe"
$exePath = Join-Path $resolvedPublishPath $exeName

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "No se encontró el ejecutable '$exePath'. Ejecutá dotnet publish antes de preparar la release web."
}

$outputRoot = Join-Path $repoRoot "deploy\output"
$releaseRoot = Join-Path $outputRoot "releases\$Version"
$packageName = "AlfaNet.WireGuardWatchdog-win-x64-$Version.zip"
$packagePath = Join-Path $releaseRoot $packageName
$latestPath = Join-Path $outputRoot "latest.json"
$versionManifestPath = Join-Path $releaseRoot "release.json"

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

Compress-Archive -Path (Join-Path $resolvedPublishPath '*') -DestinationPath $packagePath -CompressionLevel Optimal

$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$packageUrl = "$BaseUrl/releases/$Version/$packageName"
$publishedAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")

$manifest = [ordered]@{
    version = $Version
    publishedAtUtc = $publishedAtUtc
    channel = "stable"
    mandatory = [bool]$MarkAsMandatory
    package = [ordered]@{
        type = "zip"
        url = $packageUrl
        sha256 = $packageHash
        sizeBytes = (Get-Item -LiteralPath $packagePath).Length
        entryExecutable = $exeName
    }
}

$manifestJson = $manifest | ConvertTo-Json -Depth 5
$manifestJson | Set-Content -LiteralPath $versionManifestPath -Encoding UTF8
$manifestJson | Set-Content -LiteralPath $latestPath -Encoding UTF8

Write-Host "Release web preparada."
Write-Host "Version: $Version"
Write-Host "Paquete: $packagePath"
Write-Host "Manifest versionado: $versionManifestPath"
Write-Host "Manifest latest: $latestPath"
Write-Host "URL esperada: $packageUrl"
