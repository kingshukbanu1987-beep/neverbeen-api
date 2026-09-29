using System.Net.Http.Headers;
using System.Text.Json;
using NeverBeen.API.Common;

namespace NeverBeen.API.Services;

public interface IOauthLoginProvider
{
    /// <summary>Canonical provider name: "Google", "Facebook" or "Microsoft".</summary>
    string ProviderName { get; }

    /// <summary>Exchanges an OAuth authorization code for the external account profile.</summary>
    Task<ExternalUserProfile> GetExternalUserAsync(string code, CancellationToken cancellationToken = default);
}

/// <summary>Profile returned by the provider's user-info endpoint.</summary>
public class ExternalUserProfile
{
    public string ProviderKey { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PictureUrl { get; set; }
    public bool EmailVerified { get; set; }
}

public abstract class OauthLoginProviderBase : IOauthLoginProvider
{
    protected readonly IHttpClientFactory _httpClientFactory;
    protected readonly OauthOptions _options;

    protected OauthLoginProviderBase(IHttpClientFactory httpClientFactory, Microsoft.Extensions.Options.IOptions<OauthOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public abstract string ProviderName { get; }

    protected abstract ProviderOptions GetProviderOptions();

    public async Task<ExternalUserProfile> GetExternalUserAsync(string code, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExchangeCodeForUserAsync(code, cancellationToken);
        }
        catch (OauthException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new OauthException($"Could not reach the {ProviderName} OAuth service: {ex.Message}");
        }
        catch (JsonException ex)
        {
            throw new OauthException($"The {ProviderName} OAuth service returned an unexpected response: {ex.Message}");
        }
    }

    protected abstract Task<ExternalUserProfile> ExchangeCodeForUserAsync(string code, CancellationToken cancellationToken);

    protected void EnsureConfigured(ProviderOptions provider)
    {
        if (string.IsNullOrWhiteSpace(provider.ClientId) || string.IsNullOrWhiteSpace(provider.ClientSecret))
            throw new OauthException(
                $"The {ProviderName} OAuth application is not configured on the API. " +
                $"Set OAuth:{ProviderName}:ClientId and OAuth:{ProviderName}:ClientSecret in your configuration (or user secrets).");

        if (string.IsNullOrWhiteSpace(EffectiveRedirectUri(provider)))
            throw new OauthException(
                "OAuth:FrontendRedirectUri is not configured. It must be the frontend callback URL " +
                "(e.g. http://localhost:4200/auth/callback) that is also registered with the OAuth application.");
    }

    protected string EffectiveRedirectUri(ProviderOptions provider)
        => string.IsNullOrWhiteSpace(provider.RedirectUri) ? _options.FrontendRedirectUri : provider.RedirectUri;

    protected async Task<JsonDocument> PostFormAsync(HttpClient client, string url, IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await client.PostAsync(url, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new OauthException($"{ProviderName} token exchange failed with HTTP {(int)response.StatusCode}: {Truncate(body)}");
        return JsonDocument.Parse(body);
    }

    protected async Task<JsonDocument> GetJsonAsync(HttpClient client, string url, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new OauthException($"{ProviderName} profile request failed with HTTP {(int)response.StatusCode}: {Truncate(body)}");
        return JsonDocument.Parse(body);
    }

    protected static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
