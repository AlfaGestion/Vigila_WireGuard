using AlfaNet.WireGuardWatchdog.Interfaces;
using AlfaNet.WireGuardWatchdog.Models;
using Microsoft.Extensions.Options;

namespace AlfaNet.WireGuardWatchdog;

public sealed class Worker : BackgroundService
{
    private readonly IConnectivityProbe _connectivityProbe;
    private readonly IRecoveryPolicy _recoveryPolicy;
    private readonly IWireGuardController _wireGuardController;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<Worker> _logger;
    private readonly IOptionsMonitor<WatchdogOptions> _optionsMonitor;

    private bool? _previousInternetAvailable;
    private int _vpnFailureCount;
    private DateTimeOffset? _lastRestartUtc;

    public Worker(
        IConnectivityProbe connectivityProbe,
        IRecoveryPolicy recoveryPolicy,
        IWireGuardController wireGuardController,
        IAuditLogger auditLogger,
        IOptionsMonitor<WatchdogOptions> optionsMonitor,
        ILogger<Worker> logger)
    {
        _connectivityProbe = connectivityProbe;
        _recoveryPolicy = recoveryPolicy;
        _wireGuardController = wireGuardController;
        _auditLogger = auditLogger;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WireGuard watchdog iniciado.");
        _auditLogger.WriteInfo("Servicio watchdog iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue;

            try
            {
                await MonitorTunnelAsync(options, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error no controlado durante el ciclo de monitoreo.");
                _auditLogger.WriteError("Error no controlado durante el ciclo de monitoreo.", ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.CheckIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("WireGuard watchdog detenido.");
        _auditLogger.WriteInfo("Servicio watchdog detenido.");
    }

    private async Task MonitorTunnelAsync(WatchdogOptions options, CancellationToken cancellationToken)
    {
        var status = await _connectivityProbe.GetStatusAsync(cancellationToken);
        var tunnelActive = await _wireGuardController.IsTunnelActiveAsync(options.TunnelName, cancellationToken);

        LogStatus(status, tunnelActive);

        if (HasInternetBeenRestored(status))
        {
            await HandleInternetRestoreAsync(options, cancellationToken);
        }
        else
        {
            await HandleVpnConnectivityAsync(options, status, cancellationToken);
        }

        _previousInternetAvailable = status.HasInternet;
    }

    private void LogStatus(ConnectivityStatus status, bool tunnelActive)
    {
        _logger.LogInformation(
            "Estado actual. Internet: {Internet}; VPN: {Vpn}; Túnel activo: {TunnelActive}; Detalle: {Detail}",
            status.HasInternet,
            status.HasVpnConnectivity,
            tunnelActive,
            status.Detail);
    }

    private bool HasInternetBeenRestored(ConnectivityStatus status)
    {
        return _previousInternetAvailable.HasValue
            && !_previousInternetAvailable.Value
            && status.HasInternet;
    }

    private async Task HandleInternetRestoreAsync(WatchdogOptions options, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Se detectó restauración de Internet. Se esperarán {DelaySeconds} segundos antes de reiniciar el túnel.",
            options.InternetRestoreDelaySeconds);

        _auditLogger.WriteWarning("Internet volvió después de una caída. Se programará reinicio del túnel.");

        await Task.Delay(TimeSpan.FromSeconds(options.InternetRestoreDelaySeconds), cancellationToken);
        await AttemptRestartAsync(options, "Conectividad de Internet restaurada.", cancellationToken);
    }

    private async Task HandleVpnConnectivityAsync(
        WatchdogOptions options,
        ConnectivityStatus status,
        CancellationToken cancellationToken)
    {
        if (!status.HasInternet)
        {
            _vpnFailureCount = 0;
            return;
        }

        if (status.HasVpnConnectivity)
        {
            _vpnFailureCount = 0;
            return;
        }

        _vpnFailureCount++;

        _logger.LogWarning(
            "La VPN no responde hacia {VpnHost}. Fallo consecutivo {FailureCount}/{MaxFailures}.",
            options.VpnHealthHost,
            _vpnFailureCount,
            options.PingFailuresBeforeRestart);

        if (_vpnFailureCount < options.PingFailuresBeforeRestart)
        {
            return;
        }

        await AttemptRestartAsync(
            options,
            $"Se alcanzó el umbral de {_vpnFailureCount} fallos consecutivos de conectividad VPN.",
            cancellationToken);

        _vpnFailureCount = 0;
    }

    private async Task AttemptRestartAsync(
        WatchdogOptions options,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!_recoveryPolicy.CanRestart(_lastRestartUtc, DateTimeOffset.UtcNow, options.RestartCooldownSeconds))
        {
            _logger.LogInformation(
                "Reinicio omitido por cooldown. Cooldown configurado: {CooldownSeconds} segundos.",
                options.RestartCooldownSeconds);
            return;
        }

        _logger.LogWarning("Se reiniciará el túnel {TunnelName}. Motivo: {Reason}", options.TunnelName, reason);
        _auditLogger.WriteWarning($"Se reiniciará el túnel {options.TunnelName}. Motivo: {reason}");

        var result = await _wireGuardController.RestartTunnelAsync(options.TunnelName, cancellationToken);
        _lastRestartUtc = DateTimeOffset.UtcNow;

        if (result.Success)
        {
            _logger.LogInformation("Reinicio del túnel completado correctamente.");
            _auditLogger.WriteInfo("Reinicio del túnel WireGuard completado correctamente.");
            return;
        }

        _logger.LogError("El reinicio del túnel falló. Detalle: {Detail}", result.Message);
        _auditLogger.WriteError($"El reinicio del túnel falló. Detalle: {result.Message}");
    }
}
