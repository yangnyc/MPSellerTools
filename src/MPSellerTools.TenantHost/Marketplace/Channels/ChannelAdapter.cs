using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;

namespace MPSellerTools.TenantHost.Marketplace.Channels;

/// <summary>The account and marketplace an operation runs against, with the account's non-secret settings.</summary>
public record ChannelContext(ChannelAccount Account, ChannelMarket Market, JsonElement Settings)
{
    public string? Setting(string name) =>
        Settings.ValueKind == JsonValueKind.Object && Settings.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// One listing to act on: its resolved content, the versions that content
/// was read at, and the ids the channel has given it so far.
/// </summary>
public record ListingWork(
    ListingSnapshot Snapshot,
    ListingDesiredState DesiredState,
    long ContentVersion,
    long PriceVersion,
    long InventoryVersion,
    IReadOnlyDictionary<ExternalResourceType, string> References,
    bool GroupReady);

/// <summary>A request an adapter would send, as data. Building one touches no network.</summary>
public record ChannelRequest(string Method, string Url, object? Body);

public enum OutcomeKind
{
    /// <summary>The channel's own answer shows the change is in effect.</summary>
    Confirmed,

    /// <summary>The channel took the submission; whether it worked is not known yet.</summary>
    Accepted,
    Failed,
}

/// <summary>What the channel reports about a listing right now.</summary>
public record RemoteState(ListingObservedStatus Status, decimal? Price, int? Quantity, IReadOnlyList<ValidationIssue>? Issues = null);

public record OperationOutcome(
    OutcomeKind Kind,
    SyncErrorClass ErrorClass = SyncErrorClass.None,
    string? Message = null,
    string? SubmissionId = null,
    TimeSpan? RetryAfter = null,
    int? HttpStatus = null,
    string? RequestId = null,
    RemoteState? State = null,
    IReadOnlyDictionary<ExternalResourceType, string>? References = null,
    string? GroupListingId = null)
{
    public static OperationOutcome Confirmed(RemoteState? state = null, IReadOnlyDictionary<ExternalResourceType, string>? references = null) =>
        new(OutcomeKind.Confirmed, State: state, References: references);

    public static OperationOutcome Accepted(string? submissionId, IReadOnlyDictionary<ExternalResourceType, string>? references = null) =>
        new(OutcomeKind.Accepted, SubmissionId: submissionId, References: references);

    public static OperationOutcome Failed(SyncErrorClass errorClass, string message, IReadOnlyList<ValidationIssue>? issues = null) =>
        new(OutcomeKind.Failed, errorClass, message, State: issues is null ? null : new RemoteState(ListingObservedStatus.Rejected, null, null, issues));

    public static OperationOutcome From(ChannelException ex) =>
        new(OutcomeKind.Failed, ex.ErrorClass, ex.Message, RetryAfter: ex.RetryAfter, HttpStatus: ex.HttpStatus, RequestId: ex.RequestId);
}

public record ChannelOrderLine(string ExternalLineId, string? SellerSku, string? Title, int Quantity, decimal UnitPrice);

public record ChannelOrder(
    string ExternalOrderId,
    OrderStatus Status,
    DateTime? CreatedAtUtc,
    string? Currency,
    bool FulfilledByChannel,
    IReadOnlyList<ChannelOrderLine> Lines);

/// <summary>
/// A channel call that did not succeed. <see cref="Ambiguous"/> means no
/// answer came back at all, so the request may or may not have taken effect.
/// The message never contains credentials.
/// </summary>
public class ChannelException(
    SyncErrorClass errorClass, string message, int? httpStatus = null, TimeSpan? retryAfter = null, string? requestId = null, bool ambiguous = false, JsonElement? body = null)
    : Exception(message)
{
    public SyncErrorClass ErrorClass { get; } = errorClass;

    public int? HttpStatus { get; } = httpStatus;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    public string? RequestId { get; } = requestId;

    public bool Ambiguous { get; } = ambiguous;

    public JsonElement? Body { get; } = body;
}

/// <summary>
/// What every channel can be asked to do. Channels differ, and this does not
/// hide it: an operation a channel has no equivalent for returns a failure
/// saying so rather than calling a made-up endpoint.
/// </summary>
public interface IChannelAdapter
{
    SalesChannel Channel { get; }

    /// <summary>How many listings one call of an operation can carry; 1 when the channel takes them singly.</summary>
    int BatchSize(SyncOperation operation);

    /// <summary>Checks particular to this channel, on top of <see cref="ListingValidator.Common"/>.</summary>
    IReadOnlyList<ValidationIssue> Validate(ListingWork work, ChannelContext context);

    /// <summary>The requests the operation would send, without sending them.</summary>
    IReadOnlyList<ChannelRequest> Preview(SyncOperation operation, ListingWork work, ChannelContext context);

    /// <summary>Carries out the operation for each listing and reports on each one separately, in order.</summary>
    Task<IReadOnlyList<OperationOutcome>> ExecuteAsync(
        SyncOperation operation, ChannelContext context, IReadOnlyList<ListingWork> items, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the listing's state from the channel. <see cref="OutcomeKind.Accepted"/>
    /// means the channel is still working on <paramref name="submissionId"/>.
    /// </summary>
    Task<OperationOutcome> GetStatusAsync(ChannelContext context, ListingWork work, string? submissionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChannelOrder>> GetOrdersAsync(ChannelContext context, DateTime sinceUtc, CancellationToken cancellationToken);

    /// <summary>Fetches what a category requires, or null when this channel offers no way that is implemented here.</summary>
    Task<CategoryRequirements?> FetchRequirementsAsync(ChannelContext context, string externalCategoryId, CancellationToken cancellationToken);
}

/// <summary>Runs single-listing operations one by one; adapters override the batch entry point where the channel has one.</summary>
public abstract class ChannelAdapterBase : IChannelAdapter
{
    public abstract SalesChannel Channel { get; }

    public virtual int BatchSize(SyncOperation operation) => 1;

    public abstract IReadOnlyList<ValidationIssue> Validate(ListingWork work, ChannelContext context);

    public abstract IReadOnlyList<ChannelRequest> Preview(SyncOperation operation, ListingWork work, ChannelContext context);

    public virtual async Task<IReadOnlyList<OperationOutcome>> ExecuteAsync(
        SyncOperation operation, ChannelContext context, IReadOnlyList<ListingWork> items, CancellationToken cancellationToken)
    {
        var outcomes = new List<OperationOutcome>();
        foreach (var item in items)
        {
            try
            {
                outcomes.Add(await ExecuteOneAsync(operation, context, item, cancellationToken));
            }
            catch (ChannelException ex)
            {
                outcomes.Add(OperationOutcome.From(ex));
            }
        }
        return outcomes;
    }

    protected abstract Task<OperationOutcome> ExecuteOneAsync(
        SyncOperation operation, ChannelContext context, ListingWork work, CancellationToken cancellationToken);

    public abstract Task<OperationOutcome> GetStatusAsync(
        ChannelContext context, ListingWork work, string? submissionId, CancellationToken cancellationToken);

    public virtual Task<IReadOnlyList<ChannelOrder>> GetOrdersAsync(ChannelContext context, DateTime sinceUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ChannelOrder>>([]);

    public virtual Task<CategoryRequirements?> FetchRequirementsAsync(ChannelContext context, string externalCategoryId, CancellationToken cancellationToken) =>
        Task.FromResult<CategoryRequirements?>(null);

    protected ValidationIssue Issue(string path, string code, string message) => new(Channel, path, code, message);
}

public record ChannelResponse(int Status, JsonElement Body, string? RequestId);

/// <summary>An item already in a channel's catalog (for Amazon, an ASIN) that an offer can be made on.</summary>
public record CatalogSearchResult(string CatalogItemId, string? Title, string? Brand);

/// <summary>
/// Paces outgoing calls so a channel's limits are respected before it has to
/// say so: one token bucket per channel, shared by everything in this process.
/// The channel's own Retry-After and the job backoff still apply on top; this
/// only keeps a busy queue from running into them in the first place.
/// </summary>
public sealed class ChannelRateLimiter(IOptions<MarketplaceOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(channel =>
    {
        var perSecond = options.Value.RequestsPerSecond;
        return perSecond <= 0
            ? RateLimitPartition.GetNoLimiter(channel)
            : RateLimitPartition.GetTokenBucketLimiter(channel, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = Math.Max(perSecond, options.Value.RequestBurst),
                TokensPerPeriod = perSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 500,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
    });

    /// <summary>Waits for a turn. False when so many calls are already waiting that this one was turned away.</summary>
    public async Task<bool> WaitAsync(string channel, CancellationToken cancellationToken)
    {
        using var lease = await _limiter.AcquireAsync(channel, 1, cancellationToken);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}

/// <summary>
/// The HTTP transport shared by the marketplace adapters. It knows nothing
/// about payloads: it sends, classifies failures, and never puts a request's
/// headers or body into an error.
/// </summary>
public class ChannelHttp(IHttpClientFactory httpClientFactory, ChannelRateLimiter? limiter = null)
{
    public const string AmazonClient = "amazon";
    public const string WalmartClient = "walmart";

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public static HttpRequestMessage JsonRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    public static string Serialize(object body) => JsonSerializer.Serialize(body, Json);

    /// <summary>
    /// Sends the request. A status in <paramref name="alsoAccept"/> is
    /// returned like a success, for calls where it carries meaning (a 404
    /// from a lookup); any other failure is thrown, already classified.
    /// </summary>
    public async Task<ChannelResponse> SendAsync(
        string clientName, string channelName, HttpRequestMessage request, CancellationToken cancellationToken, params int[] alsoAccept)
    {
        if (limiter is not null && !await limiter.WaitAsync(clientName, cancellationToken))
        {
            // Nothing was sent, so this is safe to try again and not ambiguous.
            throw new ChannelException(SyncErrorClass.Transient, $"Too many requests to {channelName} are already waiting; this one will be tried again.");
        }

        HttpResponseMessage response;
        string text;
        try
        {
            response = await httpClientFactory.CreateClient(clientName).SendAsync(request, cancellationToken);
            text = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException && !cancellationToken.IsCancellationRequested)
        {
            throw new ChannelException(
                SyncErrorClass.Transient, $"{channelName} did not answer; the request may or may not have been carried out.", ambiguous: true);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var requestId = HeaderOf(response, "x-amzn-RequestId") ?? HeaderOf(response, "rlogid") ?? HeaderOf(response, "WM_QOS.CORRELATION_ID");
            var body = Parse(text);
            if (response.IsSuccessStatusCode || alsoAccept.Contains(status))
            {
                return new ChannelResponse(status, body, requestId);
            }

            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            throw new ChannelException(
                Classify(status), $"{channelName} refused the request ({status}): {ErrorText(body)}", status, retryAfter, requestId, body: body);
        }
    }

    public static SyncErrorClass Classify(int status) => status switch
    {
        401 or 403 => SyncErrorClass.Authorization,
        408 or 425 or 429 => SyncErrorClass.Transient,
        >= 500 => SyncErrorClass.Transient,
        400 or 404 or 409 or 422 => SyncErrorClass.DataCorrection,
        _ => SyncErrorClass.Permanent,
    };

    private static string? HeaderOf(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static JsonElement Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    // The channels' error shapes: {"errors":[{"message"}]}, {"error_description"}, {"error":[{"description"}]}.
    private static string ErrorText(JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.Object)
        {
            if (body.TryGetProperty("error_description", out var description) && description.ValueKind == JsonValueKind.String)
            {
                return Truncate(description.GetString()!);
            }

            foreach (var listName in new[] { "errors", "error" })
            {
                if (body.TryGetProperty(listName, out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0)
                {
                    foreach (var field in new[] { "message", "description", "longMessage" })
                    {
                        if (list[0].ValueKind == JsonValueKind.Object && list[0].TryGetProperty(field, out var message) && message.ValueKind == JsonValueKind.String)
                        {
                            return Truncate(message.GetString()!);
                        }
                    }
                }
            }
        }
        return "no reason given";
    }

    private static string Truncate(string text) => text.Length > 300 ? text[..300] : text;
}

/// <summary>
/// Short-lived access tokens, one per account, shared by every worker in the
/// process. A token is fetched under a per-account lock, so workers that
/// need one at the same moment make a single request between them.
/// </summary>
public class ChannelTokenCache
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<Guid, (string Token, DateTime ExpiresAtUtc)> _tokens = new();

    public async Task<string> GetAsync(Guid accountId, Func<Task<(string Token, TimeSpan Lifetime)>> fetch, CancellationToken cancellationToken)
    {
        if (_tokens.TryGetValue(accountId, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
        {
            return cached.Token;
        }

        var gate = _locks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_tokens.TryGetValue(accountId, out cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
            {
                return cached.Token;
            }

            var (token, lifetime) = await fetch();
            // Stop using it a minute early, so a call never starts with a token about to lapse.
            _tokens[accountId] = (token, DateTime.UtcNow + lifetime - TimeSpan.FromSeconds(60));
            return token;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Forgets the account's token, after the channel has turned it down or its credentials changed.</summary>
    public void Invalidate(Guid accountId) => _tokens.TryRemove(accountId, out _);
}

/// <summary>Encrypts channel credentials with this instance's data protection keys, as the eBay keys are.</summary>
public class ChannelSecrets(IDataProtectionProvider dataProtection)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("MPSellerTools.Channels");

    public string Protect(IReadOnlyDictionary<string, string> credentials) => _protector.Protect(JsonSerializer.Serialize(credentials));

    public IReadOnlyDictionary<string, string> Unprotect(ChannelAccount account)
    {
        if (string.IsNullOrEmpty(account.CredentialsProtected))
        {
            throw new ChannelException(SyncErrorClass.Authorization, $"No credentials are saved for the account \"{account.Name}\".");
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(_protector.Unprotect(account.CredentialsProtected)) ?? [];
    }

    public static string Required(IReadOnlyDictionary<string, string> credentials, string name) =>
        credentials.TryGetValue(name, out var value) && value.Length > 0
            ? value
            : throw new ChannelException(SyncErrorClass.Authorization, $"The account's credentials are missing \"{name}\".");
}
