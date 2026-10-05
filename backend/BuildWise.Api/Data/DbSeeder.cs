using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Security;
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
        await SeedSupplierPortalAccountsAsync(db);
    }

    /// <summary>
    /// Creates one Supplier portal login per Active scenario supplier.
    /// <para>
    /// These accounts are what make the "Supplier quotation" step of the
    /// end-to-end scenario a real actor. Each login is bound to exactly one
    /// supplier via <c>User.SupplierId</c>, which the API signs into the JWT so
    /// the portal can scope every query server-side. Passwords use the same
    /// demo password as the staff accounts.
    /// </para>
    /// </summary>
    private static async Task SeedSupplierPortalAccountsAsync(ApplicationDbContext db)
    {
        if (!await db.Roles.AnyAsync(r => r.Name == Roles.Supplier)) return;

        var supplierRole = await db.Roles.FirstAsync(r => r.Name == Roles.Supplier);
        var hasher = new PasswordHasher<User>();
        var activeSuppliers = await db.Suppliers
            .Where(s => s.Status == SupplierStatus.Active)
            .OrderBy(s => s.Id)
            .ToListAsync();

        foreach (var supplier in activeSuppliers)
        {
            var email = BuildSupplierPortalEmail(supplier);
            if (await db.Users.AnyAsync(u => u.Email == email)) continue;

            // Never leave a second login silently bound to the same supplier.
            if (await db.Users.AnyAsync(u => u.SupplierId == supplier.Id)) continue;

            var user = new User
            {
                FullName = $"{supplier.ContactPerson ?? supplier.Name} (Portal)",
                Email = email,
                SupplierId = supplier.Id,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, DemoPassword);
            user.UserRoles.Add(new UserRole { Role = supplierRole });
            db.Users.Add(user);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Derives a stable portal login from the supplier's own registered email
    /// address, falling back to a slug of its name. Keeps demo credentials
    /// predictable without hard-coding one account per supplier.
    /// </summary>
    private static string BuildSupplierPortalEmail(Supplier supplier)
    {
        var source = !string.IsNullOrWhiteSpace(supplier.Email)
            ? supplier.Email
            : $"{supplier.Name}@supplier.invalid";

        var local = source.Split('@')[0].Trim().ToLowerInvariant();
        var cleaned = new string(local.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = $"supplier{supplier.Id}";

        return $"{cleaned}@portal.buildwise.demo";
    }

    private static async Task SeedUsersAsync(ApplicationDbContext db)
    {
        var requiredRoleNames = new[]
        {
            Roles.Administrator, Roles.SiteEngineer, Roles.ProjectManager, Roles.ProcurementOfficer,
            Roles.ProcurementManager, Roles.ReceivingOfficer, Roles.QualityInspector, Roles.SiteOfficer,
            Roles.SiteManager, Roles.Supplier
        };
        var existingRoleNames = await db.Roles.Select(role => role.Name).ToListAsync();
        foreach (var roleName in requiredRoleNames.Where(name => !existingRoleNames.Contains(name)))
        {
            db.Roles.Add(new Role { Name = roleName });
        }
        await db.SaveChangesAsync();

        var roles = await db.Roles.ToDictionaryAsync(r => r.Name, r => r);
        var hasher = new PasswordHasher<User>();

        (string Name, string Email, string Role)[] demoAccounts =
        [
            ("Ada Administrator", "admin@buildwise.demo", Roles.Administrator),
            ("Sam SiteEngineer", "site.engineer@buildwise.demo", Roles.SiteEngineer),
            ("Nipuni SiteOfficer", "site.officer@buildwise.demo", Roles.SiteOfficer),
            ("Ravi Receiver", "receiving.officer@buildwise.demo", Roles.ReceivingOfficer),
            ("Priya Officer", "procurement.officer@buildwise.demo", Roles.ProcurementOfficer),
            ("Mira Manager", "procurement.manager@buildwise.demo", Roles.ProcurementManager),
            ("Nimal Site Manager", "site.manager@buildwise.demo", Roles.SiteManager),
            ("Dinesh Inspector", "quality.inspector@buildwise.demo", Roles.QualityInspector),
        ];

        foreach (var (name, email, roleName) in demoAccounts)
        {
            if (await db.Users.AnyAsync(user => user.Email == email)) continue;

            // SiteOfficer is an alias row for the same SiteEngineer permission
            // set — grant both rows so either JWT role claim passes [Authorize].
            var roleNames = roleName == "SiteEngineer"
                ? new[] { "SiteEngineer", "SiteOfficer" }
                : new[] { roleName };
            UserRole? roleLink = null;
            foreach (var rn in roleNames)
            {
                if (!roles.TryGetValue(rn, out var role)) continue;
                roleLink = new UserRole { Role = role };
                break;
            }
            if (roleLink is null) continue;

            var user = new User
            {
                FullName = name,
                Email = email,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, DemoPassword);
            foreach (var rn in roleNames)
            {
                if (!roles.TryGetValue(rn, out var linkedRole)) continue;
                user.UserRoles.Add(new UserRole { Role = linkedRole });
            }
            if (user.UserRoles.Count == 0) continue;

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

        var project = new Project { Name = "Riverside Apartments — Block C", Location = "Colombo 05", Status = ProjectStatus.Active, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)), MaterialBudgetAmount = 5_000_000m, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
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
