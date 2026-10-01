using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BuildWise.Api.Controllers;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace BuildWise.Api.Tests;

public class AuthUserValidationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private IConfiguration _config = null!;
    private readonly string _password = Guid.NewGuid().ToString("N");

    public async Task InitializeAsync()
    {
        var database = Guid.NewGuid().ToString();
        _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
            ["Jwt:Issuer"] = "AuthAudit", ["Jwt:Audience"] = "AuthAuditClients"
        }).Build();
        _host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddSingleton(_config);
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(database));
            services.AddScoped<AuthService>();
            services.AddSingleton<JwtTokenService>();
            services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(o => JwtAccountValidation.Configure(o, _config));
            services.AddAuthorization();
        }).Configure(app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(e => e.MapControllers());
        })).StartAsync();
        _client = _host.GetTestClient();
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        var admin = new User { Id = 77, FullName = "Admin", Email = "admin@example.test" };
        admin.UserRoles.Add(new UserRole { Role = await db.Roles.SingleAsync(r => r.Name == "Administrator") });
        db.Users.Add(admin);
        await db.SaveChangesAsync();
    }

    private string Token(string? id = "77", string role = "Administrator", string? fault = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (id is not null) claims.Add(new(ClaimTypes.NameIdentifier, id));
        if (fault == "conflicting-id") claims.Add(new(ClaimTypes.NameIdentifier, "999"));
        var key = fault == "signature" ? new string('x', 48) : _config["Jwt:Key"]!;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: fault == "issuer" ? "Wrong" : _config["Jwt:Issuer"],
            audience: fault == "audience" ? "Wrong" : _config["Jwt:Audience"], claims: claims,
            expires: DateTime.UtcNow.AddMinutes(fault == "expired" ? -5 : 30),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));
    }

    private void Authenticate(string? token = null) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token ?? Token());

    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/users")]
    public async Task Creation_rejects_invalid_fields_without_writes(string path)
    {
        Authenticate();
        foreach (var field in new[] { "fullName", "email", "password", "roleName" })
        foreach (var value in new string?[] { null, "", " " })
        {
            var body = new Dictionary<string, object?> { ["fullName"] = "New", ["email"] = "new@example.test", ["password"] = _password, ["roleName"] = "SiteEngineer" };
            body[field] = value;
            using var response = await _client.PostAsJsonAsync(path, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        foreach (var email in new[] { "invalid", "a@@example.test", new string('a', 256) + "@test.example" })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(path, new { fullName = "New", email, password = _password, roleName = "SiteEngineer" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(path, new { fullName = "New", email = "new@example.test", password = "short", roleName = "SiteEngineer" })).StatusCode);
        using var scope = _host.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid")]
    public async Task Login_rejects_missing_or_invalid_email(string? email)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/auth/login", new { email, password = _password })).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Login_requires_password(string? password)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/auth/login", new { email = "new@example.test", password })).StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/users")]
    public async Task Creation_accepts_existing_eight_character_password_policy(string path)
    {
        Authenticate();
        var password = Guid.NewGuid().ToString("N")[..8];
        var response = await _client.PostAsJsonAsync(path, new { fullName = "New", email = "new@example.test", password, roleName = "SiteEngineer" });
        Assert.Equal(path == "/api/users" ? HttpStatusCode.Created : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/auth/login", new { email = "new@example.test", password })).StatusCode);
    }

    [Theory]
    [InlineData("ProjectManager")]
    [InlineData("ReceivingOfficer")]
    [InlineData("Unknown")]
    public async Task Administrator_cannot_assign_unsupported_roles(string roleName)
    {
        Authenticate();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/users", new { fullName = "New", email = "new@example.test", password = _password, roleName })).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"isActive\":null}")]
    [InlineData("{\"isActive\":\"invalid\"}")]
    [InlineData("{\"isActive\":2}")]
    public async Task Status_requires_a_boolean(string json)
    {
        Authenticate();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PatchAsync("/api/users/77/status", new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void Jwt_configuration_rejects_missing_or_short_signing_keys(string? key)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = key }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => JwtAccountValidation.RequireSigningKey(config));
        Assert.Contains("Configure Jwt:Key", error.Message);
        Assert.Throws<InvalidOperationException>(() => JwtAccountValidation.Configure(new JwtBearerOptions(), config));
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("ProcurementManager")]
    [InlineData("ProcurementOfficer")]
    [InlineData("QualityInspector")]
    [InlineData("ProjectManager")]
    [InlineData("Unknown")]
    public async Task Public_registration_cannot_select_privileged_or_unknown_roles(string roleName)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new { fullName = "New", email = "new@example.test", password = _password, roleName });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/users")]
    public async Task Duplicate_email_is_case_and_whitespace_insensitive(string path)
    {
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Users.Add(new User { FullName = "Legacy", Email = " Legacy@Example.Test " });
            await db.SaveChangesAsync();
        }
        Authenticate();
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync(path, new { fullName = "New", email = "legacy@example.test", password = _password, roleName = "SiteEngineer" })).StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/users")]
    public async Task Allowed_role_must_exist_in_database(string path)
    {
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Roles.Remove(await db.Roles.SingleAsync(r => r.Name == "SiteEngineer"));
            await db.SaveChangesAsync();
        }
        Authenticate();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(path, new { fullName = "New", email = "new@example.test", password = _password, roleName = "SiteEngineer" })).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid")]
    [InlineData("99999")]
    public async Task Invalid_actor_cannot_create_users_or_change_status(string? actor)
    {
        Authenticate(Token(actor));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/users?userId=77", new { fullName = "New", email = "new@example.test", password = _password, roleName = "Administrator", userId = 77 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PatchAsJsonAsync("/api/users/77/status?userId=999", new { isActive = false, userId = 999, actorId = 999 })).StatusCode);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("conflicting-id")]
    public async Task Jwt_validation_rejects_invalid_tokens(string fault)
    {
        Authenticate(Token(fault: fault));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Self_deactivation_uses_JWT_actor_and_ignores_spoofed_fields()
    {
        Authenticate();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PatchAsJsonAsync("/api/users/77/status?userId=999", new { isActive = false, actorId = 999, userId = 999 })).StatusCode);
    }

    [Fact]
    public async Task Existing_token_is_rejected_after_deactivation_or_role_change()
    {
        Authenticate();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/users")).StatusCode);
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = await db.Users.Include(u => u.UserRoles).SingleAsync(u => u.Id == 77);
        admin.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PatchAsJsonAsync("/api/users/77/status", new { isActive = true })).StatusCode);
        admin.IsActive = true;
        db.UserRoles.RemoveRange(admin.UserRoles);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Signup_and_login_issue_authoritative_identity_without_passwords()
    {
        using var registered = await _client.PostAsJsonAsync("/api/auth/register", new { fullName = "New", email = " NEW@Example.Test ", password = _password, roleName = "SiteEngineer", roles = new[] { "Administrator" }, userId = 77, isActive = false });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var json = await registered.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_password, json);
        using var body = JsonDocument.Parse(json);
        var userId = body.RootElement.GetProperty("user").GetProperty("id").GetInt32();
        Assert.NotEqual(77, userId);
        var token = body.RootElement.GetProperty("token").GetString()!;
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();
        Assert.Contains(claims, c => c.Type == ClaimTypes.NameIdentifier && c.Value == userId.ToString());
        Assert.Equal("SiteEngineer", Assert.Single(claims, c => c.Type == ClaimTypes.Role).Value);
        Authenticate(token);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/auth/me")).StatusCode);
        foreach (var (method, path) in new[] { ("GET", "/api/users"), ("POST", "/api/users"), ("PATCH", "/api/users/77/status") })
            Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) })).StatusCode);
        using var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = " NEW@Example.Test ", password = _password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.DoesNotContain("password", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Authenticate();
        Assert.Equal(HttpStatusCode.OK, (await _client.PatchAsJsonAsync($"/api/users/{userId}/status", new { isActive = false })).StatusCode);
        Authenticate(token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/auth/login", new { email = "new@example.test", password = _password })).StatusCode);
    }

    [Fact]
    public async Task Seeding_preserves_deactivation_password_and_roles_for_existing_demo_email()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new User { FullName = "Public user", Email = " ADMIN@buildwise.demo ", IsActive = false, PasswordHash = "unchanged" };
        user.UserRoles.Add(new UserRole { Role = await db.Roles.SingleAsync(r => r.Name == "SiteEngineer") });
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await DbSeeder.SeedAsync(db);
        await DbSeeder.SeedAsync(db);
        Assert.False(user.IsActive);
        Assert.Equal("unchanged", user.PasswordHash);
        Assert.Equal("SiteEngineer", Assert.Single(user.UserRoles).Role.Name);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email.Trim().ToLower() == "admin@buildwise.demo"));
        Assert.DoesNotContain("PasswordHash", JsonSerializer.Serialize(user,
            new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles }));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}
