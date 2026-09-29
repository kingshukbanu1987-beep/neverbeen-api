namespace NeverBeen.API.Services;

/// <summary>Binds the "OAuth" section of appsettings.json.</summary>
public class OauthOptions
{
    /// <summary>
    /// The frontend callback URL the Angular app redirects back to after the provider
    /// consent screen, e.g. "http://localhost:4200/auth/callback". This is the exact
    /// redirect URI that must be registered with each OAuth application AND the one
    /// sent back to the provider during the code exchange.
    /// </summary>
    public string FrontendRedirectUri { get; set; } = string.Empty;

    public ProviderOptions Google { get; set; } = new();
    public ProviderOptions Facebook { get; set; } = new();
    public ProviderOptions Microsoft { get; set; } = new();
}

public class ProviderOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Defaults to <see cref="OauthOptions.FrontendRedirectUri"/> when empty.</summary>
    public string RedirectUri { get; set; } = string.Empty;
}
