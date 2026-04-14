namespace AlfaNet.WireGuardWatchdog.Utils;

public static class WireGuardPaths
{
    public static readonly string WgExePath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WireGuard", "wg.exe");

    public static string GetTunnelServiceName(string tunnelName)
    {
        return $"WireGuardTunnel${tunnelName}";
    }
}
