using AlfaNet.WireGuardWatchdog.Models;

namespace AlfaNet.WireGuardWatchdog.Interfaces;

public interface IConnectivityProbe
{
    Task<ConnectivityStatus> GetStatusAsync(CancellationToken cancellationToken);
}
