using System.Security.Claims;
using System.Text;
using BuildWise.Api.Controllers;
using BuildWise.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BuildWise.Api.Services;

public static class JwtAccountValidation
{
    public static string RequireSigningKey(IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Configure Jwt:Key with a signing key of at least 32 bytes using user-secrets or environment variables.");

        return key;
    }

    public static void Configure(JwtBearerOptions options, IConfiguration configuration)
    {
        var key = RequireSigningKey(configuration);

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = configuration["Jwt:Issuer"] ?? "BuildWise",
            ValidAudience = configuration["Jwt:Audience"] ?? "BuildWiseClients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents { OnTokenValidated = ValidateAsync };
    }

    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null || !principal.TryGetUserId(out var actorId)
            || principal.FindAll(ClaimTypes.NameIdentifier).Any(c => !int.TryParse(c.Value, out var id) || id != actorId))
        {
            context.Fail("Invalid user identity.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.AsNoTracking().Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .SingleOrDefaultAsync(u => u.Id == actorId, context.HttpContext.RequestAborted);
        if (user is null || !user.IsActive
            || !principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet(StringComparer.Ordinal)
                .SetEquals(user.UserRoles.Select(ur => ur.Role.Name)))
            context.Fail("Account access has changed. Sign in again.");
    }
}
