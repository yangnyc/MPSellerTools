using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Notifications;
using MPSellerTools.Infrastructure.Tenants;

namespace MPSellerTools.TenantHost.Services;

/// <summary>
/// Creates an invitation and sends its single-use accept link, for whoever
/// is allowed to add a user: a TenantAdmin, or the platform console.
/// </summary>
public class InvitationIssuer(
    TenantDbContext db,
    UserManager<TenantUser> userManager,
    IHttpContextAccessor httpContextAccessor,
    IDevOutbox outbox,
    AuditLogger audit)
{
    /// <summary>Returns the invitation and the accept link (a path on this instance).</summary>
    public async Task<(Invitation Invitation, string AcceptLink)> IssueAsync(string email, string role)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var actor = httpContextAccessor.HttpContext?.User;
        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            Email = email,
            Role = role,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = actor is not null && userManager.GetUserId(actor) is { } id ? Guid.Parse(id) : Guid.Empty,
            CreatedAtUtc = DateTime.UtcNow,
        };

        db.Invitations.Add(invitation);
        audit.Log("UserInvited", $"email={email}; role={role}");
        await db.SaveChangesAsync();

        var acceptLink = $"/accept-invitation?token={Uri.EscapeDataString(rawToken)}";
        await outbox.WriteAsync(email, "You've been invited to MPSellerTools", $"Accept your invitation: {acceptLink}");
        return (invitation, acceptLink);
    }

    public static string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
}
