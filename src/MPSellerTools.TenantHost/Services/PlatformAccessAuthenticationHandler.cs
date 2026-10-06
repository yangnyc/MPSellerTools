using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Hosting;

namespace MPSellerTools.TenantHost.Services;

public static class PlatformAccess
{
    public const string Scheme = "PlatformAccess";

    /// <summary>Managing this tenant's users: its own TenantAdmins, or the platform console.</summary>
    public const string UserManagementPolicy = "UserManagement";
}

/// <summary>This tenant's platform access key, read once at startup.</summary>
public record PlatformAccessKeyHolder(string Key);

/// <summary>
/// Authenticates a request from the platform console by this tenant's
/// platform access key. The caller becomes a <see cref="Roles.PlatformOperator"/>,
/// which only the user management endpoints accept — not a TenantAdmin, so
/// the key opens no business data.
/// </summary>
public class PlatformAccessAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    PlatformAccessKeyHolder key) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(PlatformAccessKey.HeaderName, out var presented))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented.ToString()), Encoding.UTF8.GetBytes(key.Key));
        if (!matches)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid platform access key."));
        }

        var actor = Request.Headers[PlatformAccessKey.ActorHeaderName].ToString().Trim();
        var name = actor.Length is > 0 and <= 200 ? $"platform:{actor}" : "platform";
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.Role, Roles.PlatformOperator)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
