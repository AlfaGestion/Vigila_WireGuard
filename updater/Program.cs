using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlfaNet.WireGuardWatchdog.Updater;

internal static class Program
{
    private const string DefaultManifestUrl = "https://www.alfagestion.com.ar/wireguard-watchdog/latest.json";
    private const string DefaultServiceName = "AlfaNet.WireGuardWatchdog";
    private const string ServiceExecutableName = "AlfaNet.WireGuardWatchdog.exe";
    private const string ServiceAssemblyName = "AlfaNet.WireGuardWatchdog.dll";
    private const string PreservedSettingsFileName = "appsettings.json";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = UpdaterOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            Console.WriteLine($"Manifest: {options.ManifestUrl}");
            Console.WriteLine($"Servicio: {options.ServiceName}");
            Console.WriteLine($"Directorio de instalacion: {options.InstallDirectory}");

            var currentVersion = TryGetInstalledVersion(options.InstallDirectory);
            if (currentVersion is null)
            {
                Console.WriteLine("No se detecto una version instalada. Se continuara como instalacion forzada.");
            }
            else
            {
                Console.WriteLine($"Version instalada: {currentVersion}");
            }

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            var manifest = await DownloadManifestAsync(httpClient, options.ManifestUrl);
            var remoteVersion = ParseVersion(manifest.Version);
            Console.WriteLine($"Version remota: {remoteVersion}");

            if (!options.Force && currentVersion is not null && remoteVersion <= currentVersion)
            {
                Console.WriteLine("No hay una actualizacion mas nueva disponible.");
                return 0;
            }

            var workspace = PrepareWorkspace(options.WorkDirectory);
            var downloadPath = Path.Combine(workspace.DownloadsDirectory, $"update-{remoteVersion}.zip");
            var stagingPath = Path.Combine(workspace.StagingRoot, remoteVersion.ToString());
            var backupPath = Path.Combine(workspace.BackupsRoot, DateTime.UtcNow.ToString("yyyyMMddHHmmss"));

            await DownloadPackageAsync(httpClient, manifest.Package.Url, downloadPath);
            ValidateHash(downloadPath, manifest.Package.Sha256);

            RecreateDirectory(stagingPath);
            ZipFile.ExtractToDirectory(downloadPath, stagingPath, overwriteFiles: true);

            EnsureExpectedPayload(stagingPath);

            var serviceWasRunning = StopServiceIfNeeded(options.ServiceName);

            try
            {
                BackupInstallation(options.InstallDirectory, backupPath);
                CopyPayload(stagingPath, options.InstallDirectory);
                WriteStateFile(workspace.StateFilePath, remoteVersion, manifest.Package.Url);
            }
            catch
            {
                if (Directory.Exists(backupPath))
                {
                    Console.WriteLine("Se detecto un error durante la copia. Restaurando backup.");
                    RestoreBackup(backupPath, options.InstallDirectory);
                }

                throw;
            }
            finally
            {
                if (serviceWasRunning)
                {
                    StartService(options.ServiceName);
                }
            }

            Console.WriteLine($"Actualizacion aplicada correctamente a la version {remoteVersion}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error de actualizacion: {ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Uso:");
        Console.WriteLine("  AlfaNet.WireGuardWatchdog.Updater.exe [--manifest-url URL] [--service-name NOMBRE] [--install-dir RUTA] [--work-dir RUTA] [--force]");
    }

    private static Version? TryGetInstalledVersion(string installDirectory)
    {
        var assemblyPath = Path.Combine(installDirectory, ServiceAssemblyName);
        if (!File.Exists(assemblyPath))
        {
            return null;
        }

        return AssemblyName.GetAssemblyName(assemblyPath).Version;
    }

    private static async Task<UpdateManifest> DownloadManifestAsync(HttpClient httpClient, string manifestUrl)
    {
        var json = await httpClient.GetStringAsync(manifestUrl);
        var manifest = JsonSerializer.Deserialize(json, UpdateJsonContext.Default.UpdateManifest);
        return manifest ?? throw new InvalidOperationException("No se pudo deserializar latest.json.");
    }

    private static Version ParseVersion(string value)
    {
        if (!Version.TryParse(value, out var parsedVersion))
        {
            throw new InvalidOperationException($"La version '{value}' no tiene un formato valido.");
        }

        return parsedVersion;
    }

    private static async Task DownloadPackageAsync(HttpClient httpClient, string packageUrl, string destinationPath)
    {
        Console.WriteLine($"Descargando paquete desde {packageUrl}");
        await using var remoteStream = await httpClient.GetStreamAsync(packageUrl);
        await using var localStream = File.Create(destinationPath);
        await remoteStream.CopyToAsync(localStream);
    }

    private static void ValidateHash(string filePath, string expectedSha256)
    {
        using var stream = File.OpenRead(filePath);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Hash invalido para el paquete descargado. Esperado: {expectedSha256}. Actual: {actualHash}.");
        }
    }

    private static UpdateWorkspace PrepareWorkspace(string baseDirectory)
    {
        var downloadsDirectory = Path.Combine(baseDirectory, "downloads");
        var stagingRoot = Path.Combine(baseDirectory, "staging");
        var backupsRoot = Path.Combine(baseDirectory, "backups");

        Directory.CreateDirectory(baseDirectory);
        Directory.CreateDirectory(downloadsDirectory);
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(backupsRoot);

        return new UpdateWorkspace(
            downloadsDirectory,
            stagingRoot,
            backupsRoot,
            Path.Combine(baseDirectory, "last-update.json"));
    }

    private static void RecreateDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }

        Directory.CreateDirectory(directoryPath);
    }

    private static void EnsureExpectedPayload(string stagingPath)
    {
        var expectedServiceExecutable = Path.Combine(stagingPath, ServiceExecutableName);
        if (!File.Exists(expectedServiceExecutable))
        {
            throw new InvalidOperationException($"El paquete descargado no contiene '{ServiceExecutableName}'.");
        }
    }

    private static bool StopServiceIfNeeded(string serviceName)
    {
        using var service = new ServiceController(serviceName);
        service.Refresh();

        if (service.Status == ServiceControllerStatus.Stopped)
        {
            Console.WriteLine("El servicio ya estaba detenido.");
            return false;
        }

        Console.WriteLine("Deteniendo servicio para aplicar la actualizacion.");
        service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
        return true;
    }

    private static void StartService(string serviceName)
    {
        using var service = new ServiceController(serviceName);
        service.Refresh();

        if (service.Status == ServiceControllerStatus.Running)
        {
            return;
        }

        Console.WriteLine("Iniciando servicio despues de la actualizacion.");
        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
    }

    private static void BackupInstallation(string sourceDirectory, string backupDirectory)
    {
        Directory.CreateDirectory(backupDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            if (IsUpdaterArtifact(relativePath))
            {
                continue;
            }

            var backupFile = Path.Combine(backupDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
            File.Copy(file, backupFile, overwrite: true);
        }
    }

    private static void CopyPayload(string sourceDirectory, string targetDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            if (string.Equals(relativePath, PreservedSettingsFileName, StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(targetDirectory, relativePath)))
            {
                Console.WriteLine("Se conserva el appsettings.json existente.");
                continue;
            }

            var destinationFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static void RestoreBackup(string backupDirectory, string targetDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(backupDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(backupDirectory, file);
            var destinationFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static void WriteStateFile(string stateFilePath, Version version, string sourceUrl)
    {
        var state = new UpdateState(version.ToString(), DateTime.UtcNow, sourceUrl);
        var json = JsonSerializer.Serialize(state, UpdateJsonContext.Default.UpdateState);
        File.WriteAllText(stateFilePath, json);
    }

    private static bool IsUpdaterArtifact(string relativePath)
    {
        return relativePath.StartsWith("Updater\\", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("Updater/", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record UpdaterOptions(
        string ManifestUrl,
        string ServiceName,
        string InstallDirectory,
        string WorkDirectory,
        bool Force,
        bool ShowHelp)
    {
        public static UpdaterOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < args.Length; index++)
            {
                var current = args[index];
                if (!current.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Argumento no reconocido: {current}");
                }

                if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    values[current] = args[++index];
                }
                else
                {
                    flags.Add(current);
                }
            }

            var baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var defaultInstallDirectory = string.Equals(Path.GetFileName(baseDirectory), "Updater", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(baseDirectory)?.FullName ?? baseDirectory
                : baseDirectory;

            var installDirectory = values.TryGetValue("--install-dir", out var explicitInstallDirectory)
                ? explicitInstallDirectory
                : defaultInstallDirectory;

            var workDirectory = values.TryGetValue("--work-dir", out var explicitWorkDirectory)
                ? explicitWorkDirectory
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "AlfaNet",
                    "WireGuardWatchdog",
                    "Updater");

            return new UpdaterOptions(
                values.GetValueOrDefault("--manifest-url") ?? DefaultManifestUrl,
                values.GetValueOrDefault("--service-name") ?? DefaultServiceName,
                installDirectory,
                workDirectory,
                flags.Contains("--force"),
                flags.Contains("--help"));
        }
    }
}

internal sealed record UpdateWorkspace(
    string DownloadsDirectory,
    string StagingRoot,
    string BackupsRoot,
    string StateFilePath);

internal sealed record UpdateManifest(string Version, ManifestPackage Package);

internal sealed record ManifestPackage(string Type, string? Runtime, bool? SelfContained, string Url, string Sha256, long SizeBytes, string EntryExecutable);

internal sealed record UpdateState(string Version, DateTime AppliedAtUtc, string SourceUrl);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(UpdateState))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext
{
}
