[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Register", "Unregister", "RunNow", "Status")]
    [string]$Action,

    [Parameter(Mandatory = $false)]
    [string]$TaskName = "AlfaNet.WireGuardWatchdog.AutoUpdate",

    [Parameter(Mandatory = $false)]
    [string]$ManifestUrl = "https://www.alfagestion.com.ar/wireguard-watchdog/latest.json",

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 24)]
    [int]$IntervalHours = 6,

    [Parameter(Mandatory = $false)]
    [string]$InstallDirectory = "C:\Program Files\Alfa Gestion\WireGuard Watchdog",

    [Parameter(Mandatory = $false)]
    [string]$ServiceName = "AlfaNet.WireGuardWatchdog"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Administrator {
    if (-not (Test-IsAdministrator)) {
        throw "Este script debe ejecutarse como administrador."
    }
}

function Get-UpdaterExecutablePath {
    $path = Join-Path $InstallDirectory "Updater\AlfaNet.WireGuardWatchdog.Updater.exe"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "No se encontro el updater en '$path'."
    }

    return $path
}

function Register-UpdateTask {
    Assert-Administrator

    $updaterPath = Get-UpdaterExecutablePath
    $arguments = "--manifest-url `"$ManifestUrl`" --service-name `"$ServiceName`" --install-dir `"$InstallDirectory`""

    $startTime = (Get-Date).Date.AddMinutes(5)
    $action = New-ScheduledTaskAction -Execute $updaterPath -Argument $arguments
    $trigger = New-ScheduledTaskTrigger -Once -At $startTime
    $trigger.Repetition.Interval = New-TimeSpan -Hours $IntervalHours
    $trigger.Repetition.Duration = [TimeSpan]::MaxValue
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -StartWhenAvailable

    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Write-Host "Tarea '$TaskName' registrada."
}

function Unregister-UpdateTask {
    Assert-Administrator
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Tarea '$TaskName' eliminada."
}

function Invoke-UpdateTaskNow {
    Assert-Administrator
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "Tarea '$TaskName' ejecutada."
}

function Show-UpdateTaskStatus {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        Write-Host "La tarea '$TaskName' no existe."
        return
    }

    $taskInfo = Get-ScheduledTaskInfo -TaskName $TaskName
    [PSCustomObject]@{
        TaskName = $task.TaskName
        State = $task.State
        LastRunTime = $taskInfo.LastRunTime
        LastTaskResult = $taskInfo.LastTaskResult
        NextRunTime = $taskInfo.NextRunTime
    } | Format-Table -AutoSize
}

switch ($Action) {
    "Register" { Register-UpdateTask }
    "Unregister" { Unregister-UpdateTask }
    "RunNow" { Invoke-UpdateTaskNow }
    "Status" { Show-UpdateTaskStatus }
    default { throw "Accion no soportada: $Action" }
}
