using System.IO.Compression;
using System.Net.Http.Headers;
using System.ServiceProcess;
using System.Text.Json;
using DSE.Updater.Models;

namespace DSE.Updater.Services;

internal class UpdateService
{
    private readonly UpdaterSettings _settings;

    public UpdateService(UpdaterSettings settings)
    {
        _settings = settings;
    }

    public async Task<bool> InstallAgentUpdateAsync(GitHubReleaseInfo latestRelease)
    {
        var manifestAsset = latestRelease.Assets
            .FirstOrDefault(a => string.Equals(a.Name, "manifest.json", StringComparison.OrdinalIgnoreCase));

        if (manifestAsset == null)
        {
            Console.WriteLine("manifest.json was not found in the release assets.");
            return false;
        }

        Directory.CreateDirectory(_settings.StagingFolder);

        string manifestPath = Path.Combine(_settings.StagingFolder, "manifest.json");

        Console.WriteLine("Downloading manifest.json...");
        await DownloadFileAsync(manifestAsset.DownloadUrl, manifestPath);
        Console.WriteLine($"Manifest saved to: {manifestPath}");
        Console.WriteLine();

        UpdateManifest? manifest = await LoadManifestAsync(manifestPath);

        if (manifest == null)
        {
            Console.WriteLine("Failed to parse manifest.json.");
            return false;
        }

        Console.WriteLine($"Manifest product: {manifest.Product}");
        Console.WriteLine($"Manifest version: {manifest.Version}");
        Console.WriteLine($"Manifest channel: {manifest.Channel}");
        Console.WriteLine();

        if (manifest.Files.Count == 0)
        {
            Console.WriteLine("No files listed in manifest.");
            return false;
        }

        Console.WriteLine("Files listed in manifest:");
        foreach (var file in manifest.Files)
        {
            Console.WriteLine($" - {file.Name}  (target: {file.Target})");
        }

        Console.WriteLine();
        Console.WriteLine($"Downloading manifest files to: {_settings.StagingFolder}");
        Console.WriteLine();

        foreach (var manifestFile in manifest.Files)
        {
            var matchingAsset = latestRelease.Assets
                .FirstOrDefault(a => string.Equals(a.Name, manifestFile.Name, StringComparison.OrdinalIgnoreCase));

            if (matchingAsset == null)
            {
                Console.WriteLine($"WARNING: Asset not found in GitHub release: {manifestFile.Name}");
                continue;
            }

            string zipPath = Path.Combine(_settings.StagingFolder, matchingAsset.Name);

            await DownloadFileAsync(matchingAsset.DownloadUrl, zipPath);

            Console.WriteLine($"Downloaded: {matchingAsset.Name}");
            Console.WriteLine($"Saved to:   {zipPath}");

            string extractFolder = GetExtractFolderPath(manifestFile.Target);
            ExtractZipToFolder(zipPath, extractFolder);

            Console.WriteLine($"Extracted to: {extractFolder}");
            Console.WriteLine();
        }

        Console.WriteLine("Download and extraction completed.");
        Console.WriteLine();

        if (!Directory.Exists(_settings.AgentInstallFolder))
        {
            Console.WriteLine($"Agent install folder not found: {_settings.AgentInstallFolder}");
            Console.WriteLine("Please verify the configured install path.");
            return false;
        }

        string stagedAgentFolder = Path.Combine(_settings.StagingFolder, "Agent");

        if (!Directory.Exists(stagedAgentFolder))
        {
            Console.WriteLine("No staged Agent folder found. Skipping install.");
            return false;
        }

        Console.WriteLine("Starting Agent install...");
        Console.WriteLine();

        StopService(_settings.ServiceName);

        string backupFolder = CreateBackupFolder(latestRelease.Version);
        Console.WriteLine($"Backup folder: {backupFolder}");

        CopyDirectory(_settings.AgentInstallFolder, backupFolder, overwrite: true);
        Console.WriteLine("Current Agent install backed up.");

        CopyDirectory(stagedAgentFolder, _settings.AgentInstallFolder, overwrite: true);
        Console.WriteLine("New Agent files copied into install folder.");

        WritePlatformVersionFile(_settings.AgentInstallFolder, manifest.Version);
        Console.WriteLine($"platform.version written: {manifest.Version}");

        StartService(_settings.ServiceName);

        bool healthOk = await WaitForHealthyStatusAsync(_settings.StatusUrl, TimeSpan.FromSeconds(20));

        if (healthOk)
        {
            Console.WriteLine("Agent service health check passed.");
            return true;
        }

        Console.WriteLine("Agent health check failed. Rolling back...");

        StopService(_settings.ServiceName);
        CopyDirectory(backupFolder, _settings.AgentInstallFolder, overwrite: true);
        StartService(_settings.ServiceName);

        bool rollbackHealthy = await WaitForHealthyStatusAsync(_settings.StatusUrl, TimeSpan.FromSeconds(20));

        if (rollbackHealthy)
        {
            Console.WriteLine("Rollback succeeded.");
        }
        else
        {
            Console.WriteLine("Rollback failed. Manual intervention required.");
        }

        return false;
    }

    private async Task<UpdateManifest?> LoadManifestAsync(string manifestPath)
    {
        string json = await File.ReadAllTextAsync(manifestPath);

        return JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private async Task DownloadFileAsync(string downloadUrl, string destinationPath)
    {
        using var httpClient = CreateHttpClient();

        using HttpResponseMessage response = await httpClient.GetAsync(downloadUrl);
        response.EnsureSuccessStatusCode();

        await using Stream contentStream = await response.Content.ReadAsStreamAsync();
        await using FileStream fileStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        await contentStream.CopyToAsync(fileStream);
    }

    private string GetExtractFolderPath(string target)
    {
        return target switch
        {
            "Agent" => Path.Combine(_settings.StagingFolder, "Agent"),
            "Desktop" => Path.Combine(_settings.StagingFolder, "Desktop"),
            _ => Path.Combine(_settings.StagingFolder, target)
        };
    }

    private static void ExtractZipToFolder(string zipPath, string extractFolder)
    {
        if (Directory.Exists(extractFolder))
        {
            Directory.Delete(extractFolder, recursive: true);
        }

        Directory.CreateDirectory(extractFolder);
        ZipFile.ExtractToDirectory(zipPath, extractFolder, overwriteFiles: true);
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DSEUpdater", "1.0"));
        return httpClient;
    }

    private void StopService(string serviceName)
    {
        Console.WriteLine($"Stopping service: {serviceName}");

        using var service = new ServiceController(serviceName);
        service.Refresh();

        if (service.Status == ServiceControllerStatus.Stopped)
        {
            Console.WriteLine("Service is already stopped.");
            return;
        }

        service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));

        Console.WriteLine("Service stopped.");
    }

    private void StartService(string serviceName)
    {
        Console.WriteLine($"Starting service: {serviceName}");

        using var service = new ServiceController(serviceName);
        service.Refresh();

        if (service.Status == ServiceControllerStatus.Running)
        {
            Console.WriteLine("Service is already running.");
            return;
        }

        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));

        Console.WriteLine("Service started.");
    }

    private string CreateBackupFolder(string targetVersion)
    {
        Directory.CreateDirectory(_settings.BackupRootFolder);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backupFolder = Path.Combine(_settings.BackupRootFolder, $"Agent_Backup_Before_{targetVersion}_{timestamp}");

        Directory.CreateDirectory(backupFolder);
        return backupFolder;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, bool overwrite)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (string directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(destinationDir, relativePath));
        }

        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDir, file);
            string destinationFile = Path.Combine(destinationDir, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite);
        }
    }

    private void WritePlatformVersionFile(string installFolder, string version)
    {
        string versionFilePath = Path.Combine(installFolder, _settings.PlatformVersionFileName);
        File.WriteAllText(versionFilePath, version.Trim().TrimStart('v', 'V'));
    }

    private async Task<bool> WaitForHealthyStatusAsync(string statusUrl, TimeSpan timeout)
    {
        using var httpClient = CreateHttpClient();
        DateTime endTime = DateTime.Now.Add(timeout);

        while (DateTime.Now < endTime)
        {
            try
            {
                using HttpResponseMessage response = await httpClient.GetAsync(statusUrl);
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch
            {
            }

            await Task.Delay(2000);
        }

        return false;
    }
}