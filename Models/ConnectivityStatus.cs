namespace AlfaNet.WireGuardWatchdog.Models;

public sealed class ConnectivityStatus
{
    public bool HasInternet { get; init; }

    public bool HasVpnConnectivity { get; init; }

    public string Detail { get; init; } = string.Empty;

    public DateTimeOffset CheckedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
