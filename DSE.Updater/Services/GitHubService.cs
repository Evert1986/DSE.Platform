using System.Net.Http.Headers;
using System.Text.Json;
using DSE.Updater.Models;
using DSE.Updater.Utils;

namespace DSE.Updater.Services;

internal class GitHubService
{
    private readonly UpdaterSettings _settings;

    public GitHubService(UpdaterSettings settings)
    {
        _settings = settings;
    }

    public async Task<GitHubReleaseInfo?> GetLatestGitHubReleaseAsync()
    {
        string url = $"https://api.github.com/repos/{_settings.GitHubOwner}/{_settings.GitHubRepo}/releases/latest";

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
            Version = VersionUtils.NormalizeVersion(tagName)
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

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient();

        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DSEUpdater", "1.0"));

        return httpClient;
    }
}