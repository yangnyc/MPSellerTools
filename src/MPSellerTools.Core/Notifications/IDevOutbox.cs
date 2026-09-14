namespace MPSellerTools.Core.Notifications;

/// <summary>
/// Local development stand-in for sending real email (brief §1/§7 — no real
/// emails are ever sent). Every host persists messages outside wwwroot and
/// outside Git; the caller who triggered the notification (e.g. the
/// TenantAdmin who just created an invitation) additionally receives the
/// link directly in the API response, since they are the authorized
/// initiator — nothing here is exposed through a general "view all mail" endpoint.
/// </summary>
public interface IDevOutbox
{
    Task WriteAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
