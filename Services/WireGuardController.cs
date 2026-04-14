using System.Diagnostics;
using AlfaNet.WireGuardWatchdog.Interfaces;
using AlfaNet.WireGuardWatchdog.Models;
using AlfaNet.WireGuardWatchdog.Utils;

namespace AlfaNet.WireGuardWatchdog.Services;

public sealed class WireGuardController : IWireGuardController
{
    private readonly ILogger<WireGuardController> _logger;

    public WireGuardController(ILogger<WireGuardController> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsTunnelActiveAsync(string tunnelName, CancellationToken cancellationToken)
    {
        try
        {
            var serviceName = WireGuardPaths.GetTunnelServiceName(tunnelName);

            using var process = CreateProcess(
                "sc.exe",
                $"query \"{serviceName}\"");

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "No se pudo consultar el estado del túnel {TunnelName}. ExitCode: {ExitCode}. Error: {Error}",
                    tunnelName,
                    process.ExitCode,
                    error);
                return false;
            }

            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo verificar el estado del túnel {TunnelName}.", tunnelName);
            return false;
        }
    }

    public async Task<double?> GetLastHandshakeSecondsAgoAsync(string tunnelName, CancellationToken cancellationToken)
    {
        var wgExe = WireGuardPaths.WgExePath;
        if (!File.Exists(wgExe))
        {
            _logger.LogWarning("No se encontró wg.exe en {Path}. No se puede verificar el handshake.", wgExe);
            return null;
        }

        try
        {
            using var process = CreateProcess(wgExe, $"show \"{tunnelName}\" latest-handshakes");
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            // Formato de salida: "<public-key>\t<unix-timestamp>\n"
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split('\t');
                if (parts.Length >= 2 && long.TryParse(parts[1].Trim(), out var unixTs))
                {
                    if (unixTs == 0)
                    {
                        // Sin handshake previo
                        return null;
                    }

                    var handshakeUtc = DateTimeOffset.FromUnixTimeSeconds(unixTs);
                    return (DateTimeOffset.UtcNow - handshakeUtc).TotalSeconds;
                }
            }

            _logger.LogWarning(
                "No se encontraron datos de handshake para el túnel {TunnelName}. Salida: {Output}",
                tunnelName, output.Trim());
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener el último handshake del túnel {TunnelName}.", tunnelName);
            return null;
        }
    }

    public async Task<CommandResult> RestartTunnelAsync(string tunnelName, CancellationToken cancellationToken)
    {
        try
        {
            var serviceName = WireGuardPaths.GetTunnelServiceName(tunnelName);

            _logger.LogInformation("Reiniciando servicio de túnel {ServiceName}.", serviceName);

            var stopResult = await RunCommandAsync(
                "sc.exe",
                $"stop \"{serviceName}\"",
                cancellationToken);

            if (!stopResult.Success)
            {
                return stopResult;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            var startResult = await RunCommandAsync(
                "sc.exe",
                $"start \"{serviceName}\"",
                cancellationToken);

            if (startResult.Success)
            {
                return CommandResult.Ok($"Túnel {tunnelName} reiniciado correctamente.");
            }

            return startResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al reiniciar el túnel {TunnelName}.", tunnelName);
            return CommandResult.Fail(
                $"Error inesperado al reiniciar el túnel {tunnelName}: {ex.Message}");
        }
    }

    private async Task<CommandResult> RunCommandAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = CreateProcess(fileName, arguments);

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode == 0)
        {
            return CommandResult.Ok(string.IsNullOrWhiteSpace(output) ? "Comando ejecutado correctamente." : output.Trim());
        }

        return CommandResult.Fail(
            string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim(),
            process.ExitCode);
    }

    private static Process CreateProcess(string fileName, string arguments)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
    }
}
