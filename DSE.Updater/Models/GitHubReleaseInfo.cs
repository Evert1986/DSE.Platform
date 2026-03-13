namespace DSE.Updater.Models;

internal class GitHubReleaseInfo
{
    public string Version { get; set; } = "";
    public List<GitHubReleaseAsset> Assets { get; set; } = new();
}