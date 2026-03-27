[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Install", "Uninstall", "Start", "Stop", "Restart", "Status")]
    [string]$Action,

    [Parameter(Mandatory = $false)]
    [string]$ExecutablePath,

    [Parameter(Mandatory = $false)]
    [string]$ServiceName = "AlfaNet.WireGuardWatchdog",

    [Parameter(Mandatory = $false)]
    [string]$DisplayName = "AlfaNet WireGuard Watchdog"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$script:RepoRoot = Split-Path -Path $PSScriptRoot -Parent
$script:DefaultExecutablePath = Join-Path $script:RepoRoot "publish\win-x64\AlfaNet.WireGuardWatchdog.exe"
$script:DefaultSettingsPath = Join-Path $script:RepoRoot "publish\win-x64\appsettings.json"

function Test-IsAdministrator {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Administrator {
    if (-not (Test-IsAdministrator)) {
        throw "Este script debe ejecutarse en una consola de PowerShell con privilegios de administrador."
    }
}

function Get-ServiceSafe {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    return Get-Service -Name $Name -ErrorAction SilentlyContinue
}

function Resolve-ExecutablePath {
    if (-not [string]::IsNullOrWhiteSpace($ExecutablePath)) {
        return (Resolve-Path -LiteralPath $ExecutablePath).Path
    }

    if (Test-Path -LiteralPath $script:DefaultExecutablePath) {
        return (Resolve-Path -LiteralPath $script:DefaultExecutablePath).Path
    }

    throw "No se encontró el ejecutable publicado. Indicá -ExecutablePath o generá publish en '$script:DefaultExecutablePath'."
}

function Get-ConfiguredLogDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsPath
    )

    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        return $null
    }

    $settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    return $settings.Watchdog.LogDirectory
}

function Ensure-LogDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResolvedExecutablePath
    )

    $settingsPath = Join-Path (Split-Path -Path $ResolvedExecutablePath -Parent) "appsettings.json"
    $logDirectory = Get-ConfiguredLogDirectory -SettingsPath $settingsPath

    if ([string]::IsNullOrWhiteSpace($logDirectory)) {
        return
    }

    if (-not (Test-Path -LiteralPath $logDirectory)) {
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        Write-Host "Carpeta de logs creada en '$logDirectory'."
    }
}

function Wait-ServiceState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Running", "Stopped")]
        [string]$DesiredStatus,

        [Parameter(Mandatory = $false)]
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        $service = Get-ServiceSafe -Name $Name
        if ($null -eq $service) {
            if ($DesiredStatus -eq "Stopped") {
                return
            }
        }
        elseif ($service.Status.ToString() -eq $DesiredStatus) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "Timeout esperando que el servicio '$Name' alcance el estado '$DesiredStatus'."
}

function Install-Service {
    Assert-Administrator

    $resolvedExecutable = Resolve-ExecutablePath
    $existingService = Get-ServiceSafe -Name $ServiceName

    if ($null -ne $existingService) {
        throw "El servicio '$ServiceName' ya existe."
    }

    Ensure-LogDirectory -ResolvedExecutablePath $resolvedExecutable

    New-Service `
        -Name $ServiceName `
        -BinaryPathName ('"{0}"' -f $resolvedExecutable) `
        -DisplayName $DisplayName `
        -StartupType Automatic `
        -Description "Supervisa y autorecupera la conectividad de un túnel WireGuard."

    Write-Host "Servicio '$ServiceName' instalado correctamente."
}

function Uninstall-Service {
    Assert-Administrator

    $service = Get-ServiceSafe -Name $ServiceName
    if ($null -eq $service) {
        Write-Host "El servicio '$ServiceName' no existe."
        return
    }

    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $ServiceName -Force
        Wait-ServiceState -Name $ServiceName -DesiredStatus Stopped
    }

    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2

    Write-Host "Servicio '$ServiceName' desinstalado correctamente."
}

function Start-WatchdogService {
    Assert-Administrator
    Start-Service -Name $ServiceName
    Wait-ServiceState -Name $ServiceName -DesiredStatus Running
    Write-Host "Servicio '$ServiceName' iniciado."
}

function Stop-WatchdogService {
    Assert-Administrator
    Stop-Service -Name $ServiceName -Force
    Wait-ServiceState -Name $ServiceName -DesiredStatus Stopped
    Write-Host "Servicio '$ServiceName' detenido."
}

function Restart-WatchdogService {
    Assert-Administrator

    $service = Get-ServiceSafe -Name $ServiceName
    if ($null -eq $service) {
        throw "El servicio '$ServiceName' no existe."
    }

    if ($service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
        Stop-Service -Name $ServiceName -Force
        Wait-ServiceState -Name $ServiceName -DesiredStatus Stopped
    }

    Start-Service -Name $ServiceName
    Wait-ServiceState -Name $ServiceName -DesiredStatus Running
    Write-Host "Servicio '$ServiceName' reiniciado."
}

function Show-ServiceStatus {
    $service = Get-ServiceSafe -Name $ServiceName
    if ($null -eq $service) {
        Write-Host "El servicio '$ServiceName' no existe."
        return
    }

    $serviceDetails = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'" |
        Select-Object Name, DisplayName, State, StartMode, PathName

    $serviceDetails | Format-Table -AutoSize
}

switch ($Action) {
    "Install" { Install-Service }
    "Uninstall" { Uninstall-Service }
    "Start" { Start-WatchdogService }
    "Stop" { Stop-WatchdogService }
    "Restart" { Restart-WatchdogService }
    "Status" { Show-ServiceStatus }
    default { throw "Acción no soportada: $Action" }
}
