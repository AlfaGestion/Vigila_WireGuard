using System.Net.NetworkInformation;
using AlfaNet.WireGuardWatchdog.Interfaces;
using AlfaNet.WireGuardWatchdog.Models;
using Microsoft.Extensions.Options;

namespace AlfaNet.WireGuardWatchdog.Services;

public sealed class ConnectivityProbe : IConnectivityProbe
{
    private static readonly string[] InternetProbeHosts = ["1.1.1.1", "8.8.8.8"];

    private readonly IOptionsMonitor<WatchdogOptions> _optionsMonitor;
    private readonly ILogger<ConnectivityProbe> _logger;

    public ConnectivityProbe(IOptionsMonitor<WatchdogOptions> optionsMonitor, ILogger<ConnectivityProbe> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task<ConnectivityStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var hasInternet = await CheckInternetAsync(options.PingTimeoutMs);
        var hasVpnConnectivity = false;
        var detail = hasInternet
            ? "Internet disponible."
            : "No se detectó salida a Internet.";

        if (hasInternet)
        {
            hasVpnConnectivity = await PingAsync(options.VpnHealthHost, options.PingTimeoutMs);
            detail = hasVpnConnectivity
                ? $"La VPN respondió correctamente en {options.VpnHealthHost}."
                : $"Hay Internet, pero la VPN no respondió en {options.VpnHealthHost}.";
        }

        return new ConnectivityStatus
        {
            HasInternet = hasInternet,
            HasVpnConnectivity = hasVpnConnectivity,
            Detail = detail,
            CheckedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task<bool> CheckInternetAsync(int timeoutMs)
    {
        foreach (var host in InternetProbeHosts)
        {
            if (await PingAsync(host, timeoutMs))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> PingAsync(string host, int timeoutMs)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            return reply.Status == IPStatus.Success;
        }
        catch (PingException ex)
        {
            _logger.LogWarning(ex, "Falló el ping hacia {Host}.", host);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado durante el ping hacia {Host}.", host);
            return false;
        }
    }
}
