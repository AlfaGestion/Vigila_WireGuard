using AlfaNet.WireGuardWatchdog.Interfaces;

namespace AlfaNet.WireGuardWatchdog.Services;

public sealed class AuditLogger : IAuditLogger
{
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(ILogger<AuditLogger> logger)
    {
        _logger = logger;
    }

    public void WriteInfo(string message)
    {
        _logger.LogInformation("{Message}", message);
    }

    public void WriteWarning(string message)
    {
        _logger.LogWarning("{Message}", message);
    }

    public void WriteError(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            _logger.LogError("{Message}", message);
            return;
        }

        _logger.LogError(exception, "{Message}", message);
    }
}
