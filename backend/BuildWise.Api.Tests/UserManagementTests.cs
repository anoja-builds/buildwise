using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildWise.Api.Tests;

public partial class SecurityAuthorizationTests
{
    [Theory]
    [InlineData("SiteEngineer")]
    [InlineData("ProcurementOfficer")]
    [InlineData("ProcurementManager")]
    [InlineData("QualityInspector")]
    [InlineData("")]
    public async Task User_endpoints_require_administrator(string role)
    {
        if (role != "") SignIn(role);
        foreach (var (method, path) in new[] { ("GET", "/api/users"), ("POST", "/api/users"), ("PATCH", "/api/users/1/status") })
        {
            using var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });
            Assert.Equal(role == "" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("SiteEngineer")]
    [InlineData("ProcurementOfficer")]
    [InlineData("ProcurementManager")]
    [InlineData("QualityInspector")]
    [InlineData("Administrator")]
    public async Task Administrator_creates_lists_and_changes_status_with_normal_login(string roleName)
    {
        SignIn("Administrator");
        using var created = await _client.PostAsJsonAsync("/api/users", new { fullName = " New User ", email = " NEW@Example.test ", password = "Password123!", roleName });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var json = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(json);
        var id = body.RootElement.GetProperty("id").GetInt32();
        Assert.Equal("new@example.test", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("New User", body.RootElement.GetProperty("fullName").GetString());
        Assert.Equal(roleName, body.RootElement.GetProperty("roles")[0].GetString());
        using var list = await _client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain("password", await list.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        using var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "new@example.test", password = "Password123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        foreach (var isActive in new[] { false, true })
        {
            var before = DateTime.UtcNow;
            using var status = await _client.PatchAsJsonAsync($"/api/users/{id}/status", new { isActive });
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            Assert.DoesNotContain("password", await status.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            using var scope = _host.Services.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.FindAsync(id);
            Assert.Equal(isActive, user!.IsActive);
            Assert.True(user.UpdatedAt >= before);
            using var nextLogin = await _client.PostAsJsonAsync("/api/auth/login", new { email = "new@example.test", password = "Password123!" });
            Assert.Equal(isActive ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, nextLogin.StatusCode);
        }
        using var duplicate = await _client.PostAsJsonAsync("/api/users", new { fullName = "Duplicate", email = " NEW@example.test ", password = "Password123!", roleName });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Theory]
    [InlineData("Name", "valid@example.test", "Password123", "Owner")]
    [InlineData("Name", "valid@example.test", "short", "SiteEngineer")]
    [InlineData(" ", "valid@example.test", "Password123", "SiteEngineer")]
    [InlineData("Name", " ", "Password123", "SiteEngineer")]
    [InlineData("Name", "invalid", "Password123", "SiteEngineer")]
    public async Task Invalid_user_input_is_rejected(string fullName, string email, string password, string roleName)
    {
        SignIn("Administrator");
        using var response = await _client.PostAsJsonAsync("/api/users", new { fullName, email, password, roleName });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Status_rejects_self_deactivation_invalid_ids_and_missing_status()
    {
        SignIn("Administrator");
        foreach (var id in new[] { 0, -1, 77 })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PatchAsJsonAsync($"/api/users/{id}/status", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PatchAsJsonAsync("/api/users/99999/status", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PatchAsJsonAsync("/api/users/77/status", new { })).StatusCode);
    }

    [Fact]
    public async Task Catalogue_seeds_with_existing_suppliers_and_options_return_active_materials()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.Suppliers.AnyAsync());
        db.Materials.Add(new Material { Name = " sand ", Unit = "custom", IsActive = false });
        await db.SaveChangesAsync();
        await DbSeeder.SeedAsync(db);
        var count = await db.Materials.CountAsync();
        await DbSeeder.SeedAsync(db);
        Assert.Equal(count, await db.Materials.CountAsync());
        Assert.Equal(13, count); // Twelve catalogue rows plus the existing scenario material.
        var sand = await db.Materials.SingleAsync(m => m.Name == " sand ");
        Assert.False(sand.IsActive);
        Assert.Equal("custom", sand.Unit);
        SignIn("SiteEngineer");
        using var response = await _client.GetAsync("/api/material-requests/options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var options = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(12, options.RootElement.GetProperty("materials").GetArrayLength());
        Assert.DoesNotContain(options.RootElement.GetProperty("materials").EnumerateArray(), m => m.GetProperty("id").GetInt32() == sand.Id);
    }
}
