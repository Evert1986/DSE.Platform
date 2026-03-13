using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using DSE.Updater.Services;
using DSE.Updater.Models;
using DSE.Updater.Utils;



namespace DSE.Updater;

internal class Program
{
    private static UpdaterSettings _settings = new();

    private static async Task Main()
    {
        Console.WriteLine("DSE Updater starting...");
        Console.WriteLine();

        try
        {
            LoadConfiguration();

            var versionService = new VersionService(_settings);
            string installedVersion = await versionService.GetInstalledPlatformVersionAsync();
            Console.WriteLine($"Installed platform version: {installedVersion}");

            var gitHubService = new GitHubService(_settings);
            GitHubReleaseInfo? latestRelease = await gitHubService.GetLatestGitHubReleaseAsync();

            if (latestRelease == null)
            {
                Console.WriteLine("Could not determine latest release information.");
                return;
            }

            Console.WriteLine($"Latest GitHub release version: {latestRelease.Version}");

            if (!VersionUtils.IsNewerVersion(latestRelease.Version, installedVersion))
            {
                Console.WriteLine("No update required.");
                return;
            }

            Console.WriteLine("Update available.");
            Console.WriteLine();

            var updateService = new UpdateService(_settings);
            bool updateSucceeded = await updateService.InstallAgentUpdateAsync(latestRelease);

            if (updateSucceeded)
            {
                Console.WriteLine("Update completed successfully.");
            }
            else
            {
                Console.WriteLine("Update did not complete successfully.");
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

    private static void LoadConfiguration()
    {
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        _settings = config.GetSection("UpdaterSettings").Get<UpdaterSettings>()
                    ?? throw new InvalidOperationException("UpdaterSettings section is missing or invalid.");

        Console.WriteLine("Configuration loaded.");
        Console.WriteLine($"Agent install folder: {_settings.AgentInstallFolder}");
        Console.WriteLine($"Staging folder:       {_settings.StagingFolder}");
        Console.WriteLine($"Backup folder:        {_settings.BackupRootFolder}");
        Console.WriteLine();
    }

    
    
}


