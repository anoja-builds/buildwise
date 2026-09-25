using BuildWise.Api.DTOs;
using BuildWise.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BuildWise.Api.Tests;

public class AuthServiceTests
{
    private static AuthService CreateAuthService(out Data.ApplicationDbContext db)
    {
        db = TestDbFactory.CreateInMemory();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "unit-test-signing-key-at-least-32-characters-long",
                ["Jwt:Issuer"] = "BuildWiseTests",
                ["Jwt:Audience"] = "BuildWiseTestsClients",
                ["Jwt:ExpiryMinutes"] = "60"
            })
            .Build();

        var tokenService = new JwtTokenService(config);
        return new AuthService(db, tokenService);
    }

    [Fact]
    public async Task Register_CreatesUser_HashesPassword_And_ReturnsToken()
    {
        var authService = CreateAuthService(out var db);

        var response = await authService.RegisterAsync(new RegisterRequestDto(
            "Priya Officer", "priya@buildwise.test", "SuperSecret1", "ProcurementOfficer"));

        Assert.False(string.IsNullOrWhiteSpace(response.Token));
        Assert.Equal("priya@buildwise.test", response.User.Email);
        Assert.Contains("ProcurementOfficer", response.User.Roles);

        var stored = db.Users.Single(u => u.Email == "priya@buildwise.test");
        Assert.NotEqual("SuperSecret1", stored.PasswordHash);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Throws()
    {
        var authService = CreateAuthService(out _);
        await authService.RegisterAsync(new RegisterRequestDto("A", "dup@buildwise.test", "SuperSecret1", "ProcurementOfficer"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authService.RegisterAsync(new RegisterRequestDto("B", "dup@buildwise.test", "AnotherPass1", "ProcurementManager")));
    }

    [Fact]
    public async Task Register_UnknownRole_Throws()
    {
        var authService = CreateAuthService(out _);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.RegisterAsync(new RegisterRequestDto("C", "c@buildwise.test", "SuperSecret1", "NotARealRole")));
    }

    [Fact]
    public async Task Register_PrivilegedSelfRegistration_IsBlocked()
    {
        var authService = CreateAuthService(out _);
        foreach (var role in new[] { "Administrator", "ProcurementManager", "SiteManager" })
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => authService.RegisterAsync(
                new RegisterRequestDto("Privileged User", $"{role}@buildwise.test", "SuperSecret1", role)));
            Assert.Contains("existing administrator", ex.Message);
        }
    }

    [Fact]
    public async Task Login_CorrectPassword_Succeeds()
    {
        var authService = CreateAuthService(out var db);
        await Data.DbSeeder.SeedAsync(db);

        var response = await authService.LoginAsync(new LoginRequestDto("procurement.manager@buildwise.demo", Data.DbSeeder.DemoPassword));

        Assert.False(string.IsNullOrWhiteSpace(response.Token));
        Assert.Contains("ProcurementManager", response.User.Roles);
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsUnauthorized()
    {
        var authService = CreateAuthService(out var db);
        await Data.DbSeeder.SeedAsync(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequestDto("procurement.manager@buildwise.demo", "WrongPassword")));
    }

    [Fact]
    public async Task Login_UnknownEmail_ThrowsUnauthorized_NotFound()
    {
        // Deliberately the same exception/message as a wrong password, so the
        // API never reveals whether an email is registered (user enumeration).
        var authService = CreateAuthService(out _);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequestDto("nobody@buildwise.test", "WhateverPass1")));
    }
}
