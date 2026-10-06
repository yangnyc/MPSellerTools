using System.Net.Http.Json;
using System.Text.Json;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// Small helpers so integration tests exercise the API exactly the way the
/// real SPA does — fetch an antiforgery token, echo it back on mutating
/// requests — rather than bypassing that protection for convenience.
/// </summary>
public static class TenantApiHelpers
{
    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery/token");
        return response.GetProperty("token").GetString()!;
    }

    public static async Task<HttpResponseMessage> PostJsonWithAntiforgeryAsync(HttpClient client, string url, object body)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PutJsonWithAntiforgeryAsync(HttpClient client, string url, object body)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> DeleteWithAntiforgeryAsync(HttpClient client, string url)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    public static async Task<bool> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await PostJsonWithAntiforgeryAsync(client, "/api/auth/login", new { email, password });
        return response.IsSuccessStatusCode;
    }
}
