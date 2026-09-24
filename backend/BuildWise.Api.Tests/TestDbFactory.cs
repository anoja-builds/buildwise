using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Tests;

public record StandardScenarioEntities(
    Project Project,
    Material Material,
    MaterialRequest Request,
    MaterialRequestItem RequestItem
);

public static class TestDbFactory
{
    public static ApplicationDbContext CreateInMemory(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static async Task<StandardScenarioEntities> SeedStandardScenarioDataAsync(ApplicationDbContext db)
    {
        var project = new Project
        {
            Name = "City Center Commercial Complex",
            Location = "Downtown Sector 4",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Projects.Add(project);

        var cement = new Material
        {
            Name = "Portland Composite Cement (50kg)",
            Unit = "Bags",
            Category = "Structural Materials",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Materials.Add(cement);
        await db.SaveChangesAsync();

        var request = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            Reason = "Foundation slab casting phase 2",
            Status = MaterialRequestStatus.Approved,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.MaterialRequests.Add(request);
        await db.SaveChangesAsync();

        var requestItem = new MaterialRequestItem
        {
            MaterialRequestId = request.Id,
            MaterialId = cement.Id,
            RequestedQuantity = 250.0m,
            Notes = "Standard Portland cement grade 42.5N"
        };
        db.MaterialRequestItems.Add(requestItem);
        await db.SaveChangesAsync();

        return new StandardScenarioEntities(project, cement, request, requestItem);
    }
}
