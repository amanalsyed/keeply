using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhotoKeepKill;

internal sealed class LicenseClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _baseUrl;
    public string CheckoutUrl { get; }
    public LicenseClient()
    {
        var config = Path.Combine(AppContext.BaseDirectory, "licensing.json");
        using var json = File.Exists(config) ? JsonDocument.Parse(File.ReadAllText(config)) : null;
        _baseUrl = json?.RootElement.TryGetProperty("apiBaseUrl", out var api) == true ? api.GetString() ?? "" : "";
        CheckoutUrl = json?.RootElement.TryGetProperty("checkoutUrl", out var checkout) == true ? checkout.GetString() ?? "" : "";
        _baseUrl = _baseUrl.TrimEnd('/');
    }
    public async Task<string> ActivateAsync(string key, string installName)
    {
        var result = await SendAsync("activate", new { key, instanceName = installName });
        return result.GetProperty("instanceId").GetString() ?? throw new InvalidDataException("The licensing service did not return an activation ID.");
    }
    public async Task DeactivateAsync(string key, string instanceId) => _ = await SendAsync("deactivate", new { key, instanceId });
    private async Task<JsonElement> SendAsync(string action, object payload)
    {
        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && (uri.Host == "localhost" || uri.Host == "127.0.0.1"))))
            throw new InvalidOperationException("License service is not configured. Configure licensing.json with your deployed HTTPS API URL.");
        using var response = await Http.PostAsJsonAsync(new Uri(uri, $"api/license/{action}"), payload);
        using var body = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(body);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(json.RootElement.TryGetProperty("error", out var error) ? error.GetString() : "License service request failed.");
        return json.RootElement.Clone();
    }
}
