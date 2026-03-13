using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ServiceProcess;

namespace DSE.Updater;

internal class Program
{
    private const string GitHubOwner = "Evert1986";
    private const string GitHubRepo = "DSE.Platform";

    private const string StagingFolder = @"C:\Users\vaneve\Desktop\DSE\updates\staging";
    private const string AgentInstallFolder = @"C:\Users\vaneve\Desktop\DSE\Agent";
    private const string BackupRootFolder = @"C:\Users\vaneve\Desktop\DSE\updates\backups";
    private const string ServiceName = "DSE Agent";
    private const string StatusUrl = "http://localhost:5070/status";

    private const string PlatformVersionFileName = "platform.version";
    private const string AgentExeName = "DSE.Agent.exe";

    private static async Task Main()
    {
        Console.WriteLine("DSE Updater starting...");
        Console.WriteLine();

        try
        {
            string installedVersion = await GetInstalledPlatformVersionAsync();
            Console.WriteLine($"Installed platform version: {installedVersion}");

            GitHubReleaseInfo? latestRelease = await GetLatestGitHubReleaseAsync();

            if (latestRelease == null)
            {
                Console.WriteLine("Could not determine latest release information.");
                return;
            }

            Console.WriteLine($"Latest GitHub release version: {latestRelease.Version}");

            if (!IsNewerVersion(latestRelease.Version, installedVersion))
            {
                Console.WriteLine("No update required.");
                return;
            }

            Console.WriteLine("Update available.");
            Console.WriteLine();

            var manifestAsset = latestRelease.Assets
                .FirstOrDefault(a => string.Equals(a.Name, "manifest.json", StringComparison.OrdinalIgnoreCase));

            if (manifestAsset == null)
            {
                Console.WriteLine("manifest.json was not found in the release assets.");
                return;
            }

            Directory.CreateDirectory(StagingFolder);

            string manifestPath = Path.Combine(StagingFolder, "manifest.json");

            Console.WriteLine("Downloading manifest.json...");
            await DownloadFileAsync(manifestAsset.DownloadUrl, manifestPath);
            Console.WriteLine($"Manifest saved to: {manifestPath}");
            Console.WriteLine();

            UpdateManifest? manifest = await LoadManifestAsync(manifestPath);

            if (manifest == null)
            {
                Console.WriteLine("Failed to parse manifest.json.");
                return;
            }

            Console.WriteLine($"Manifest product: {manifest.Product}");
            Console.WriteLine($"Manifest version: {manifest.Version}");
            Console.WriteLine($"Manifest channel: {manifest.Channel}");
            Console.WriteLine();

            if (manifest.Files.Count == 0)
            {
                Console.WriteLine("No files listed in manifest.");
                return;
            }

            Console.WriteLine("Files listed in manifest:");
            foreach (var file in manifest.Files)
            {
                Console.WriteLine($" - {file.Name}  (target: {file.Target})");
            }

            Console.WriteLine();
            Console.WriteLine($"Downloading manifest files to: {StagingFolder}");
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

                string zipPath = Path.Combine(StagingFolder, matchingAsset.Name);

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

            string stagedAgentFolder = Path.Combine(StagingFolder, "Agent");

            if (!Directory.Exists(stagedAgentFolder))
            {
                Console.WriteLine("No staged Agent folder found. Skipping install.");
                return;
            }

            Console.WriteLine("Starting Agent install...");
            Console.WriteLine();

            StopService(ServiceName);

            string backupFolder = CreateBackupFolder(latestRelease.Version);
            Console.WriteLine($"Backup folder: {backupFolder}");

            CopyDirectory(AgentInstallFolder, backupFolder, overwrite: true);
            Console.WriteLine("Current Agent install backed up.");

            CopyDirectory(stagedAgentFolder, AgentInstallFolder, overwrite: true);
            Console.WriteLine("New Agent files copied into install folder.");

            WritePlatformVersionFile(AgentInstallFolder, manifest.Version);
            Console.WriteLine($"platform.version written: {manifest.Version}");

            StartService(ServiceName);

            bool healthOk = await WaitForHealthyStatusAsync(StatusUrl, TimeSpan.FromSeconds(20));

            if (healthOk)
            {
                Console.WriteLine("Agent service health check passed.");
            }
            else
            {
                Console.WriteLine("Agent health check failed. Rolling back...");

                StopService(ServiceName);
                CopyDirectory(backupFolder, AgentInstallFolder, overwrite: true);
                StartService(ServiceName);

                bool rollbackHealthy = await WaitForHealthyStatusAsync(StatusUrl, TimeSpan.FromSeconds(20));

                if (rollbackHealthy)
                {
                    Console.WriteLine("Rollback succeeded.");
                }
                else
                {
                    Console.WriteLine("Rollback failed. Manual intervention required.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Updater failed.");
            Console.WriteLine(ex.ToString());
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    private static async Task<string> GetInstalledPlatformVersionAsync()
    {
        string? versionFromApi = await TryGetInstalledVersionFromApiAsync();
        if (!string.IsNullOrWhiteSpace(versionFromApi))
        {
            Console.WriteLine("Installed version source: Agent API");
            return NormalizeVersion(versionFromApi);
        }

        string? versionFromFile = TryGetInstalledVersionFromFile();
        if (!string.IsNullOrWhiteSpace(versionFromFile))
        {
            Console.WriteLine("Installed version source: platform.version");
            return NormalizeVersion(versionFromFile);
        }

        string? versionFromExe = TryGetInstalledVersionFromExe();
        if (!string.IsNullOrWhiteSpace(versionFromExe))
        {
            Console.WriteLine("Installed version source: Agent EXE metadata");
            return NormalizeVersion(versionFromExe);
        }

        Console.WriteLine("Installed version source: default fallback");
        return "0.0.0";
    }

    private static async Task<string?> TryGetInstalledVersionFromApiAsync()
    {
        try
        {
            using var httpClient = CreateHttpClient();
            using HttpResponseMessage response = await httpClient.GetAsync(StatusUrl);

            if (!response.IsSuccessStatusCode)
                return null;

            string json = await response.Content.ReadAsStringAsync();

            var status = JsonSerializer.Deserialize<AgentStatusDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return status?.Version;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetInstalledVersionFromFile()
    {
        try
        {
            string versionFilePath = Path.Combine(AgentInstallFolder, PlatformVersionFileName);

            if (!File.Exists(versionFilePath))
                return null;

            return File.ReadAllText(versionFilePath).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetInstalledVersionFromExe()
    {
        try
        {
            string exePath = Path.Combine(AgentInstallFolder, AgentExeName);

            if (!File.Exists(exePath))
                return null;

            var info = FileVersionInfo.GetVersionInfo(exePath);
            return info.FileVersion;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeVersion(string version)
    {
        return version.Trim().TrimStart('v', 'V');
    }

    private static async Task<GitHubReleaseInfo?> GetLatestGitHubReleaseAsync()
    {
        string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

        using var httpClient = CreateHttpClient();

        using HttpResponseMessage response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("tag_name", out JsonElement tagNameElement))
            return null;

        string? tagName = tagNameElement.GetString();

        if (string.IsNullOrWhiteSpace(tagName))
            return null;

        var releaseInfo = new GitHubReleaseInfo
        {
            Version = NormalizeVersion(tagName)
        };

        if (root.TryGetProperty("assets", out JsonElement assetsElement) &&
            assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement assetElement in assetsElement.EnumerateArray())
            {
                string? assetName = assetElement.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString()
                    : null;

                string? downloadUrl = assetElement.TryGetProperty("browser_download_url", out JsonElement urlElement)
                    ? urlElement.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(assetName) &&
                    !string.IsNullOrWhiteSpace(downloadUrl))
                {
                    releaseInfo.Assets.Add(new GitHubReleaseAsset
                    {
                        Name = assetName,
                        DownloadUrl = downloadUrl
                    });
                }
            }
        }

        return releaseInfo;
    }

    private static async Task<UpdateManifest?> LoadManifestAsync(string manifestPath)
    {
        string json = await File.ReadAllTextAsync(manifestPath);

        return JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private static async Task DownloadFileAsync(string downloadUrl, string destinationPath)
    {
        using var httpClient = CreateHttpClient();

        using HttpResponseMessage response = await httpClient.GetAsync(downloadUrl);
        response.EnsureSuccessStatusCode();

        await using Stream contentStream = await response.Content.ReadAsStreamAsync();
        await using FileStream fileStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        await contentStream.CopyToAsync(fileStream);
    }

    private static string GetExtractFolderPath(string target)
    {
        return target switch
        {
            "Agent" => Path.Combine(StagingFolder, "Agent"),
            "Desktop" => Path.Combine(StagingFolder, "Desktop"),
            _ => Path.Combine(StagingFolder, target)
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

    private static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        if (!Version.TryParse(latestVersion, out Version? latest))
            return false;

        if (!Version.TryParse(currentVersion, out Version? current))
            return false;

        return latest > current;
    }

    private static void StopService(string serviceName)
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

    private static void StartService(string serviceName)
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

    private static string CreateBackupFolder(string targetVersion)
    {
        Directory.CreateDirectory(BackupRootFolder);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backupFolder = Path.Combine(BackupRootFolder, $"Agent_Backup_Before_{targetVersion}_{timestamp}");

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

    private static void WritePlatformVersionFile(string installFolder, string version)
    {
        string versionFilePath = Path.Combine(installFolder, PlatformVersionFileName);
        File.WriteAllText(versionFilePath, NormalizeVersion(version));
    }

    private static async Task<bool> WaitForHealthyStatusAsync(string statusUrl, TimeSpan timeout)
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

internal class GitHubReleaseInfo
{
    public string Version { get; set; } = "";
    public List<GitHubReleaseAsset> Assets { get; set; } = new();
}

internal class GitHubReleaseAsset
{
    public string Name { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
}

internal class UpdateManifest
{
    [JsonPropertyName("product")]
    public string Product { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "";

    [JsonPropertyName("files")]
    public List<UpdateManifestFile> Files { get; set; } = new();
}

internal class UpdateManifestFile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";
}

internal class AgentStatusDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}