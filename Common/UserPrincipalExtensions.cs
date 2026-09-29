using System.Security.Claims;

namespace NeverBeen.API.Common;

public static class UserPrincipalExtensions
{
    /// <summary>Reads the user id claim written by <c>JwtTokenService</c>.</summary>
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The authenticated request does not carry a user identifier claim.");
        return int.Parse(raw);
    }
}
