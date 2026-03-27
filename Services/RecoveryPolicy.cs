using AlfaNet.WireGuardWatchdog.Interfaces;

namespace AlfaNet.WireGuardWatchdog.Services;

public sealed class RecoveryPolicy : IRecoveryPolicy
{
    public bool CanRestart(DateTimeOffset? lastRestartUtc, DateTimeOffset nowUtc, int cooldownSeconds)
    {
        if (cooldownSeconds <= 0)
        {
            return true;
        }

        if (!lastRestartUtc.HasValue)
        {
            return true;
        }

        return nowUtc - lastRestartUtc.Value >= TimeSpan.FromSeconds(cooldownSeconds);
    }
}
