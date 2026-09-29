using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;
using NeverBeen.API.Services;

namespace NeverBeen.API.Controllers;

/// <summary>
/// OAuth (SSO) entry point. The Angular "Connect to NeverBeen Community" page redirects
/// the user to Google / Facebook / Microsoft; after consent the provider redirects back
/// to the frontend with an authorization "code", which the frontend sends here.
///
/// The API then:
///  1. exchanges the code for an access token (client secret stays on the server),
///  2. reads the external account (email, name, picture),
///  3. looks the user up by email / provider key - creating a "Pending" user when new,
///  4. returns a JWT plus flags telling the frontend where to navigate:
///       - new / pending user  -> Registration Page
///       - existing active user -> User Profile (home) page
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly IReadOnlyList<IOauthLoginProvider> _providers;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext db,
        JwtTokenService jwt,
        IEnumerable<IOauthLoginProvider> providers,
        ILogger<AuthController> logger)
    {
        _db = db;
        _jwt = jwt;
        _providers = providers.ToList();
        _logger = logger;
    }

    /// <summary>Exchanges an OAuth authorization code and returns a JWT for the (possibly new) user.</summary>
    [HttpPost("oauth/login")]
    [ProducesResponseType(typeof(AuthResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResultDto>> OauthLogin([FromBody] OAuthLoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { error = "Both 'provider' and 'code' are required." });

        var provider = _providers
            .FirstOrDefault(p => p.ProviderName.Equals(request.Provider.Trim(), StringComparison.OrdinalIgnoreCase));
        if (provider == null)
            return BadRequest(new
            {
                error = $"Unsupported OAuth provider '{request.Provider}'. Supported providers: {string.Join(", ", _providers.Select(p => p.ProviderName))}."
            });

        ExternalUserProfile external;
        try
        {
            external = await provider.GetExternalUserAsync(request.Code.Trim(), cancellationToken);
        }
        catch (OauthException ex)
        {
            _logger.LogWarning(ex, "OAuth login failed for provider {Provider}", request.Provider);
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected OAuth failure for provider {Provider}", request.Provider);
            return StatusCode(502, new { error = "Unexpected error while contacting the OAuth provider." });
        }

        if (string.IsNullOrWhiteSpace(external.Email))
            return Unauthorized(new
            {
                error = "The OAuth account did not provide an email address. Make sure the OAuth application has the 'email' scope enabled and the user granted it on the consent screen."
            });

        var email = external.Email.Trim();

        var user = await _db.Users
            .Include(u => u.Identities)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);

        if (user == null)
        {
            // The email on the provider account may have changed - fall back to the stable provider key.
            var identityByProviderKey = await _db.ExternalIdentities
                .FirstOrDefaultAsync(i => i.Provider == provider.ProviderName && i.ProviderKey == external.ProviderKey, cancellationToken);
            if (identityByProviderKey != null)
                user = await _db.Users
                    .Include(u => u.Identities)
                    .FirstOrDefaultAsync(u => u.Id == identityByProviderKey.UserId, cancellationToken);
        }

        var isNewUser = user == null;

        if (isNewUser)
        {
            user = new UserProfile
            {
                Email = email,
                FullName = string.IsNullOrWhiteSpace(external.FullName)
                    ? email.Split('@')[0]
                    : external.FullName.Trim(),
                ExternalProfilePictureUrl = external.PictureUrl,
                Status = UserProfileStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            user.Identities.Add(new ExternalIdentity
            {
                Provider = provider.ProviderName,
                ProviderKey = external.ProviderKey,
                Email = email,
                CreatedAtUtc = DateTime.UtcNow
            });
            _db.Users.Add(user);
        }
        else
        {
            var existingIdentity = user.Identities
                .FirstOrDefault(i => i.Provider == provider.ProviderName);
            if (existingIdentity != null)
            {
                existingIdentity.ProviderKey = external.ProviderKey;
                existingIdentity.Email = email;
            }
            else
            {
                // Same email, a different provider this time - link the new identity to the same account.
                user.Identities.Add(new ExternalIdentity
                {
                    Provider = provider.ProviderName,
                    ProviderKey = external.ProviderKey,
                    Email = email,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            if (user.Email != email)
                user.Email = email;

            user.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var (token, expiresIn) = _jwt.CreateToken(user);

        return Ok(new AuthResultDto
        {
            Token = token,
            ExpiresIn = expiresIn,
            IsNewUser = isNewUser,
            ProfileComplete = user.Status == UserProfileStatus.Active,
            Message = isNewUser
                ? "New NeverBeen community member. Please complete your registration."
                : "Welcome back to the NeverBeen community.",
            User = MapCurrentUser(user)
        });
    }

    /// <summary>Returns the signed-in user from the JWT (used by the Angular app to restore sessions).</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == User.GetUserId(), cancellationToken);
        if (user == null)
            return NotFound();

        return Ok(MapCurrentUser(user));
    }

    private static CurrentUserDto MapCurrentUser(UserProfile user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        Status = user.Status,
        ProfileComplete = user.Status == UserProfileStatus.Active,
        ProfilePhotoUrl = user.ProfilePhotoData != null
            ? $"/api/profile/{user.Id}/photo"
            : user.ExternalProfilePictureUrl
    };
}
