using System.Net;
using System.Net.Http.Json;
using MPSellerTools.Core.Platform;
using MPSellerTools.Infrastructure.Hosting;

namespace MPSellerTools.PlatformHost.Services;

/// <summary>Why a company's instance could not be asked about its users; safe to show.</summary>
public class TenantUnavailableException(string reason) : Exception(reason);

/// <summary>
/// Calls a company's own instance to read or manage its users, presenting
/// that company's platform access key. The platform holds no tenant
/// connection string and keeps none of what comes back (brief §4).
/// </summary>
public class TenantUsersClient(IHttpClientFactory httpClientFactory, string localDataDirectory, bool viaPublicUrl)
{
    public const string HttpClientName = "TenantHosts";

    public async Task<HttpResponseMessage> SendAsync(
        Tenant tenant, HttpMethod method, string path, object? body, string actorEmail, CancellationToken cancellationToken)
    {
        if (tenant.Status != TenantStatus.Active || tenant.Url is null)
        {
            throw new TenantUnavailableException(tenant.Status == TenantStatus.Active ? "Not running" : tenant.Status.ToString());
        }

        // The slug becomes part of a file path, so check its characters again
        // here rather than trusting the stored value.
        var key = SlugValidator.IsValid(tenant.Slug)
            ? PlatformAccessKey.TryRead(PlatformAccessKey.PathFor(localDataDirectory, tenant.Slug))
            : null;
        if (key is null)
        {
            throw new TenantUnavailableException("Needs a restart to accept platform access");
        }

        // tenant.Url may name a public host. Behind a proxy its certificate is
        // trusted, so use it; otherwise only localhost matches the HTTPS
        // development certificate the instance presents.
        var baseUrl = viaPublicUrl ? tenant.Url : $"https://localhost:{tenant.Port}";
        using var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
        request.Headers.Add(PlatformAccessKey.HeaderName, key);
        request.Headers.Add(PlatformAccessKey.ActorHeaderName, actorEmail);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new TenantUnavailableException("Not responding");
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new TenantUnavailableException("Needs a restart to accept platform access");
        }

        return response;
    }
}
