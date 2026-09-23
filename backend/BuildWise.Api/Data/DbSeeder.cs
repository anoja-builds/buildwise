using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Data;

/// <summary>
/// Idempotent development seed data: demo login accounts for every role
/// relevant to Component 2, plus the spec's cement scenario (§11) — three
/// suppliers and one approved material request — so a fresh database is
/// immediately demo-ready (spec §6: "suitable seed data").
/// Safe to call on every startup; each block checks for existing rows first.
/// </summary>
public static class DbSeeder
{
    public const string DemoPassword = "Passw0rd!";

    public static async Task SeedAsync(ApplicationDbContext db)
    {
        await SeedUsersAsync(db);
        await SeedProcurementScenarioAsync(db);
    }

    private static async Task SeedUsersAsync(ApplicationDbContext db)
    {
        if (await db.Users.AnyAsync()) return;

        var roles = await db.Roles.ToDictionaryAsync(r => r.Name, r => r);
        var hasher = new PasswordHasher<User>();

        (string Name, string Email, string Role)[] demoAccounts =
        [
            ("Ada Administrator", "admin@buildwise.demo", "Administrator"),
            ("Sam SiteEngineer", "site.engineer@buildwise.demo", "SiteEngineer"),
            ("Priya Officer", "procurement.officer@buildwise.demo", "ProcurementOfficer"),
            ("Mira Manager", "procurement.manager@buildwise.demo", "ProcurementManager")
        ];

        foreach (var (name, email, roleName) in demoAccounts)
        {
            if (!roles.TryGetValue(roleName, out var role)) continue;

            var user = new User
            {
                FullName = name,
                Email = email,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, DemoPassword);
            user.UserRoles.Add(new UserRole { Role = role });

            db.Users.Add(user);
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedProcurementScenarioAsync(ApplicationDbContext db)
    {
        if (await db.Suppliers.AnyAsync()) return;

        var supplierA = new Supplier { Name = "Supplier A Building Materials", ContactPerson = "Nimal Perera", Email = "sales@suppliera.demo", Phone = "+94 11 234 5678", Address = "12 Galle Road, Colombo", Status = SupplierStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var supplierB = new Supplier { Name = "Supplier B Traders", ContactPerson = "Kamal Silva", Email = "info@supplierb.demo", Phone = "+94 11 345 6789", Address = "45 Kandy Road, Kandy", Status = SupplierStatus.Suspended, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var supplierC = new Supplier { Name = "Supplier C Wholesale", ContactPerson = "Anusha Fernando", Email = "quotes@supplierc.demo", Phone = "+94 11 456 7890", Address = "8 Negombo Road, Gampaha", Status = SupplierStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Suppliers.AddRange(supplierA, supplierB, supplierC);

        var project = new Project { Name = "Riverside Apartments — Block C", Location = "Colombo 05", Status = ProjectStatus.Active, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Projects.Add(project);

        var cement = new Material { Name = "Cement (50kg bag)", Unit = "bag", Category = "Structural Materials", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Materials.Add(cement);

        await db.SaveChangesAsync();

        var siteEngineer = await db.Users.FirstOrDefaultAsync(u => u.Email == "site.engineer@buildwise.demo");

        var request = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = siteEngineer?.Id ?? 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            Reason = "Foundation pour for Block C",
            Status = MaterialRequestStatus.Approved,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.MaterialRequests.Add(request);
        await db.SaveChangesAsync();

        db.MaterialRequestItems.Add(new MaterialRequestItem
        {
            MaterialRequestId = request.Id,
            MaterialId = cement.Id,
            RequestedQuantity = 250m,
            Notes = "Standard Portland cement grade 42.5N"
        });

        await db.SaveChangesAsync();
    }
}
