namespace MPSellerTools.Core.Business;

/// <summary>
/// Single-row settings table for this tenant. Company name and other permitted
/// settings only — never tenant identity, database, or host config (brief §8 `/settings`).
/// </summary>
public class CompanySettings
{
    public Guid Id { get; set; }

    public required string CompanyName { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[]? RowVersion { get; set; }
}
