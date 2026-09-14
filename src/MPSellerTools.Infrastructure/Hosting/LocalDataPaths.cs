namespace MPSellerTools.Infrastructure.Hosting;

/// <summary>
/// Resolves the shared `.local/` directory (runtime config, keys, logs,
/// outbox — brief §11, always outside source control). Hosts always prefer
/// an explicit, already-absolute `Hosting:LocalDataDirectory` value — the
/// provisioning worker (Increment 4) writes one into every instance config
/// it generates. The walk-up-to-the-.sln fallback exists only so a bare
/// `dotnet run` with no instance config still works out of the box; it does
/// not depend on a hardcoded number of ".." segments, which would silently
/// break whenever the build configuration or target framework moniker
/// changes the output directory's depth (reproduced during Increment 3).
/// </summary>
public static class LocalDataPaths
{
    public static string Resolve(string? configuredPath, string contentRootPath)
    {
        if (!string.IsNullOrEmpty(configuredPath))
        {
            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
        }

        var solutionRoot = FindSolutionRoot(contentRootPath)
            ?? throw new InvalidOperationException(
                "Could not resolve a local data directory: no 'Hosting:LocalDataDirectory' was " +
                "configured and no MPSellerTools.sln was found above the content root to fall back to.");

        return Path.Combine(solutionRoot, ".local");
    }

    private static string? FindSolutionRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (directory.GetFiles("MPSellerTools.sln").Length > 0)
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return null;
    }
}
