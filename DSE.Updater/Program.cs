using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace DSE.Updater;

internal class Program
{
    // Change these later to your real GitHub details
    private const string GitHubOwner = "YOUR_GITHUB_USERNAME";
    private const string GitHubRepo = "DSE.Platform";

    private static async Task Main()
    {
        Console.WriteLine("DSE Updater starting...");
        Console.WriteLine();

        try
        {
            string currentVersion = GetCurrentVersion();
            Console.WriteLine($"Installed version: {currentVersion}");

            string? latestVersion = await GetLatestGitHubReleaseVersionAsync();

            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                Console.WriteLine("Could not determine latest release version.");
                return;
            }

            Console.WriteLine($"Latest GitHub release: {latestVersion}");

            if (IsNewerVersion(latestVersion, currentVersion))
            {
                Console.WriteLine("Update available.");
            }
            else
            {
                Console.WriteLine("You are already on the latest version.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Updater failed.");
            Console.WriteLine(ex.Message);
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    private static string GetCurrentVersion()
    {
        return Assembly.GetExecutingAssembly()
                   .GetName()
                   .Version?
                   .ToString() ?? "0.0.0.0";
    }

    private static async Task<string?> GetLatestGitHubReleaseVersionAsync()
    {
        string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

        using var httpClient = new HttpClient();

        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DSEUpdater", "1.0"));

        using HttpResponseMessage response = await httpClient.GetAsync(url);

        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("tag_name", out JsonElement tagNameElement))
            return null;

        string? tagName = tagNameElement.GetString();

        if (string.IsNullOrWhiteSpace(tagName))
            return null;

        return tagName.TrimStart('v', 'V');
    }

    private static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        if (!Version.TryParse(latestVersion, out Version? latest))
            return false;

        if (!Version.TryParse(currentVersion, out Version? current))
            return false;

        return latest > current;
    }
}