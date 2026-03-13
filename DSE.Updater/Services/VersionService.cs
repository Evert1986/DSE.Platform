using System.Diagnostics;
using System.Text.Json;
using DSE.Updater.Models;
using DSE.Updater.Utils;

namespace DSE.Updater.Services;

internal class VersionService
{
    private readonly UpdaterSettings _settings;

    public VersionService(UpdaterSettings settings)
    {
        _settings = settings;
    }

    public async Task<string> GetInstalledPlatformVersionAsync()
    {
        string? versionFromApi = await TryGetInstalledVersionFromApiAsync();
        if (!string.IsNullOrWhiteSpace(versionFromApi))
        {
            Console.WriteLine("Installed version source: Agent API");
            return VersionUtils.NormalizeVersion(versionFromApi);
        }

        string? versionFromFile = TryGetInstalledVersionFromFile();
        if (!string.IsNullOrWhiteSpace(versionFromFile))
        {
            Console.WriteLine("Installed version source: platform.version");
            return VersionUtils.NormalizeVersion(versionFromFile);
        }

        string? versionFromExe = TryGetInstalledVersionFromExe();
        if (!string.IsNullOrWhiteSpace(versionFromExe))
        {
            Console.WriteLine("Installed version source: Agent EXE metadata");
            return VersionUtils.NormalizeVersion(versionFromExe);
        }

        Console.WriteLine("Installed version source: default fallback");
        return "0.0.0";
    }

    private async Task<string?> TryGetInstalledVersionFromApiAsync()
    {
        try
        {
            using var httpClient = CreateHttpClient();
            using HttpResponseMessage response = await httpClient.GetAsync(_settings.StatusUrl);

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

    private string? TryGetInstalledVersionFromFile()
    {
        try
        {
            string versionFilePath = Path.Combine(_settings.AgentInstallFolder, _settings.PlatformVersionFileName);

            if (!File.Exists(versionFilePath))
                return null;

            return File.ReadAllText(versionFilePath).Trim();
        }
        catch
        {
            return null;
        }
    }

    private string? TryGetInstalledVersionFromExe()
    {
        try
        {
            string exePath = Path.Combine(_settings.AgentInstallFolder, _settings.AgentExeName);

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

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DSEUpdater/1.0");
        return httpClient;
    }
}