using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MPSellerTools.Infrastructure.Tenants;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// Boots a real MPSellerTools.TenantHost (real EF Core context, real
/// Identity, real cookie/antiforgery config — not a hand-rolled test
/// double) against a uniquely-named LocalDB database created fresh for this
/// fixture instance and dropped in DisposeAsync, so parallel test runs never
/// collide and never leave databases behind (brief §11/§13: "Use unique test
/// database names for integration tests and delete only databases created
/// by the current test run").
///
/// Configuration is injected via the exact same MPST_INSTANCE_CONFIG_FILE
/// environment variable the real provisioning worker uses (see
/// TenantHost/Program.cs), not WebApplicationFactory's ConfigureAppConfiguration
/// hook — Program.cs is top-level statements that read builder.Configuration
/// eagerly before Build(), which runs before that hook ever gets a chance to
/// contribute (a real gotcha hit while writing this fixture). Because the
/// env var is process-wide, [assembly: CollectionBehavior(DisableTestParallelization = true)]
/// in AssemblyInfo.cs keeps two fixtures from starting concurrently and
/// racing on it.
/// </summary>
public class TenantHostFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"MPSellerTools_Test_{Guid.NewGuid():N}";
    public Guid TenantId { get; } = Guid.NewGuid();
    public Guid ApplicationInstanceId { get; } = Guid.NewGuid();
    public string Slug { get; } = $"test-{Guid.NewGuid():N}"[..14];

    private readonly string _instanceConfigPath = Path.Combine(Path.GetTempPath(), $"mpst-test-config-{Guid.NewGuid():N}.json");

    private string LocalDataDirectory => Path.Combine(Path.GetTempPath(), "mpst-tests", DatabaseName);

    /// <summary>The key this instance accepts from the platform console, as the platform would read it.</summary>
    public string PlatformAccessKey =>
        Infrastructure.Hosting.PlatformAccessKey.TryRead(Infrastructure.Hosting.PlatformAccessKey.PathFor(LocalDataDirectory, Slug))!;

    private string ConnectionString =>
        $"Server=(localdb)\\MSSQLLocalDB;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True";

    public TenantHostFixture()
    {
        // The real cookie/antiforgery config requires Secure (HTTPS-only)
        // cookies by design (brief §7) — WebApplicationFactory's TestServer
        // defaults its client to http://localhost, which trips
        // AntiforgeryOptions.Cookie.SecurePolicy = Always with a 500, not a
        // meaningful auth failure. TestServer treats the request as HTTPS
        // based on the absolute URI's scheme alone (no real TLS handshake
        // needed for in-memory testing), so simply pointing the client at
        // https:// is enough — reproduced and fixed while writing these tests.
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    /// <summary>Stands in for eBay: every call the host makes to eBay's APIs is answered by this.</summary>
    public FakeEbay Ebay { get; } = new();

    /// <summary>Method-aware answers for eBay's Inventory API; consulted before <see cref="Ebay"/>'s own.</summary>
    public ChannelRouter EbayApi { get; } = new();

    /// <summary>Stands in for Amazon's Selling Partner API.</summary>
    public ChannelRouter Amazon { get; } = new();

    /// <summary>Stands in for Walmart's Marketplace API.</summary>
    public ChannelRouter Walmart { get; } = new();

    /// <summary>Stands in for a Magento store's REST API.</summary>
    public ChannelRouter Magento { get; } = new();

    /// <summary>Stands in for another store's public website, read by the website import.</summary>
    public ChannelRouter Storefront { get; } = new();

    /// <summary>
    /// The host's "Marketplace" settings. The sync worker and the startup
    /// backfill are off for every test host: tests that want them run them
    /// by hand, so nothing happens behind a test's back.
    /// </summary>
    /// <summary>Invitations are off unless a host switches them on; these tests cover them, so they are on here.</summary>
    protected virtual bool InvitationsEnabled => true;

    // Stock accounting is said outright, so a machine whose own local settings switch it on tests the same thing.
    protected virtual object MarketplaceSettings =>
        new { WorkerEnabled = false, AutoBackfill = false, RequestsPerSecond = 0, LiveWritesEnabled = false, InventoryAccountingEnabled = false };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        Ebay.Intercept = EbayApi.Respond;
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(TenantHost.Services.EbayClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Ebay);
            services.AddHttpClient(TenantHost.Marketplace.Channels.ChannelHttp.AmazonClient)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeChannelApi(Amazon));
            services.AddHttpClient(TenantHost.Marketplace.Channels.ChannelHttp.WalmartClient)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeChannelApi(Walmart));
            services.AddHttpClient(TenantHost.Marketplace.Channels.ChannelHttp.MagentoClient)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeChannelApi(Magento));
            services.AddHttpClient(TenantHost.Services.WebsiteImport.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeChannelApi(Storefront));
        });
    }

    public async Task InitializeAsync()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            ConnectionStrings = new { TenantDatabase = ConnectionString },
            Tenant = new
            {
                TenantId,
                ApplicationInstanceId,
                Slug,
                DisplayName = "Test Tenant",
                Url = "https://localhost:0",
            },
            Hosting = new { LocalDataDirectory },
            Marketplace = MarketplaceSettings,
            Features = new { InvitationsEnabled },
        });
        await File.WriteAllTextAsync(_instanceConfigPath, json);
        Environment.SetEnvironmentVariable("MPST_INSTANCE_CONFIG_FILE", _instanceConfigPath);

        try
        {
            // Force the host to actually start (WebApplicationFactory is lazy
            // otherwise), which runs its Development-mode migrate/seed block.
            using var client = CreateClient();
            var response = await client.GetAsync("/api/health");
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            // Program.cs already read and cached everything it needs into
            // DI singletons by the time the host finished starting above, so
            // clearing the env var immediately keeps the window in which it
            // could matter to another fixture as small as possible.
            Environment.SetEnvironmentVariable("MPST_INSTANCE_CONFIG_FILE", null);
        }
    }

    /// <summary>
    /// Creates a user directly via UserManager (real Identity password
    /// hashing, real role assignment) rather than through the invitation
    /// email flow — that flow is covered by its own test; these tests are
    /// about what happens once a user already exists in a given role.
    /// </summary>
    public async Task<Guid> CreateUserAsync(string email, string password, string role)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<TenantUser>>();

        var user = new TenantUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = email,
            EmailConfirmed = true,
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }
        await userManager.AddToRoleAsync(user, role);
        return user.Id;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(_instanceConfigPath);

        await using var connection = new SqlConnection(
            "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID('{DatabaseName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{DatabaseName}]; END";
        await command.ExecuteNonQueryAsync();
    }
}
