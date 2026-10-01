using System.Security.Claims;
using BuildWise.Api.Controllers;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildWise.Api.Tests;

public class MaterialRequestValidationTests
{
    private static MaterialRequestsController CreateController(ApplicationDbContext db, int userId, string role)
    {
        var planningService = new ProcurementPlanningAgentService(db);
        var controller = new MaterialRequestsController(db, planningService);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };
        return controller;
    }

    private static async Task<(Project Project, Material MaterialActive, Material MaterialInactive, User Engineer, User Manager)> SeedBaseData(ApplicationDbContext db)
    {
        var engineerRole = await db.Roles.FirstAsync(r => r.Name == "SiteEngineer");
        var managerRole = await db.Roles.FirstAsync(r => r.Name == "ProcurementManager");

        var engineer = new User { Id = 101, FullName = "Site Engineer 1", Email = "eng1@buildwise.demo" };
        var manager = new User { Id = 102, FullName = "Proc Manager 1", Email = "mgr1@buildwise.demo" };
        engineer.UserRoles.Add(new UserRole { RoleId = engineerRole.Id });
        manager.UserRoles.Add(new UserRole { RoleId = managerRole.Id });
        db.Users.AddRange(engineer, manager);

        var project = new Project { Id = 201, Name = "Active Construction Project", Status = ProjectStatus.Active };
        var matActive = new Material { Id = 301, Name = "Structural Steel (10mm)", Unit = "Ton", IsActive = true };
        var matInactive = new Material { Id = 302, Name = "Old Cement Type", Unit = "Bag", IsActive = false };

        db.Projects.Add(project);
        db.Materials.AddRange(matActive, matInactive);
        await db.SaveChangesAsync();

        return (project, matActive, matInactive, engineer, manager);
    }

    // -------------------------------------------------------------
    // CREATE REQUEST VALIDATIONS
    // -------------------------------------------------------------

    [Fact]
    public async Task CreateRequest_Rejects_When_Items_Empty()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, _, _, engineer, _) = await SeedBaseData(db);
        var controller = CreateController(db, engineer.Id, "SiteEngineer");

        var dto = new CreateMaterialRequestDto
        {
            ProjectId = project.Id,
            RequiredDate = DateTime.UtcNow.AddDays(7),
            Reason = "No items",
            Items = new List<CreateMaterialRequestItemDto>()
        };

        var result = await controller.CreateRequest(dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Material request must contain at least one item.", badRequest.Value);
    }

    [Fact]
    public async Task CreateRequest_Rejects_When_Project_Not_Found()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, matActive, _, engineer, _) = await SeedBaseData(db);
        var controller = CreateController(db, engineer.Id, "SiteEngineer");

        var dto = new CreateMaterialRequestDto
        {
            ProjectId = 99999,
            RequiredDate = DateTime.UtcNow.AddDays(7),
            Reason = "Missing project",
            Items = new List<CreateMaterialRequestItemDto>
            {
                new() { MaterialId = matActive.Id, Quantity = 10, Unit = "Ton" }
            }
        };

        var result = await controller.CreateRequest(dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Select an active project.", badRequest.Value);
    }

    [Fact]
    public async Task CreateRequest_Rejects_When_Project_Is_Inactive()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, matActive, _, engineer, _) = await SeedBaseData(db);
        project.Status = ProjectStatus.Completed;
        await db.SaveChangesAsync();

        var controller = CreateController(db, engineer.Id, "SiteEngineer");
        var dto = new CreateMaterialRequestDto
        {
            ProjectId = project.Id,
            RequiredDate = DateTime.UtcNow.AddDays(7),
            Reason = "Completed project",
            Items = new List<CreateMaterialRequestItemDto>
            {
                new() { MaterialId = matActive.Id, Quantity = 10, Unit = "Ton" }
            }
        };

        var result = await controller.CreateRequest(dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Select an active project.", badRequest.Value);
    }

    [Fact]
    public async Task CreateRequest_Rejects_When_Material_Is_Inactive_Or_Not_Found()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, _, matInactive, engineer, _) = await SeedBaseData(db);
        var controller = CreateController(db, engineer.Id, "SiteEngineer");

        var dto = new CreateMaterialRequestDto
        {
            ProjectId = project.Id,
            RequiredDate = DateTime.UtcNow.AddDays(7),
            Reason = "Inactive material",
            Items = new List<CreateMaterialRequestItemDto>
            {
                new() { MaterialId = matInactive.Id, Quantity = 5, Unit = "Bag" }
            }
        };

        var result = await controller.CreateRequest(dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Select active materials from the catalogue.", badRequest.Value);
    }

    [Fact]
    public async Task CreateRequest_Rejects_When_Quantity_Zero_Or_Negative()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, matActive, _, engineer, _) = await SeedBaseData(db);
        var controller = CreateController(db, engineer.Id, "SiteEngineer");

        var dtoZero = new CreateMaterialRequestDto
        {
            ProjectId = project.Id,
            RequiredDate = DateTime.UtcNow.AddDays(7),
            Reason = "Zero quantity",
            Items = new List<CreateMaterialRequestItemDto>
            {
                new() { MaterialId = matActive.Id, Quantity = 0m, Unit = "Ton" }
            }
        };

        var resultZero = await controller.CreateRequest(dtoZero);
        var badRequestZero = Assert.IsType<BadRequestObjectResult>(resultZero);
        Assert.Equal("Quantity must be greater than zero.", badRequestZero.Value);

        var dtoNegative = new CreateMaterialRequestDto
        {
            ProjectId = project.Id,
            RequiredDate = DateTime.UtcNow.AddDays(7),
            Reason = "Negative quantity",
            Items = new List<CreateMaterialRequestItemDto>
            {
                new() { MaterialId = matActive.Id, Quantity = -3m, Unit = "Ton" }
            }
        };

        var resultNegative = await controller.CreateRequest(dtoNegative);
        var badRequestNegative = Assert.IsType<BadRequestObjectResult>(resultNegative);
        Assert.Equal("Quantity must be greater than zero.", badRequestNegative.Value);
    }

    [Fact]
    public async Task CreateRequest_Rejects_Duplicate_Materials()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, matActive, _, engineer, _) = await SeedBaseData(db);
        var controller = CreateController(db, engineer.Id, "SiteEngineer");

        var dto = new CreateMaterialRequestDto
        {
            ProjectId = project.Id,
            RequiredDate = DateTime.UtcNow.AddDays(7),
            Reason = "Duplicate material rows",
            Items = new List<CreateMaterialRequestItemDto>
            {
                new() { MaterialId = matActive.Id, Quantity = 5, Unit = "Ton" },
                new() { MaterialId = matActive.Id, Quantity = 15, Unit = "Ton" }
            }
        };

        var result = await controller.CreateRequest(dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Duplicate material items are not allowed in the same request.", badRequest.Value);
    }

    [Fact]
    public async Task CreateRequest_Rejects_RequiredDate_Before_Today()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, matActive, _, engineer, _) = await SeedBaseData(db);
        var controller = CreateController(db, engineer.Id, "SiteEngineer");

        var dto = new CreateMaterialRequestDto
        {
            ProjectId = project.Id,
            RequiredDate = DateTime.UtcNow.AddDays(-2),
            Reason = "Past date",
            Items = new List<CreateMaterialRequestItemDto>
            {
                new() { MaterialId = matActive.Id, Quantity = 5, Unit = "Ton" }
            }
        };

        var result = await controller.CreateRequest(dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Required date cannot be before today.", badRequest.Value);
    }

    // -------------------------------------------------------------
    // SUBMIT DRAFT REQUEST VALIDATIONS
    // -------------------------------------------------------------

    [Fact]
    public async Task SubmitRequest_Rejects_When_Not_Draft()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, matActive, _, engineer, _) = await SeedBaseData(db);
        var req = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = engineer.Id,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = MaterialRequestStatus.PendingApproval,
            Reason = "Already pending"
        };
        db.MaterialRequests.Add(req);
        await db.SaveChangesAsync();

        var controller = CreateController(db, engineer.Id, "SiteEngineer");
        var result = await controller.SubmitRequest(req.Id);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Only draft requests can be submitted.", badRequest.Value);
    }

    [Fact]
    public async Task SubmitRequest_Rejects_When_Items_Empty()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, _, _, engineer, _) = await SeedBaseData(db);
        var req = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = engineer.Id,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = MaterialRequestStatus.Draft,
            Reason = "Draft with no items"
        };
        db.MaterialRequests.Add(req);
        await db.SaveChangesAsync();

        var controller = CreateController(db, engineer.Id, "SiteEngineer");
        var result = await controller.SubmitRequest(req.Id);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Material request must contain at least one item.", badRequest.Value);
    }

    [Fact]
    public async Task SubmitRequest_Rejects_When_Project_Inactive()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, matActive, _, engineer, _) = await SeedBaseData(db);
        var req = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = engineer.Id,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = MaterialRequestStatus.Draft,
            Reason = "Draft for project that became inactive",
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = matActive.Id, RequestedQuantity = 10 }
            }
        };
        db.MaterialRequests.Add(req);
        project.Status = ProjectStatus.Cancelled;
        await db.SaveChangesAsync();

        var controller = CreateController(db, engineer.Id, "SiteEngineer");
        var result = await controller.SubmitRequest(req.Id);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Associated project must exist and be active.", badRequest.Value);
    }

    [Fact]
    public async Task SubmitRequest_Rejects_When_Material_Inactive()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, _, matInactive, engineer, _) = await SeedBaseData(db);
        var req = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = engineer.Id,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = MaterialRequestStatus.Draft,
            Reason = "Draft with inactive material",
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = matInactive.Id, RequestedQuantity = 10 }
            }
        };
        db.MaterialRequests.Add(req);
        await db.SaveChangesAsync();

        var controller = CreateController(db, engineer.Id, "SiteEngineer");
        var result = await controller.SubmitRequest(req.Id);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("All materials in the request must exist and be active.", badRequest.Value);
    }

    [Fact]
    public async Task SubmitRequest_Rejects_When_Quantity_Zero_Or_Negative()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, matActive, _, engineer, _) = await SeedBaseData(db);
        var req = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = engineer.Id,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = MaterialRequestStatus.Draft,
            Reason = "Draft with zero quantity item",
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = matActive.Id, RequestedQuantity = 0 }
            }
        };
        db.MaterialRequests.Add(req);
        await db.SaveChangesAsync();

        var controller = CreateController(db, engineer.Id, "SiteEngineer");
        var result = await controller.SubmitRequest(req.Id);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Quantity must be greater than zero.", badRequest.Value);
    }

    [Fact]
    public async Task SubmitRequest_Rejects_RequiredDate_Before_Today()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, matActive, _, engineer, _) = await SeedBaseData(db);
        var req = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = engineer.Id,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
            Status = MaterialRequestStatus.Draft,
            Reason = "Draft whose required date expired",
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = matActive.Id, RequestedQuantity = 10 }
            }
        };
        db.MaterialRequests.Add(req);
        await db.SaveChangesAsync();

        var controller = CreateController(db, engineer.Id, "SiteEngineer");
        var result = await controller.SubmitRequest(req.Id);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Required date cannot be before today.", badRequest.Value);
    }

    [Fact]
    public async Task SubmitRequest_Enforces_Owner_SiteEngineer_Or_Administrator()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, matActive, _, engineer, _) = await SeedBaseData(db);
        var req = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = engineer.Id,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = MaterialRequestStatus.Draft,
            Reason = "Draft owned by engineer 101",
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = matActive.Id, RequestedQuantity = 10 }
            }
        };
        db.MaterialRequests.Add(req);
        await db.SaveChangesAsync();

        // Another engineer (ID 999) cannot submit
        var otherController = CreateController(db, 999, "SiteEngineer");
        var forbidResult = await otherController.SubmitRequest(req.Id);
        Assert.IsType<ForbidResult>(forbidResult);

        // Administrator CAN submit
        var adminController = CreateController(db, 888, "Administrator");
        var adminResult = await adminController.SubmitRequest(req.Id);
        var okResult = Assert.IsType<OkObjectResult>(adminResult);
        var reloaded = await db.MaterialRequests.FindAsync(req.Id);
        Assert.Equal(MaterialRequestStatus.PendingApproval, reloaded!.Status);
    }

    // -------------------------------------------------------------
    // APPROVAL VALIDATIONS
    // -------------------------------------------------------------

    [Fact]
    public async Task ApproveRequest_Rejects_When_Not_PendingApproval()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, _, _, _, manager) = await SeedBaseData(db);
        var req = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = 101,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = MaterialRequestStatus.Draft,
            Reason = "Draft request cannot be directly approved"
        };
        db.MaterialRequests.Add(req);
        await db.SaveChangesAsync();

        var controller = CreateController(db, manager.Id, "ProcurementManager");
        var result = await controller.ApproveRequest(req.Id, new ApproveMaterialRequestDto { Decision = ApprovalDecision.Approved });
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Only pending requests can be reviewed.", badRequest.Value);
    }

    [Fact]
    public async Task ApproveRequest_Rejects_Approval_When_Project_Inactive()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, matActive, _, _, manager) = await SeedBaseData(db);
        var req = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = 101,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = MaterialRequestStatus.PendingApproval,
            Reason = "Project deactivated during review",
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = matActive.Id, RequestedQuantity = 10 }
            }
        };
        db.MaterialRequests.Add(req);
        project.Status = ProjectStatus.Cancelled;
        await db.SaveChangesAsync();

        var controller = CreateController(db, manager.Id, "ProcurementManager");
        var result = await controller.ApproveRequest(req.Id, new ApproveMaterialRequestDto { Decision = ApprovalDecision.Approved });
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Associated project must exist and be active.", badRequest.Value);
    }

    [Fact]
    public async Task ApproveRequest_Rejects_Approval_When_Material_Inactive()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, matActive, _, _, manager) = await SeedBaseData(db);
        var req = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = 101,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = MaterialRequestStatus.PendingApproval,
            Reason = "Material deactivated during review",
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = matActive.Id, RequestedQuantity = 10 }
            }
        };
        db.MaterialRequests.Add(req);
        matActive.IsActive = false;
        await db.SaveChangesAsync();

        var controller = CreateController(db, manager.Id, "ProcurementManager");
        var result = await controller.ApproveRequest(req.Id, new ApproveMaterialRequestDto { Decision = ApprovalDecision.Approved });
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("All materials in the request must exist and be active.", badRequest.Value);
    }
}
