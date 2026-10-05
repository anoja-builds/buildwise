using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using BuildWise.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace BuildWise.Api.Tests;

/// <summary>
/// Boots the real <c>Program.cs</c> pipeline (JWT bearer authentication, the
/// registered authorization policies and the authorization middleware) against
/// an in-memory database, so RBAC assertions exercise production configuration
/// rather than a hand-rolled replica of it.
/// </summary>
public class RbacApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Fixed 64-char key so tokens are deterministic across the suite.</summary>
    public const string TestJwtKey = "buildwise-rbac-test-signing-key-0123456789-abcdefghijklmnop";

    private readonly string _dbName = $"rbac-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);

        // Program.cs validates these during host construction — before
        // ConfigureServices runs — so they must be supplied as host settings.
        // The connection string is never used: the DbContext is replaced with
        // the in-memory provider below.
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=buildwise_rbac_tests");
        builder.UseSetting("Jwt:Key", TestJwtKey);
        builder.UseSetting("Jwt:Issuer", "BuildWise");
        builder.UseSetting("Jwt:Audience", "BuildWiseClients");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=buildwise_rbac_tests",
                ["Jwt:Key"] = TestJwtKey,
                ["Jwt:Issuer"] = "BuildWise",
                ["Jwt:Audience"] = "BuildWiseClients",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Swap Npgsql for the in-memory provider. The rest of the container —
            // including AddAuthorization and every registered policy — is untouched.
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            // Keep test output readable; the pipeline is not the subject here.
            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        });
    }

    /// <summary>Applies the model configuration (roles, indexes) to the in-memory store.</summary>
    public async Task<ApplicationDbContext> GetSeededDbAsync()
    {
        var db = Services.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    /// <summary>Builds a client authenticated as the given roles.</summary>
    public HttpClient CreateClientFor(params string[] roles) => CreateClientForUser(1, roles);

    /// <summary>
    /// Builds a client whose token identifies a specific user id, so tests can
    /// distinguish "the caller raised this request" from "a colleague did".
    /// </summary>
    public HttpClient CreateClientForUser(int userId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BuildToken(roles, null, userId));
        return client;
    }

    /// <summary>
    /// Builds a client for a supplier portal user, signing the supplier id into
    /// the token exactly as <c>JwtTokenService</c> does in production.
    /// </summary>
    public HttpClient CreateSupplierClient(int supplierId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BuildToken(new[] { "Supplier" }, supplierId));
        return client;
    }

    private string BuildToken(string[] roles, int? supplierId = null, int userId = 1)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, "rbac.tester@buildwise.test"),
            new(ClaimTypes.Email, "rbac.tester@buildwise.test")
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        if (supplierId is > 0) claims.Add(new Claim("supplier_id", supplierId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var token = new JwtSecurityToken(
            issuer: "BuildWise",
            audience: "BuildWiseClients",
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}