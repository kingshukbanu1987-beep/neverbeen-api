using System.Text.Json;

namespace NeverBeen.API.Services;

/// <summary>
/// Microsoft OAuth (authorization code flow) for Outlook / Microsoft 365 / personal
/// Microsoft accounts. The frontend redirects to
/// https://login.microsoftonline.com/common/oauth2/v2.0/authorize with scopes
/// "openid email profile User.Read"; this provider exchanges the code and reads the
/// profile from Microsoft Graph.
/// </summary>
public class MicrosoftOauthLoginProvider : OauthLoginProviderBase
{
    public MicrosoftOauthLoginProvider(IHttpClientFactory httpClientFactory, Microsoft.Extensions.Options.IOptions<OauthOptions> options)
        : base(httpClientFactory, options)
    {
    }

    public override string ProviderName => "Microsoft";

    protected override ProviderOptions GetProviderOptions() => _options.Microsoft;

    protected override async Task<ExternalUserProfile> ExchangeCodeForUserAsync(string code, CancellationToken cancellationToken)
    {
        var provider = GetProviderOptions();
        EnsureConfigured(provider);

        var client = _httpClientFactory.CreateClient();

        using var tokenDoc = await PostFormAsync(client, "https://login.microsoftonline.com/common/oauth2/v2.0/token", new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = provider.ClientId,
            ["client_secret"] = provider.ClientSecret,
            ["redirect_uri"] = EffectiveRedirectUri(provider),
            ["grant_type"] = "authorization_code",
            ["scope"] = "openid email profile User.Read"
        }, cancellationToken);

        var accessToken = GetString(tokenDoc.RootElement, "access_token")
            ?? throw new OauthException("Microsoft token response did not include an access_token.");

        using var meDoc = await GetJsonAsync(client, "https://graph.microsoft.com/v1.0/me", accessToken, cancellationToken);
        var root = meDoc.RootElement;

        var providerKey = GetString(root, "id")
            ?? throw new OauthException("Microsoft Graph profile did not include a stable user id.");

        return new ExternalUserProfile
        {
            ProviderKey = providerKey,
            Email = GetString(root, "mail")
                   ?? GetString(root, "userPrincipalName")
                   ?? GetString(root, "userPersonalEmail")
                   ?? string.Empty,
            FullName = GetString(root, "displayName"),
            FirstName = GetString(root, "givenName"),
            LastName = GetString(root, "surname"),
            PictureUrl = null // Graph photos require an extra authenticated request; the user uploads a photo on the registration page
        };
    }
}
