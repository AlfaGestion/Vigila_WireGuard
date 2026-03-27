namespace AlfaNet.WireGuardWatchdog.Interfaces;

public interface IRecoveryPolicy
{
    bool CanRestart(DateTimeOffset? lastRestartUtc, DateTimeOffset nowUtc, int cooldownSeconds);
}
