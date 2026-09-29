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

public class MaterialRequestApprovalHistoryTests
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

    private static async Task<(MaterialRequest Request, User Manager)> SeedPendingRequest(ApplicationDbContext db)
    {
        var managerRole = await db.Roles.FirstAsync(r => r.Name == "ProcurementManager");
        var engineerRole = await db.Roles.FirstAsync(r => r.Name == "SiteEngineer");

        var engineer = new User { Id = 2, FullName = "Sam Engineer", Email = "site.engineer@buildwise.demo" };
        var manager = new User { Id = 4, FullName = "Mira Manager", Email = "procurement.manager@buildwise.demo" };
        engineer.UserRoles.Add(new UserRole { RoleId = engineerRole.Id });
        manager.UserRoles.Add(new UserRole { RoleId = managerRole.Id });
        db.Users.AddRange(engineer, manager);

        var project = new Project { Id = 1, Name = "Test Project", Status = ProjectStatus.Active };
        var material = new Material { Id = 1, Name = "Cement", Unit = "bag", IsActive = true };
        db.Projects.Add(project);
        db.Materials.Add(material);

        var request = new MaterialRequest
        {
            Id = 10,
            ProjectId = 1,
            RequestedByUserId = 2,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            Status = MaterialRequestStatus.PendingApproval,
            Reason = "Foundation work"
        };
        db.MaterialRequests.Add(request);
        await db.SaveChangesAsync();

        return (request, manager);
    }

    [Fact]
    public async Task ProcurementManager_approval_changes_status_and_persists_approval_history()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (request, manager) = await SeedPendingRequest(db);
        var controller = CreateController(db, manager.Id, "ProcurementManager");

        // Request body contains a different userId (999), but controller MUST use actorId (manager.Id)
        var dto = new ApproveMaterialRequestDto
        {
            UserId = 999,
            Decision = ApprovalDecision.Approved,
            Comment = "Approved for foundation pour."
        };

        var result = await controller.ApproveRequest(request.Id, dto);
        var okResult = Assert.IsType<OkObjectResult>(result);

        // Verify request status changed
        var reloaded = await db.MaterialRequests.FindAsync(request.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(MaterialRequestStatus.Approved, reloaded.Status);

        // Verify exactly one Approval row was created
        var approval = Assert.Single(await db.Approvals.ToListAsync());
        Assert.Equal(request.Id, approval.MaterialRequestId);
        Assert.Equal(manager.Id, approval.ApprovedByUserId);
        Assert.Equal(ApprovalDecision.Approved, approval.Decision);
        Assert.Equal("Approved for foundation pour.", approval.Comment);
        Assert.True(approval.DecisionDate <= DateTime.UtcNow);
        Assert.True(approval.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task ProcurementManager_rejection_changes_status_to_Rejected_and_persists_history()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (request, manager) = await SeedPendingRequest(db);
        var controller = CreateController(db, manager.Id, "ProcurementManager");

        var dto = new ApproveMaterialRequestDto
        {
            UserId = 999,
            Decision = ApprovalDecision.Rejected,
            Comment = "Rejected due to budget constraints."
        };

        var result = await controller.ApproveRequest(request.Id, dto);
        Assert.IsType<OkObjectResult>(result);

        var reloaded = await db.MaterialRequests.FindAsync(request.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(MaterialRequestStatus.Rejected, reloaded.Status);

        var approval = Assert.Single(await db.Approvals.ToListAsync());
        Assert.Equal(ApprovalDecision.Rejected, approval.Decision);
        Assert.Equal("Rejected due to budget constraints.", approval.Comment);
    }

    [Fact]
    public async Task ProcurementManager_revision_requested_keeps_pending_and_persists_history()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (request, manager) = await SeedPendingRequest(db);
        var controller = CreateController(db, manager.Id, "ProcurementManager");

        var dto = new ApproveMaterialRequestDto
        {
            UserId = manager.Id,
            Decision = ApprovalDecision.RevisionRequested,
            Comment = "Please specify exact delivery location on site."
        };

        var result = await controller.ApproveRequest(request.Id, dto);
        Assert.IsType<OkObjectResult>(result);

        var reloaded = await db.MaterialRequests.FindAsync(request.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(MaterialRequestStatus.PendingApproval, reloaded.Status);

        var approval = Assert.Single(await db.Approvals.ToListAsync());
        Assert.Equal(ApprovalDecision.RevisionRequested, approval.Decision);
        Assert.Equal("Please specify exact delivery location on site.", approval.Comment);
    }

    [Fact]
    public async Task Invalid_decision_creates_no_approval_row()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (request, manager) = await SeedPendingRequest(db);
        var controller = CreateController(db, manager.Id, "ProcurementManager");

        var dto = new ApproveMaterialRequestDto
        {
            UserId = manager.Id,
            Decision = (ApprovalDecision)99,
            Comment = "Bogus decision"
        };

        var result = await controller.ApproveRequest(request.Id, dto);
        Assert.IsType<BadRequestObjectResult>(result);

        Assert.Empty(await db.Approvals.ToListAsync());
        var reloaded = await db.MaterialRequests.FindAsync(request.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(MaterialRequestStatus.PendingApproval, reloaded.Status);
    }

    [Fact]
    public async Task Multiple_valid_reviews_preserve_history_rather_than_overwriting()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (request, manager) = await SeedPendingRequest(db);
        var controller = CreateController(db, manager.Id, "ProcurementManager");

        // 1. RevisionRequested
        await controller.ApproveRequest(request.Id, new ApproveMaterialRequestDto
        {
            UserId = manager.Id,
            Decision = ApprovalDecision.RevisionRequested,
            Comment = "First review: needs clarification."
        });

        // 2. Final Approval
        await controller.ApproveRequest(request.Id, new ApproveMaterialRequestDto
        {
            UserId = manager.Id,
            Decision = ApprovalDecision.Approved,
            Comment = "Second review: clarifications accepted."
        });

        var history = await db.Approvals.Where(a => a.MaterialRequestId == request.Id)
            .OrderBy(a => a.Id).ToListAsync();

        Assert.Equal(2, history.Count);
        Assert.Equal(ApprovalDecision.RevisionRequested, history[0].Decision);
        Assert.Equal("First review: needs clarification.", history[0].Comment);
        Assert.Equal(ApprovalDecision.Approved, history[1].Decision);
        Assert.Equal("Second review: clarifications accepted.", history[1].Comment);
    }
}
