using System.Net;
using System.Text;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// Answers the host's calls to eBay from canned JSON, keyed by a fragment of
/// the request address, and records what was asked. Nothing leaves the machine.
/// </summary>
public class FakeEbay : HttpMessageHandler
{
    private readonly List<(string UrlFragment, HttpStatusCode Status, string Json)> _answers = [];

    /// <summary>Every request made, as "METHOD address", then the form body for a token request.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>
    /// Asked first, when set: lets a test answer by HTTP method as well as
    /// address. Returning null falls through to the answers added here.
    /// </summary>
    public Func<HttpRequestMessage, string, HttpResponseMessage?>? Intercept { get; set; }

    public void Reset()
    {
        _answers.Clear();
        Requests.Clear();
    }

    /// <summary>The first matching answer added wins.</summary>
    public void Answer(string urlFragment, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        _answers.Add((urlFragment, status, json));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add($"{request.Method} {url} {body}".TrimEnd());
        if (Intercept?.Invoke(request, body) is { } intercepted)
        {
            return intercepted;
        }

        var answer = _answers.FirstOrDefault(a => url.Contains(a.UrlFragment, StringComparison.Ordinal));
        return answer.UrlFragment is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}", Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Json, Encoding.UTF8, "application/json") };
    }

    // The fixture owns this handler for its whole life; an HttpClient must not dispose it.
    protected override void Dispose(bool disposing)
    {
    }
}
