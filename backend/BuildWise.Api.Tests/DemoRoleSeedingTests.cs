using BuildWise.Api.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildWise.Api.Tests;

public class DemoRoleSeedingTests
{
    [Fact]
    public async Task Fresh_seed_assigns_only_five_roles_and_retains_legacy_role_rows()
    {
        await using var db = TestDbFactory.CreateInMemory();
        await DbSeeder.SeedAsync(db);
        var assignments = await db.UserRoles.Include(r => r.Role).Select(r => r.Role.Name).ToListAsync();
        Assert.Equal(new[] { "Administrator", "ProcurementManager", "ProcurementOfficer", "QualityInspector", "SiteEngineer" },
            assignments.OrderBy(r => r).ToArray());
        Assert.True(await db.Roles.AnyAsync(r => r.Name == "ProjectManager"));
        Assert.True(await db.Roles.AnyAsync(r => r.Name == "ReceivingOfficer"));
        await DbSeeder.SeedAsync(db);
        Assert.Equal(5, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Existing_database_safely_backfills_missing_quality_inspector_account()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var adminRole = await db.Roles.FirstAsync(r => r.Name == "Administrator");
        var adminUser = new BuildWise.Api.Models.Entities.User
        {
            FullName = "Ada Administrator",
            Email = "admin@buildwise.demo",
            PasswordHash = "PRE_EXISTING_HASH",
            IsActive = true
        };
        adminUser.UserRoles.Add(new BuildWise.Api.Models.Entities.UserRole { Role = adminRole });
        db.Users.Add(adminUser);
        await db.SaveChangesAsync();

        // Seed should add missing 4 demo accounts (including QualityInspector) without touching admin password hash
        await DbSeeder.SeedAsync(db);

        Assert.Equal(5, await db.Users.CountAsync());
        var inspector = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == "quality.inspector@buildwise.demo");
        Assert.NotNull(inspector);
        Assert.Contains(inspector.UserRoles, ur => ur.Role.Name == "QualityInspector");

        var reloadedAdmin = await db.Users.FirstAsync(u => u.Email == "admin@buildwise.demo");
        Assert.Equal("PRE_EXISTING_HASH", reloadedAdmin.PasswordHash);
    }
}
