using System.Text.Json;

namespace NeverBeen.API.Services;

/// <summary>
/// Google OAuth (authorization code flow). The frontend redirects to
/// https://accounts.google.com/o/oauth2/v2/auth with scopes "openid email profile";
/// this provider exchanges the code and reads the user info from the OpenID endpoint.
/// </summary>
public class GoogleOauthLoginProvider : OauthLoginProviderBase
{
    public GoogleOauthLoginProvider(IHttpClientFactory httpClientFactory, Microsoft.Extensions.Options.IOptions<OauthOptions> options)
        : base(httpClientFactory, options)
    {
    }

    public override string ProviderName => "Google";

    protected override ProviderOptions GetProviderOptions() => _options.Google;

    protected override async Task<ExternalUserProfile> ExchangeCodeForUserAsync(string code, CancellationToken cancellationToken)
    {
        var provider = GetProviderOptions();
        EnsureConfigured(provider);

        var client = _httpClientFactory.CreateClient();

        using var tokenDoc = await PostFormAsync(client, "https://oauth2.googleapis.com/token", new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = provider.ClientId,
            ["client_secret"] = provider.ClientSecret,
            ["redirect_uri"] = EffectiveRedirectUri(provider),
            ["grant_type"] = "authorization_code"
        }, cancellationToken);

        var accessToken = GetString(tokenDoc.RootElement, "access_token")
            ?? throw new OauthException("Google token response did not include an access_token.");

        using var userDoc = await GetJsonAsync(client, "https://openidconnect.googleapis.com/v1/userinfo", accessToken, cancellationToken);
        var root = userDoc.RootElement;

        var providerKey = GetString(root, "sub")
            ?? throw new OauthException("Google profile did not include a stable user id (\"sub\").");

        return new ExternalUserProfile
        {
            ProviderKey = providerKey,
            Email = GetString(root, "email") ?? string.Empty,
            FullName = GetString(root, "name"),
            FirstName = GetString(root, "given_name"),
            LastName = GetString(root, "family_name"),
            PictureUrl = GetString(root, "picture"),
            EmailVerified = root.TryGetProperty("email_verified", out var emailVerified) && emailVerified.ValueKind == JsonValueKind.True
        };
    }
}
