namespace DSE.Updater.Models;

internal class UpdaterSettings
{
    public string GitHubOwner { get; set; } = "";
    public string GitHubRepo { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string StatusUrl { get; set; } = "";
    public string AgentInstallFolder { get; set; } = "";
    public string DesktopInstallFolder { get; set; } = "";
    public string StagingFolder { get; set; } = "";
    public string BackupRootFolder { get; set; } = "";
    public string Channel { get; set; } = "stable";
    public string PlatformVersionFileName { get; set; } = "platform.version";
    public string AgentExeName { get; set; } = "DSE.Agent.exe";
}