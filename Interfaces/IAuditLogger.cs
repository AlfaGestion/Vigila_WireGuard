namespace AlfaNet.WireGuardWatchdog.Interfaces;

public interface IAuditLogger
{
    void WriteInfo(string message);

    void WriteWarning(string message);

    void WriteError(string message, Exception? exception = null);
}
