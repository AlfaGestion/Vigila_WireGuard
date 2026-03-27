using System.Collections.Concurrent;
using System.Text;
using AlfaNet.WireGuardWatchdog.Models;
using Microsoft.Extensions.Options;

namespace AlfaNet.WireGuardWatchdog.Services;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOptionsMonitor<WatchdogOptions> _optionsMonitor;

    public FileLoggerProvider(IOptionsMonitor<WatchdogOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _optionsMonitor));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }

    private sealed class FileLogger : ILogger
    {
        private static readonly object SyncRoot = new();

        private readonly string _categoryName;
        private readonly IOptionsMonitor<WatchdogOptions> _optionsMonitor;

        public FileLogger(string categoryName, IOptionsMonitor<WatchdogOptions> optionsMonitor)
        {
            _categoryName = categoryName;
            _optionsMonitor = optionsMonitor;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            try
            {
                var options = _optionsMonitor.CurrentValue;
                Directory.CreateDirectory(options.LogDirectory);

                var path = Path.Combine(
                    options.LogDirectory,
                    $"watchdog-{DateTime.UtcNow:yyyyMMdd}.log");

                var builder = new StringBuilder();
                builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
                builder.Append(" [");
                builder.Append(logLevel);
                builder.Append("] ");
                builder.Append(_categoryName);
                builder.Append(" - ");
                builder.Append(formatter(state, exception));

                if (exception is not null)
                {
                    builder.Append(" | ");
                    builder.Append(exception);
                }

                lock (SyncRoot)
                {
                    File.AppendAllText(path, builder.AppendLine().ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Nunca interrumpir el servicio por un fallo de logging.
            }
        }
    }
}
