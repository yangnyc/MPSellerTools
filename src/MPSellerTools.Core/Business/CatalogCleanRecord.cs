using System.ComponentModel.DataAnnotations;

namespace MPSellerTools.Core.Business;

public enum CatalogCleanRecordKind
{
    /// <summary>A group of look-alike products that was looked at and found not to be duplicates; it is not shown again.</summary>
    Ignored,

    /// <summary>A product archived as a duplicate of another, so the clean can be undone.</summary>
    Retired,
}

/// <summary>
/// What cleaning the catalog of duplicates has decided, kept so a group
/// once dismissed stays dismissed and the last clean can be undone.
/// </summary>
public class CatalogCleanRecord
{
    public Guid Id { get; set; }

    public CatalogCleanRecordKind Kind { get; set; }

    /// <summary>The group of products the decision was about: a hash of their ids.</summary>
    [MaxLength(64)]
    public required string GroupKey { get; set; }

    /// <summary>The clean this belongs to; every product retired together shares it.</summary>
    public Guid RunId { get; set; }

    /// <summary>For <see cref="CatalogCleanRecordKind.Retired"/>: the product archived.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>For <see cref="CatalogCleanRecordKind.Retired"/>: the product kept in its place.</summary>
    public Guid? KeptProductId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
