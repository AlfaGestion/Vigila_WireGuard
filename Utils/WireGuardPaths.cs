namespace AlfaNet.WireGuardWatchdog.Utils;

public static class WireGuardPaths
{
    // TODO: Si el método final de reinicio usa wireguard.exe o wireguard /installtunnelservice,
    // centralizar aquí la ruta base y los argumentos concretos para evitar hardcodear en servicios.
    public static string GetTunnelServiceName(string tunnelName)
    {
        return $"WireGuardTunnel${tunnelName}";
    }
}
