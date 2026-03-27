using AlfaNet.WireGuardWatchdog.Models;
using Microsoft.Extensions.Options;

namespace AlfaNet.WireGuardWatchdog.Services;

public sealed class WatchdogOptionsValidator : IValidateOptions<WatchdogOptions>
{
    public ValidateOptionsResult Validate(string? name, WatchdogOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.TunnelName))
        {
            errors.Add("Watchdog:TunnelName es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(options.VpnHealthHost))
        {
            errors.Add("Watchdog:VpnHealthHost es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(options.LogDirectory))
        {
            errors.Add("Watchdog:LogDirectory es obligatorio.");
        }

        if (options.PingFailuresBeforeRestart <= 0)
        {
            errors.Add("Watchdog:PingFailuresBeforeRestart debe ser mayor que cero.");
        }

        if (options.CheckIntervalSeconds <= 0)
        {
            errors.Add("Watchdog:CheckIntervalSeconds debe ser mayor que cero.");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
