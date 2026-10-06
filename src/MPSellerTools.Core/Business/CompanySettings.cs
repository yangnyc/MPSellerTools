namespace MPSellerTools.Core.Business;

/// <summary>
/// Single-row settings table for this tenant. Company name and other permitted
/// settings only — never tenant identity, database, or host config (brief §8 `/settings`).
/// </summary>
public class CompanySettings
{
    public Guid Id { get; set; }

    public required string CompanyName { get; set; }

    /// <summary>
    /// A variant with this many units or fewer left to sell gets a restocking
    /// task. Null switches the alerts off.
    /// </summary>
    public int? LowStockThreshold { get; set; }

    /// <summary>Who the restocking tasks are assigned to; alerts need both this and the threshold.</summary>
    public Guid? LowStockAssigneeId { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[]? RowVersion { get; set; }
}
