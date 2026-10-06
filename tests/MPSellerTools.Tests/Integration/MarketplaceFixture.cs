using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// Canned answers for a marketplace's API, matched by HTTP method and a
/// fragment of the address, with a record of everything asked. The most
/// recently added matching rule wins, so a test can replace an answer as its
/// scenario moves on. Nothing leaves the machine.
/// </summary>
public class ChannelRouter
{
    private readonly List<(string Method, string Fragment, Func<Uri, string, HttpResponseMessage> Respond)> _rules = [];

    /// <summary>Every request made, as "METHOD address body".</summary>
    public List<string> Requests { get; } = [];

    public void Reset()
    {
        _rules.Clear();
        Requests.Clear();
    }

    public void On(string method, string fragment, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        _rules.Add((method, fragment, (_, _) => Json(json, status)));

    public void On(string method, string fragment, Func<string, HttpResponseMessage> respond) =>
        _rules.Add((method, fragment, (_, body) => respond(body)));

    /// <summary>For answers that depend on the address asked as well as the body.</summary>
    public void On(string method, string fragment, Func<Uri, string, HttpResponseMessage> respond) => _rules.Add((method, fragment, respond));

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>The request gets no answer at all, as when a connection drops after the request went out.</summary>
    public void TimeOut(string method, string fragment) =>
        _rules.Add((method, fragment, (_, _) => throw new TaskCanceledException("Simulated timeout")));

    public int Count(string method, string fragment) =>
        Requests.Count(r => r.StartsWith(method + " ", StringComparison.Ordinal) && r.Contains(fragment, StringComparison.Ordinal));

    public HttpResponseMessage? Respond(HttpRequestMessage request, string body)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add($"{request.Method} {url} {body}".TrimEnd());
        var rule = _rules.LastOrDefault(r => r.Method == request.Method.Method && url.Contains(r.Fragment, StringComparison.Ordinal));
        return rule.Respond?.Invoke(request.RequestUri, body);
    }
}

/// <summary>Stands in for Amazon's and Walmart's APIs; unmatched requests get a 404.</summary>
public class FakeChannelApi(ChannelRouter router) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        return router.Respond(request, body)
            ?? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }

    // The fixture owns this handler for its whole life; an HttpClient must not dispose it.
    protected override void Dispose(bool disposing)
    {
    }
}

/// <summary>
/// A tenant host with the multichannel catalog fully switched on: live
/// writes allowed and stock accounted for. "Live" here still means the fake
/// channel APIs of the base fixture. The background worker stays off; tests
/// run the sync engine themselves so every step is deterministic.
/// </summary>
public class MarketplaceFixture : TenantHostFixture
{
    protected override object MarketplaceSettings =>
        new { WorkerEnabled = false, AutoBackfill = false, LiveWritesEnabled = true, InventoryAccountingEnabled = true, RequestsPerSecond = 0 };

    public async Task<HttpClient> AdminAsync(string email)
    {
        await CreateUserAsync(email, "Password123!", "TenantAdmin");
        var client = CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(client, email, "Password123!"));
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>One pass of the sync engine, as the worker would make it. Returns how many jobs ran.</summary>
    public Task<int> SyncAsync() => WithScopeAsync(services => services.GetRequiredService<SyncEngine>().RunOnceAsync("test-worker", default));

    /// <summary>Makes every waiting job due now, standing in for the passage of a retry delay.</summary>
    public Task<int> MakeJobsDueAsync() => WithDbAsync(db => db.SyncJobs
        .Where(j => j.Status == SyncJobStatus.Pending || j.Status == SyncJobStatus.AwaitingRemote)
        .ExecuteUpdateAsync(s => s.SetProperty(j => j.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1))));

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>
    /// A product ready to be listed anywhere: brand, description, category,
    /// an image and a UPC. The UPC is a made-up, test-only number.
    /// </summary>
    public async Task<(Guid ProductId, Guid VariantId)> CreateProductAsync(HttpClient admin, string sku, decimal price = 20m, int stock = 10, string? category = "Mugs")
    {
        var product = await JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/products", new { sku, name = $"Product {sku}", price, stockQuantity = stock }));
        var id = product.GetProperty("id").GetGuid();
        await JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/products/{id}/content", new { brand = "TestBrand", description = "A base description.", category }));
        await JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/products/{id}/media", new { url = $"https://images.example.com/{sku}.jpg", purpose = 0, position = 0 }));
        var catalog = await JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/products/{id}/identifiers", new { type = (int)ProductIdentifierType.Upc, value = "000012345678" }));
        return (id, catalog.GetProperty("variants")[0].GetProperty("id").GetGuid());
    }

    public async Task<(Guid AccountId, Guid MarketId)> CreateAccountAsync(
        HttpClient admin, int channel, string name, string marketplace, object? settings = null, string? sellerId = null,
        bool liveWrites = true, bool orderImport = false, bool inventorySync = false, int priceConflictPolicy = 2)
    {
        var account = await JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/channels", new
        {
            channel,
            name,
            environment = 0,
            sellerId,
            settings,
            isEnabled = true,
            liveWritesEnabled = liveWrites,
            inventorySyncEnabled = inventorySync,
            orderImportEnabled = orderImport,
            priceConflictPolicy,
        }));
        var accountId = account.GetProperty("id").GetGuid();
        if (account.GetProperty("markets").GetArrayLength() > 0)
        {
            return (accountId, account.GetProperty("markets")[0].GetProperty("id").GetGuid());
        }

        var market = await JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, $"/api/channels/{accountId}/markets", new { marketplaceCode = marketplace }));
        return (accountId, market.GetProperty("id").GetGuid());
    }

    public async Task<Guid> SaveListingAsync(HttpClient admin, Guid marketId, Guid variantId, object? extra = null)
    {
        var body = JsonSerializer.SerializeToNode(extra ?? new { })!.AsObject();
        body["channelMarketId"] = marketId;
        body["variantId"] = variantId;
        body.TryAdd("fulfillmentMode", 0);
        var listing = await JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, "/api/channel-listings", body));
        return listing.GetProperty("id").GetGuid();
    }

    public Task<ChannelListing> ListingAsync(Guid id) => WithDbAsync(db => db.ChannelListings.AsNoTracking().FirstAsync(l => l.Id == id));

    public Task<List<SyncJob>> JobsAsync(Guid listingId) =>
        WithDbAsync(db => db.SyncJobs.AsNoTracking().Where(j => j.ChannelListingId == listingId).OrderBy(j => j.CreatedAtUtc).ToListAsync());

    /// <summary>The company's eBay connection, consented, so the eBay adapter can get tokens from the fake.</summary>
    public async Task ConnectEbayAsync(HttpClient admin)
    {
        Ebay.Reset();
        EbayApi.Reset();
        Ebay.Answer("/identity/v1/oauth2/token",
            """{"access_token":"ebay-access-token","expires_in":7200,"refresh_token":"ebay-refresh-token","refresh_token_expires_in":47304000}""");
        await JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, "/api/ebay/settings", new { environment = 0, clientId = "App-123", clientSecret = "ebay-cert-secret", ruName = "My-RuName" }));
        var connect = await JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/ebay/connect", new { }));
        var authorizeUrl = connect.GetProperty("authorizeUrl").GetString()!;
        var state = authorizeUrl[(authorizeUrl.IndexOf("&state=", StringComparison.Ordinal) + 7)..];
        await JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/ebay/complete", new { codeOrUrl = $"https://localhost/ebay?code=the-code&state={state}" }));
    }

    public static readonly object EbaySettings = new
    {
        merchantLocationKey = "MAIN",
        fulfillmentPolicyId = "fp-1",
        paymentPolicyId = "pp-1",
        returnPolicyId = "rp-1",
    };
}
