using AlfaNet.WireGuardWatchdog.Models;

namespace AlfaNet.WireGuardWatchdog.Interfaces;

public interface IWireGuardController
{
    Task<bool> IsTunnelActiveAsync(string tunnelName, CancellationToken cancellationToken);

    Task<CommandResult> RestartTunnelAsync(string tunnelName, CancellationToken cancellationToken);
}
