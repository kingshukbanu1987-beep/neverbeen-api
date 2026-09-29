using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Services;

/// <summary>Creates the JWT returned after an OAuth login; the Angular app stores it and sends it as a Bearer token.</summary>
public class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string Token, long ExpiresInSeconds) CreateToken(UserProfile user)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_configuration["Jwt:SigningKey"] ?? string.Empty);
        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

        var expiresHours = ParseExpiresHours(_configuration["Jwt:ExpiresInHours"]);
        var notBefore = DateTime.UtcNow;
        var expires = notBefore.AddHours(expiresHours);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName ?? user.Email),
            new("nb_status", user.Status)
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "NeverBeen.API",
            audience: _configuration["Jwt:Audience"] ?? "NeverBeen.Web",
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: signingCredentials);

        var handler = new JwtSecurityTokenHandler();
        return (handler.WriteToken(token), (long)expiresHours * 3600);
    }

    private static double ParseExpiresHours(string? raw)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) && hours > 0)
            return hours;
        return 72;
    }
}
