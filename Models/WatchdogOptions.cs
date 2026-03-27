using System.ComponentModel.DataAnnotations;

namespace AlfaNet.WireGuardWatchdog.Models;

public sealed class WatchdogOptions
{
    public const string SectionName = "Watchdog";

    [Required]
    public string TunnelName { get; init; } = string.Empty;

    [Required]
    public string VpnHealthHost { get; init; } = "10.8.0.1";

    [Range(1, 3600)]
    public int CheckIntervalSeconds { get; init; } = 15;

    [Range(1, 100)]
    public int PingFailuresBeforeRestart { get; init; } = 3;

    [Range(0, 3600)]
    public int InternetRestoreDelaySeconds { get; init; } = 10;

    [Range(0, 86400)]
    public int RestartCooldownSeconds { get; init; } = 60;

    [Range(100, 60000)]
    public int PingTimeoutMs { get; init; } = 1500;

    [Required]
    public string LogDirectory { get; init; } = string.Empty;

    public bool EnableEventLog { get; init; } = true;
}
