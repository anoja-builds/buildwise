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
    public static string DemoPassword { get; } = Environment.GetEnvironmentVariable("BUILDWISE_DEMO_PASSWORD")
        ?? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public static async Task SeedAsync(ApplicationDbContext db)
    {
        await SeedUsersAsync(db);
        await SeedMaterialsAsync(db);
        await SeedProcurementScenarioAsync(db);
    }

    private static async Task SeedUsersAsync(ApplicationDbContext db)
    {
        if (string.IsNullOrWhiteSpace(DemoPassword) || DemoPassword.Length < 8)
            throw new InvalidOperationException("BUILDWISE_DEMO_PASSWORD must contain at least 8 characters.");

        var roles = await db.Roles.ToDictionaryAsync(r => r.Name, r => r);
        var hasher = new PasswordHasher<User>();

        (string Name, string Email, string Role)[] demoAccounts =
        [
            ("Ada Administrator", "admin@buildwise.demo", "Administrator"),
            ("Sam SiteEngineer", "site.engineer@buildwise.demo", "SiteEngineer"),
            ("Priya Officer", "procurement.officer@buildwise.demo", "ProcurementOfficer"),
            ("Mira Manager", "procurement.manager@buildwise.demo", "ProcurementManager"),
            ("Quinn Inspector", "quality.inspector@buildwise.demo", "QualityInspector")
        ];

        foreach (var (name, email, roleName) in demoAccounts)
        {
            if (!roles.TryGetValue(roleName, out var role)) continue;

            var existingUser = await db.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Email.Trim().ToLower() == email);

            if (existingUser == null)
            {
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
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedMaterialsAsync(ApplicationDbContext db)
    {
        (string Name, string Unit, string Category)[] catalogue =
        [
            ("Cement (50kg bag)", "bag", "Structural Materials"),
            ("Sand", "m3", "Aggregate"),
            ("Crushed Aggregate", "m3", "Aggregate"),
            ("Reinforcement Steel Bar", "kg", "Structural Materials"),
            ("Concrete Block", "unit", "Masonry"),
            ("Clay Brick", "unit", "Masonry"),
            ("Timber", "m", "Carpentry"),
            ("Plywood Sheet", "sheet", "Carpentry"),
            ("PVC Pipe", "m", "Plumbing"),
            ("Electrical Cable", "m", "Electrical"),
            ("Interior Paint", "litre", "Finishes"),
            ("Ceramic Tile", "m2", "Finishes")
        ];
        var names = (await db.Materials.Select(m => m.Name).ToListAsync())
            .Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, unit, category) in catalogue)
        {
            // Preserve existing IDs, custom values, and intentional deactivation.
            if (!names.Add(name)) continue;
            db.Materials.Add(new Material { Name = name, Unit = unit, Category = category, IsActive = true });
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

        var cement = (await db.Materials.ToListAsync()).First(m =>
            string.Equals(m.Name.Trim(), "Cement (50kg bag)", StringComparison.OrdinalIgnoreCase));

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
