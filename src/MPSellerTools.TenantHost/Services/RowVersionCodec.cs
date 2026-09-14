namespace MPSellerTools.TenantHost.Services;

/// <summary>Encodes/decodes EF Core's byte[] RowVersion as an opaque base64 string for JSON responses.</summary>
public static class RowVersionCodec
{
    public static string Encode(byte[]? rowVersion) => Convert.ToBase64String(rowVersion ?? []);

    public static byte[] Decode(string rowVersion) => Convert.FromBase64String(rowVersion);
}
