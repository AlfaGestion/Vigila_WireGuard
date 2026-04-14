using AlfaNet.WireGuardWatchdog.Models;

namespace AlfaNet.WireGuardWatchdog.Interfaces;

public interface IWireGuardController
{
    Task<bool> IsTunnelActiveAsync(string tunnelName, CancellationToken cancellationToken);

    /// <summary>
    /// Devuelve los segundos transcurridos desde el último handshake WireGuard,
    /// o <see langword="null"/> si no se pudo obtener el dato (wg no disponible, sin handshake previo, etc.).
    /// </summary>
    Task<double?> GetLastHandshakeSecondsAgoAsync(string tunnelName, CancellationToken cancellationToken);

    Task<CommandResult> RestartTunnelAsync(string tunnelName, CancellationToken cancellationToken);
}
