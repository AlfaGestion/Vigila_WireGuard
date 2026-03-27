using AlfaNet.WireGuardWatchdog;
using AlfaNet.WireGuardWatchdog.Interfaces;
using AlfaNet.WireGuardWatchdog.Models;
using AlfaNet.WireGuardWatchdog.Services;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AlfaNet WireGuard Watchdog";
});

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

builder.Services
    .AddOptions<WatchdogOptions>()
    .Bind(builder.Configuration.GetSection(WatchdogOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<FileLoggerProvider>();
builder.Services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<FileLoggerProvider>());
builder.Services.AddSingleton<IAuditLogger, AuditLogger>();
builder.Services.AddSingleton<IConnectivityProbe, ConnectivityProbe>();
builder.Services.AddSingleton<IWireGuardController, WireGuardController>();
builder.Services.AddSingleton<IRecoveryPolicy, RecoveryPolicy>();
builder.Services.AddHostedService<Worker>();

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

var enableEventLog = builder.Configuration.GetValue<bool>($"{WatchdogOptions.SectionName}:EnableEventLog");
if (OperatingSystem.IsWindows() && enableEventLog)
{
    builder.Logging.AddEventLog(settings =>
    {
        settings.SourceName = "AlfaNet.WireGuardWatchdog";
        settings.LogName = "Application";
    });
}

builder.Services.AddSingleton<IValidateOptions<WatchdogOptions>, WatchdogOptionsValidator>();

var host = builder.Build();
await host.RunAsync();
