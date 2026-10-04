using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhotoKeepKill;

internal sealed class LicenseClient
{
    private const string DefaultApiBaseUrl = "https://www.trykeeply.live/";
    private const string DefaultCheckoutUrl = "https://www.creem.io/payment/prod_6z7buEm087gA7b7rLpO94B";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _baseUrl;
    public string CheckoutUrl { get; }
    public LicenseClient()
    {
        var config = Path.Combine(AppContext.BaseDirectory, "licensing.json");
        using var json = File.Exists(config) ? JsonDocument.Parse(File.ReadAllText(config)) : null;
        var configuredApi = json?.RootElement.TryGetProperty("apiBaseUrl", out var api) == true ? api.GetString() : null;
        var configuredCheckout = json?.RootElement.TryGetProperty("checkoutUrl", out var checkout) == true ? checkout.GetString() : null;
#if !DEBUG
        if (Uri.TryCreate(configuredApi, UriKind.Absolute, out var configuredUri) && configuredUri.IsLoopback)
            configuredApi = null;
#endif
        _baseUrl = string.IsNullOrWhiteSpace(configuredApi) ? DefaultApiBaseUrl : configuredApi;
        CheckoutUrl = string.IsNullOrWhiteSpace(configuredCheckout) ? DefaultCheckoutUrl : configuredCheckout;
        _baseUrl = _baseUrl.TrimEnd('/');
    }
    public async Task<ActivationResult> ActivateAsync(string key, string installName)
    {
        var result = await SendAsync("activate", new { key, instanceName = installName });
        var instanceId = result.GetProperty("instanceId").GetString() ?? throw new InvalidDataException("The licensing service did not return an activation ID.");
        var signedToken = result.GetProperty("licenseToken").GetString() ?? throw new InvalidDataException("The licensing service did not return a signed license token.");
        if (!LicenseTokenVerifier.IsValid(signedToken, instanceId))
            throw new InvalidDataException("The licensing service returned a license token that Keeply could not verify.");
        return new ActivationResult(instanceId, signedToken);
    }
    public async Task<ValidationResult> ValidateAsync(string key, string instanceId)
    {
        var result = await SendAsync("validate", new { key, instanceId });
        var isValid = result.TryGetProperty("valid", out var valid) && valid.ValueKind == JsonValueKind.True;
        var signedToken = result.TryGetProperty("licenseToken", out var token) ? token.GetString() : null;
        if (isValid && (signedToken is null || !LicenseTokenVerifier.IsValid(signedToken, instanceId)))
            throw new InvalidDataException("The licensing service returned a license token that Keeply could not verify.");
        return new ValidationResult(isValid, signedToken);
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

    public sealed record ActivationResult(string InstanceId, string SignedToken);
    public sealed record ValidationResult(bool IsValid, string? SignedToken);
}
