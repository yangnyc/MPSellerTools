using System.Security.Cryptography;

namespace MPSellerTools.Infrastructure.Hosting;

/// <summary>
/// The secret a tenant instance accepts from the platform console for
/// managing that tenant's users. Each tenant has its own key, kept in the
/// tenant's directory under `.local/` (outside source control and wwwroot):
/// the TenantHost creates it on first start, and the PlatformHost only ever
/// reads it. The platform still holds no tenant connection string and no
/// tenant user data — it asks the tenant's own process, which answers from
/// its own database.
/// </summary>
public static class PlatformAccessKey
{
    public const string HeaderName = "X-Platform-Key";

    /// <summary>Email of the platform administrator the request is made for, recorded in the tenant's audit log.</summary>
    public const string ActorHeaderName = "X-Platform-Actor";

    public static string PathFor(string localDataDirectory, string tenantSlug) =>
        Path.Combine(localDataDirectory, "tenants", tenantSlug, "platform-access.key");

    public static string LoadOrCreate(string path)
    {
        if (TryRead(path) is { } existing)
        {
            return existing;
        }

        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, key);
        return key;
    }

    public static string? TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var key = File.ReadAllText(path).Trim();
        return key.Length == 0 ? null : key;
    }
}
