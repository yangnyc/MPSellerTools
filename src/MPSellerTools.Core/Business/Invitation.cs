namespace MPSellerTools.Core.Business;

/// <summary>
/// A single-use, time-limited invitation to set a password and join this tenant
/// (brief §7). Only a hash of the token is stored — the raw token exists only in
/// the link delivered via the dev outbox.
/// </summary>
public class Invitation
{
    public Guid Id { get; set; }

    public required string Email { get; set; }

    /// <summary>One of <see cref="Tenancy.Roles"/> — TenantAdmin or Employee.</summary>
    public required string Role { get; set; }

    public required string TokenHash { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? AcceptedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
