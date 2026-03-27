namespace AlfaNet.WireGuardWatchdog.Models;

public sealed class CommandResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public int? ExitCode { get; init; }

    public static CommandResult Ok(string message) => new()
    {
        Success = true,
        Message = message
    };

    public static CommandResult Fail(string message, int? exitCode = null) => new()
    {
        Success = false,
        Message = message,
        ExitCode = exitCode
    };
}
