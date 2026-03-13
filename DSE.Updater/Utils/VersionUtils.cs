namespace DSE.Updater.Utils;

internal static class VersionUtils
{
    public static string NormalizeVersion(string version)
    {
        return version.Trim().TrimStart('v', 'V');
    }

    public static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        if (!Version.TryParse(latestVersion, out Version? latest))
            return false;

        if (!Version.TryParse(currentVersion, out Version? current))
            return false;

        return latest > current;
    }
}