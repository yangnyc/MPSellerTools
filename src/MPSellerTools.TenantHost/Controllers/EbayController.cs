using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

/// <summary>
/// The company's link to its eBay seller account: the application keys, the
/// seller's consent (OAuth authorization code grant), and importing orders
/// and products. TenantAdmin only.
/// </summary>
[ApiController]
[Route("api/ebay")]
[Authorize(Policy = Roles.TenantAdmin)]
public class EbayController(TenantDbContext db, EbayClient ebay, EbaySync sync, AuditLogger audit) : ControllerBase
{
    /// <summary>The workspace page eBay sends the seller back to; set as the RuName's accepted URL.</summary>
    private const string CallbackPath = "/ebay";

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(await StatusAsync(await db.EbayConnections.FirstOrDefaultAsync(cancellationToken), cancellationToken));

    /// <summary>
    /// Saves the application keys. Changing the environment, App ID or RuName
    /// ends the current connection, as the seller's consent was given to the old ones.
    /// </summary>
    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] SaveEbaySettingsRequest request, CancellationToken cancellationToken)
    {
        var clientId = request.ClientId?.Trim() ?? "";
        var ruName = request.RuName?.Trim() ?? "";
        var clientSecret = request.ClientSecret?.Trim() ?? "";
        if (clientId.Length is 0 or > 200 || ruName.Length is 0 or > 200)
        {
            return Problem("App ID and RuName are required, up to 200 characters each.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!Enum.IsDefined(request.Environment) || clientSecret.Length > 500)
        {
            return Problem("Choose Sandbox or Production, and a Cert ID of up to 500 characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        var connection = await db.EbayConnections.FirstOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            if (clientSecret.Length == 0)
            {
                return Problem("Cert ID is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            connection = new EbayConnection
            {
                Id = Guid.NewGuid(),
                Environment = request.Environment,
                ClientId = clientId,
                ClientSecretProtected = sync.Protect(clientSecret),
                RuName = ruName,
            };
            db.EbayConnections.Add(connection);
        }
        else
        {
            var keysChanged = connection.Environment != request.Environment || connection.ClientId != clientId
                || connection.RuName != ruName || clientSecret.Length > 0;
            connection.Environment = request.Environment;
            connection.ClientId = clientId;
            connection.RuName = ruName;
            if (clientSecret.Length > 0)
            {
                connection.ClientSecretProtected = sync.Protect(clientSecret);
            }

            if (keysChanged)
            {
                Disconnect(connection);
            }
        }

        connection.UpdatedAtUtc = DateTime.UtcNow;
        audit.Log("EbaySettingsSaved", $"environment={connection.Environment}; appId={connection.ClientId}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await StatusAsync(connection, cancellationToken));
    }

    /// <summary>Starts the seller's consent: returns the eBay page to send them to.</summary>
    [HttpPost("connect")]
    public async Task<IActionResult> Connect(CancellationToken cancellationToken)
    {
        var connection = await db.EbayConnections.FirstOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return Problem("Save the eBay application keys first.", statusCode: StatusCodes.Status400BadRequest);
        }

        connection.PendingState = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        connection.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        // Consent covers writing only once an eBay channel account has been allowed to write.
        var forWriting = await db.ChannelAccounts.AnyAsync(a => a.Channel == SalesChannel.Ebay && a.LiveWritesEnabled, cancellationToken);
        return Ok(new EbayConnectResponse(EbayClient.AuthorizeUrl(connection, connection.PendingState, forWriting)));
    }

    /// <summary>
    /// Finishes the consent with the address eBay sent the seller back to.
    /// The workspace page at <see cref="CallbackPath"/> posts its own address
    /// here; the seller can also paste it by hand when eBay could not reach
    /// this workspace. eBay cannot be sent straight to an API address: the
    /// session cookie is SameSite=Strict, so it is not sent on a navigation
    /// arriving from eBay.
    /// </summary>
    [HttpPost("complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteEbayConnectRequest request, CancellationToken cancellationToken)
    {
        var connection = await db.EbayConnections.FirstOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return Problem("Save the eBay application keys first.", statusCode: StatusCodes.Status400BadRequest);
        }

        var pasted = request.CodeOrUrl?.Trim() ?? "";
        string? code = pasted, state = null;
        var queryStart = pasted.IndexOf('?');
        if (queryStart >= 0)
        {
            var query = QueryHelpers.ParseQuery(pasted[queryStart..]);
            code = query.TryGetValue("code", out var codeValue) ? codeValue.ToString() : null;
            state = query.TryGetValue("state", out var stateValue) ? stateValue.ToString() : null;
        }

        if (string.IsNullOrEmpty(code))
        {
            return Problem("Paste the address eBay sent you to, or the code from it.", statusCode: StatusCodes.Status400BadRequest);
        }

        // A bare code has no state with it; one from an address must match.
        var error = await CompleteAsync(connection, code, state, requireState: state is not null, cancellationToken);
        return error is null
            ? Ok(await StatusAsync(connection, cancellationToken))
            : Problem(error, statusCode: StatusCodes.Status400BadRequest);
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> DisconnectAccount(CancellationToken cancellationToken)
    {
        var connection = await db.EbayConnections.FirstOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return NotFound();
        }

        Disconnect(connection);
        connection.UpdatedAtUtc = DateTime.UtcNow;
        audit.Log("EbayDisconnected");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(await StatusAsync(connection, cancellationToken));
    }

    [HttpPost("import/orders")]
    public Task<IActionResult> ImportOrders(CancellationToken cancellationToken) =>
        ImportAsync(async connection => Ok(await sync.ImportOrdersAsync(connection, cancellationToken)), cancellationToken);

    [HttpPost("import/products")]
    public Task<IActionResult> ImportProducts(CancellationToken cancellationToken) =>
        ImportAsync(async connection => Ok(await sync.ImportProductsAsync(connection, cancellationToken)), cancellationToken);

    private async Task<IActionResult> ImportAsync(Func<EbayConnection, Task<IActionResult>> import, CancellationToken cancellationToken)
    {
        var connection = await db.EbayConnections.FirstOrDefaultAsync(cancellationToken);
        if (connection?.RefreshTokenProtected is null)
        {
            return Problem("Connect the eBay account first.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            return await import(connection);
        }
        catch (EbayApiException ex)
        {
            // Nothing from the failed import is kept; only the reason is.
            db.ChangeTracker.Clear();
            var stored = await db.EbayConnections.FirstAsync(cancellationToken);
            stored.LastSyncError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>Exchanges the code for the seller's tokens. Returns why it failed, or null.</summary>
    private async Task<string?> CompleteAsync(
        EbayConnection connection, string code, string? state, bool requireState, CancellationToken cancellationToken)
    {
        if (connection.PendingState is null)
        {
            return "Start the connection from this page first.";
        }

        if (requireState && !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(state ?? ""), System.Text.Encoding.UTF8.GetBytes(connection.PendingState)))
        {
            return "This is not the answer to the connection started here. Start again.";
        }

        EbayTokens tokens;
        try
        {
            tokens = await ebay.ExchangeCodeAsync(connection, sync.Unprotect(connection.ClientSecretProtected), code, cancellationToken);
        }
        catch (EbayApiException ex)
        {
            return ex.Message;
        }

        if (string.IsNullOrEmpty(tokens.RefreshToken))
        {
            return "eBay did not return a refresh token.";
        }

        var now = DateTime.UtcNow;
        connection.RefreshTokenProtected = sync.Protect(tokens.RefreshToken);
        connection.RefreshTokenExpiresAtUtc = tokens.RefreshTokenExpiresInSeconds is { } seconds ? now.AddSeconds(seconds) : null;
        connection.ConnectedAtUtc = now;
        connection.PendingState = null;
        connection.LastSyncError = null;
        connection.UpdatedAtUtc = now;
        audit.Log("EbayConnected", $"environment={connection.Environment}");
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private static void Disconnect(EbayConnection connection)
    {
        connection.RefreshTokenProtected = null;
        connection.RefreshTokenExpiresAtUtc = null;
        connection.ConnectedAtUtc = null;
        connection.PendingState = null;
    }

    private async Task<EbayStatusResponse> StatusAsync(EbayConnection? connection, CancellationToken cancellationToken) =>
        new(
            Configured: connection is not null,
            Environment: connection?.Environment ?? EbayEnvironment.Sandbox,
            ClientId: connection?.ClientId,
            RuName: connection?.RuName,
            Connected: connection?.RefreshTokenProtected is not null,
            ConnectedAtUtc: connection?.ConnectedAtUtc,
            AccessExpiresAtUtc: connection?.RefreshTokenExpiresAtUtc,
            LastOrderSyncAtUtc: connection?.LastOrderSyncAtUtc,
            LastProductSyncAtUtc: connection?.LastProductSyncAtUtc,
            LastSyncError: connection?.LastSyncError,
            ImportedOrders: await db.Orders.CountAsync(o => o.EbayOrderId != null, cancellationToken),
            CallbackPath: CallbackPath);
}
