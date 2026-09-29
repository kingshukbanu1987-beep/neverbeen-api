using System.ComponentModel.DataAnnotations;

namespace NeverBeen.API.Dtos;

/// <summary>
/// Body of POST /api/auth/oauth/login. The Angular app starts the OAuth flow with the
/// provider (redirecting to the frontend callback URL) and sends the authorization
/// <c>code</c> here; the API exchanges it for a token using the shared client secret.
/// </summary>
public class OAuthLoginRequest
{
    /// <summary>"Google", "Facebook" or "Microsoft".</summary>
    [Required]
    [MaxLength(20)]
    public string Provider { get; set; } = string.Empty;

    [Required]
    [MinLength(5)]
    public string Code { get; set; } = string.Empty;
}

public class AuthResultDto
{
    public string Token { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public long ExpiresIn { get; set; }

    /// <summary>True when this email/key was not a NeverBeen member before this login.</summary>
    public bool IsNewUser { get; set; }

    /// <summary>False when the user must complete the registration page first.</summary>
    public bool ProfileComplete { get; set; }

    public string Message { get; set; } = string.Empty;
    public CurrentUserDto User { get; set; } = new();
}

public class CurrentUserDto
{
    public int Id { get; set; }
    public string? FullName { get; set; }
    public string Email { get; set; } = string.Empty;

    /// <summary>"Pending" (registration not finished) or "Active".</summary>
    public string Status { get; set; } = string.Empty;
    public bool ProfileComplete { get; set; }

    /// <summary>Relative API URL of the uploaded profile picture, or the OAuth provider picture URL as fallback.</summary>
    public string? ProfilePhotoUrl { get; set; }
}
