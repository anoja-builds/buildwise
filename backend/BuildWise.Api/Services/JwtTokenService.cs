using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BuildWise.Api.Models.Entities;
using Microsoft.IdentityModel.Tokens;

namespace BuildWise.Api.Services;

/// <summary>
/// Issues and validates the shared JWT used by every component's controllers,
/// React and Flutter. Signing key, issuer and audience come from configuration
/// (see appsettings.json "Jwt" section) — never hard-coded.
/// </summary>
public class JwtTokenService
{
    private readonly IConfiguration _config;

    public JwtTokenService(IConfiguration config)
    {
        _config = config;
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(User user, IEnumerable<string> roles)
    {
        var jwtSection = _config.GetSection("Jwt");
        var key = jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var issuer = jwtSection["Issuer"] ?? "BuildWise";
        var audience = jwtSection["Audience"] ?? "BuildWiseClients";
        var expiryMinutes = int.TryParse(jwtSection["ExpiryMinutes"], out var m) ? m : 480;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
