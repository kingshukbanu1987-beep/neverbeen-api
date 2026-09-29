using System.Text.Json;

namespace NeverBeen.API.Services;

/// <summary>
/// Facebook OAuth (authorization code flow). The frontend redirects to
/// https://www.facebook.com/v19.0/dialog/oauth with scopes "email public_profile";
/// this provider exchanges the code and reads the profile from the Graph API.
/// </summary>
public class FacebookOauthLoginProvider : OauthLoginProviderBase
{
    public FacebookOauthLoginProvider(IHttpClientFactory httpClientFactory, Microsoft.Extensions.Options.IOptions<OauthOptions> options)
        : base(httpClientFactory, options)
    {
    }

    public override string ProviderName => "Facebook";

    protected override ProviderOptions GetProviderOptions() => _options.Facebook;

    protected override async Task<ExternalUserProfile> ExchangeCodeForUserAsync(string code, CancellationToken cancellationToken)
    {
        var provider = GetProviderOptions();
        EnsureConfigured(provider);

        var client = _httpClientFactory.CreateClient();

        using var tokenDoc = await PostFormAsync(client, "https://graph.facebook.com/v19.0/oauth/access_token", new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = provider.ClientId,
            ["client_secret"] = provider.ClientSecret,
            ["redirect_uri"] = EffectiveRedirectUri(provider),
            ["grant_type"] = "authorization_code"
        }, cancellationToken);

        var accessToken = GetString(tokenDoc.RootElement, "access_token")
            ?? throw new OauthException("Facebook token response did not include an access_token.");

        using var meDoc = await GetJsonAsync(
            client,
            "https://graph.facebook.com/v19.0/me?fields=id,name,email,first_name,last_name,picture.type(large)",
            accessToken,
            cancellationToken);

        var root = meDoc.RootElement;

        var providerKey = GetString(root, "id")
            ?? throw new OauthException("Facebook profile did not include a stable user id.");

        string? pictureUrl = null;
        if (root.TryGetProperty("picture", out var picture)
            && picture.ValueKind == JsonValueKind.Object
            && picture.TryGetProperty("data", out var pictureData))
        {
            pictureUrl = GetString(pictureData, "url");
        }

        return new ExternalUserProfile
        {
            ProviderKey = providerKey,
            Email = GetString(root, "email") ?? string.Empty,
            FullName = GetString(root, "name"),
            FirstName = GetString(root, "first_name"),
            LastName = GetString(root, "last_name"),
            PictureUrl = pictureUrl
        };
    }
}
