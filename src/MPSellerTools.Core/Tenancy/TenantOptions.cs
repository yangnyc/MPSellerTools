namespace MPSellerTools.Core.Tenancy;

/// <summary>
/// Immutable identity of the single tenant a TenantHost process serves,
/// bound once from configuration at startup (brief §4) and never re-resolved
/// per request. The connection string lives separately in
/// ConnectionStrings:TenantDatabase, not here, so it never accidentally
/// flows to the client alongside this metadata.
/// </summary>
public class TenantOptions
{
    public const string SectionName = "Tenant";

    public required Guid TenantId { get; set; }

    public required string Slug { get; set; }

    public required string DisplayName { get; set; }
}
