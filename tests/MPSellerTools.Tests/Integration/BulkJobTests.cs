using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.TenantHost.Marketplace;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// Bulk jobs: asked for through the API, carried out by the runner (called
/// here by hand, as the background worker is off in tests), with a fake
/// Magento store behind the sales channel they work on.
/// </summary>
public class BulkJobTests(MarketplaceFixture fixture) : IClassFixture<MarketplaceFixture>
{
    private const int Magento = (int)SalesChannel.Magento;
    private const string Store = "https://jobs.example.test";

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object? body = null) =>
        TenantApiHelpers.PostJsonWithAntiforgeryAsync(client, url, body ?? new { });

    private Task<bool> RunNextAsync() => fixture.WithScopeAsync(services => services.GetRequiredService<BulkJobRunner>().RunNextAsync(default));

    /// <summary>The one Magento account of this fixture's database, with every listing and job of earlier tests cleared away.</summary>
    private async Task<(Guid AccountId, Guid MarketId)> MagentoAsync(HttpClient admin)
    {
        fixture.Magento.Reset();
        await fixture.WithDbAsync(async db =>
        {
            await db.BulkJobs.ExecuteDeleteAsync();
            await db.SyncJobs.ExecuteDeleteAsync();
            await db.OutboxEvents.ExecuteDeleteAsync();
            return await db.ChannelListings.ExecuteDeleteAsync();
        });
        var existing = (await admin.GetFromJsonAsync<JsonElement>("/api/channels")).EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("channel").GetInt32() == Magento);
        if (existing.ValueKind == JsonValueKind.Object)
        {
            return (existing.GetProperty("id").GetGuid(), existing.GetProperty("markets")[0].GetProperty("id").GetGuid());
        }

        var created = await fixture.CreateAccountAsync(admin, Magento, "Our store", "default", new { baseUrl = Store });
        await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/channels/{created.AccountId}/credentials", new { credentials = new { accessToken = "magento-integration-token" } });
        return created;
    }

    private static async Task<JsonElement> StartAsync(HttpClient admin, BulkJobType type, Guid accountId) =>
        await MarketplaceFixture.JsonAsync(await PostAsync(admin, "/api/bulk-jobs", new { type = (int)type, channelAccountId = accountId }));

    [Fact]
    public async Task Publishing_every_draft_queues_the_ones_that_pass_and_names_the_ones_held_back()
    {
        using var admin = await fixture.AdminAsync("jobs-publish@example.com");
        var (accountId, marketId) = await MagentoAsync(admin);
        var listingIds = new Dictionary<string, Guid>();
        foreach (var sku in new[] { "JOB-A", "JOB-B", "JOB-C" })
        {
            var (_, variantId) = await fixture.CreateProductAsync(admin, sku);
            // A category Magento cannot take holds JOB-B back.
            listingIds[sku] = await fixture.SaveListingAsync(admin, marketId, variantId, sku == "JOB-B" ? new { externalCategoryId = "not-a-number" } : null);
        }

        var queued = await StartAsync(admin, BulkJobType.PublishDrafts, accountId);
        Assert.Equal((int)BulkJobStatus.Queued, queued.GetProperty("status").GetInt32());
        Assert.Equal("Our store", queued.GetProperty("accountName").GetString());
        var jobId = queued.GetProperty("id").GetGuid();

        // The same job for the same channel cannot be queued twice at once.
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(admin, "/api/bulk-jobs", new { type = (int)BulkJobType.PublishDrafts, channelAccountId = accountId })).StatusCode);

        Assert.True(await RunNextAsync());
        Assert.False(await RunNextAsync());

        var job = await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{jobId}");
        Assert.Equal((int)BulkJobStatus.CompletedWithErrors, job.GetProperty("status").GetInt32());
        Assert.Equal((3, 3, 2, 1), (job.GetProperty("total").GetInt32(), job.GetProperty("processed").GetInt32(), job.GetProperty("succeeded").GetInt32(), job.GetProperty("failed").GetInt32()));
        Assert.Equal("2 queued for publishing; 1 held back.", job.GetProperty("summary").GetString());
        var error = Assert.Single(job.GetProperty("errors").EnumerateArray());
        Assert.Equal("JOB-B", error.GetProperty("item").GetString());
        Assert.Contains("category", error.GetProperty("message").GetString());

        // The two that passed are wanted on sale; the one held back is still a draft, with the reason on record.
        Assert.Equal(ListingDesiredState.Active, (await fixture.ListingAsync(listingIds["JOB-A"])).DesiredState);
        Assert.Equal(ListingDesiredState.Active, (await fixture.ListingAsync(listingIds["JOB-C"])).DesiredState);
        var heldBack = await fixture.ListingAsync(listingIds["JOB-B"]);
        Assert.Equal(ListingDesiredState.Draft, heldBack.DesiredState);
        Assert.Contains("category", heldBack.IssuesJson);

        // The sync queue then carries them to the store, exactly as a single Publish would.
        fixture.Magento.On("POST", "/rest/all/V1/products", """{"id":1,"status":1,"price":20}""");
        // Their category is not mapped, so the store is asked for its own, which has one of that name.
        fixture.Magento.On("GET", "/rest/all/V1/categories", """
            {"id":1,"parent_id":0,"name":"Root Catalog","level":0,"children_data":[
              {"id":2,"parent_id":1,"name":"Default Category","level":1,"children_data":[
                {"id":12,"parent_id":2,"name":"Mugs","level":2,"children_data":[]}]}]}
            """);
        await fixture.SyncAsync();
        Assert.Equal(2, fixture.Magento.Count("POST", $"{Store}/rest/all/V1/products"));
        Assert.Equal(0, fixture.Magento.Count("POST", $"{Store}/rest/all/V1/categories"));

        // It is in the audit trail under whoever asked for it.
        Assert.Contains(await fixture.WithDbAsync(db => db.AuditEntries.Where(a => a.Action == "BulkJobFinished").Select(a => a.ActorEmail).ToListAsync()), email => email == "jobs-publish@example.com");

        // Run again, it works on what is left: the one draft, held back once more.
        var again = await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/bulk-jobs/{jobId}/run-again"));
        Assert.NotEqual(jobId, again.GetProperty("id").GetGuid());
        Assert.True(await RunNextAsync());
        var second = await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{again.GetProperty("id").GetGuid()}");
        Assert.Equal((1, 0, 1), (second.GetProperty("total").GetInt32(), second.GetProperty("succeeded").GetInt32(), second.GetProperty("failed").GetInt32()));

        // Taking everything off sale is a job too.
        var off = await StartAsync(admin, BulkJobType.TakeOffSale, accountId);
        Assert.True(await RunNextAsync());
        var done = await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{off.GetProperty("id").GetGuid()}");
        Assert.Equal((int)BulkJobStatus.Succeeded, done.GetProperty("status").GetInt32());
        Assert.Equal("2 queued to come off sale.", done.GetProperty("summary").GetString());
        Assert.Equal(ListingDesiredState.Inactive, (await fixture.ListingAsync(listingIds["JOB-A"])).DesiredState);
    }

    [Fact]
    public async Task A_job_can_be_stopped_before_it_starts_and_removed_once_finished()
    {
        using var admin = await fixture.AdminAsync("jobs-cancel@example.com");
        var (accountId, _) = await MagentoAsync(admin);

        var queued = await StartAsync(admin, BulkJobType.CheckListings, accountId);
        var jobId = queued.GetProperty("id").GetGuid();
        // An unfinished job cannot be removed or run again.
        Assert.Equal(HttpStatusCode.Conflict, (await TenantApiHelpers.DeleteWithAntiforgeryAsync(admin, $"/api/bulk-jobs/{jobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(admin, $"/api/bulk-jobs/{jobId}/run-again")).StatusCode);

        var cancelled = await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/bulk-jobs/{jobId}/cancel"));
        Assert.Equal((int)BulkJobStatus.Cancelled, cancelled.GetProperty("status").GetInt32());
        // Nothing is left for the runner.
        Assert.False(await RunNextAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(admin, $"/api/bulk-jobs/{jobId}/cancel")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await TenantApiHelpers.DeleteWithAntiforgeryAsync(admin, $"/api/bulk-jobs/{jobId}")).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>("/api/bulk-jobs")).EnumerateArray());

        // A job with nothing to do says so rather than failing.
        var empty = await StartAsync(admin, BulkJobType.SendAgain, accountId);
        Assert.True(await RunNextAsync());
        var done = await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{empty.GetProperty("id").GetGuid()}");
        Assert.Equal((int)BulkJobStatus.Succeeded, done.GetProperty("status").GetInt32());
        Assert.Equal("There was nothing to do.", done.GetProperty("summary").GetString());

        // An employee has no part in this.
        await fixture.CreateUserAsync("jobs-employee@example.com", "Password123!", "Employee");
        using var employee = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(employee, "jobs-employee@example.com", "Password123!"));
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/bulk-jobs")).StatusCode);
    }

    [Fact]
    public async Task Reading_the_store_runs_as_a_job_and_a_refusal_fails_it_with_the_stores_own_words()
    {
        using var admin = await fixture.AdminAsync("jobs-read@example.com");
        var (accountId, _) = await MagentoAsync(admin);

        fixture.Magento.On("GET", "/rest/all/V1/products?", """
            {"total_count":2,"items":[
              {"id":901,"sku":"JOBREAD-1","name":"Store thing","price":5,"status":1},
              {"id":902,"sku":"JOBREAD-2","name":"Other thing","price":6,"status":1}]}
            """);
        fixture.Magento.On("GET", "/rest/V1/stockItems/lowStock/", """{"total_count":0,"items":[]}""");

        var queued = await StartAsync(admin, BulkJobType.ReadStore, accountId);
        Assert.True(await RunNextAsync());
        var job = await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{queued.GetProperty("id").GetGuid()}");
        Assert.Equal((int)BulkJobStatus.Succeeded, job.GetProperty("status").GetInt32());
        Assert.Equal("2 product(s) read from the store, 2 new to the catalog.", job.GetProperty("summary").GetString());
        Assert.Equal(2, await fixture.WithDbAsync(db => db.Listings.CountAsync(l => l.Channel == SalesChannel.Magento)));

        fixture.Magento.On("GET", "/rest/all/V1/products?", """{"message":"The consumer isn't authorized to access %resources."}""", HttpStatusCode.Unauthorized);
        var refused = await StartAsync(admin, BulkJobType.ReadStore, accountId);
        Assert.True(await RunNextAsync());
        var failed = await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{refused.GetProperty("id").GetGuid()}");
        Assert.Equal((int)BulkJobStatus.Failed, failed.GetProperty("status").GetInt32());
        Assert.Contains("isn't authorized", failed.GetProperty("lastError").GetString());
    }
}
