using System.Security.Claims;
using BuildWise.Api.Controllers;
using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildWise.Api.Tests;

public class ProcurementComponent2ValidationTests
{
    private class DummyEmailService : IEmailService
    {
        public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static QuotationsController CreateQuotationsController(ApplicationDbContext db)
    {
        var controller = new QuotationsController(db, NullLogger<QuotationsController>.Instance);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "101"),
            new(ClaimTypes.Role, "ProcurementOfficer")
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

    private static PurchaseOrdersController CreatePurchaseOrdersController(ApplicationDbContext db)
    {
        var validationService = new ProcurementValidationService(db);
        var workflowService = new ProcurementWorkflowService(
            db,
            new QuotationAgentClient(new HttpClient(), NullLogger<QuotationAgentClient>.Instance),
            validationService,
            new DummyEmailService(),
            NullLogger<ProcurementWorkflowService>.Instance
        );
        var controller = new PurchaseOrdersController(db, workflowService, NullLogger<PurchaseOrdersController>.Instance);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "102"),
            new(ClaimTypes.Role, "ProcurementManager")
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

    private static ProcurementWorkflowService CreateWorkflowService(ApplicationDbContext db)
    {
        var validationService = new ProcurementValidationService(db);
        return new ProcurementWorkflowService(
            db,
            new QuotationAgentClient(new HttpClient(), NullLogger<QuotationAgentClient>.Instance),
            validationService,
            new DummyEmailService(),
            NullLogger<ProcurementWorkflowService>.Instance
        );
    }

    private static async Task<(Project Project, Material Material, MaterialRequest Request, MaterialRequestItem RequestItem, Supplier ActiveSupplier, Supplier InactiveSupplier)> SeedDataAsync(ApplicationDbContext db)
    {
        var project = new Project
        {
            Name = "Commerce Tower Phase 1",
            Location = "Block A",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Projects.Add(project);

        var material = new Material
        {
            Name = "Reinforcement Rebar (16mm)",
            Unit = "Ton",
            Category = "Structural",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var request = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            Reason = "Piling rebar reinforcement",
            Status = MaterialRequestStatus.Approved,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.MaterialRequests.Add(request);
        await db.SaveChangesAsync();

        var requestItem = new MaterialRequestItem
        {
            MaterialRequestId = request.Id,
            MaterialId = material.Id,
            RequestedQuantity = 50m,
            Notes = "Grade 500 rebar"
        };
        db.MaterialRequestItems.Add(requestItem);

        var activeSupplier = new Supplier
        {
            Name = "Apex Steel Mills",
            Status = SupplierStatus.Active,
            ContactPerson = "John",
            Email = "apex@test.com",
            CreatedAt = DateTime.UtcNow
        };
        var inactiveSupplier = new Supplier
        {
            Name = "Suspended Steel Works",
            Status = SupplierStatus.Suspended,
            ContactPerson = "Dave",
            Email = "suspended@test.com",
            CreatedAt = DateTime.UtcNow
        };
        db.Suppliers.AddRange(activeSupplier, inactiveSupplier);
        await db.SaveChangesAsync();

        return (project, material, request, requestItem, activeSupplier, inactiveSupplier);
    }

    // =========================================================================
    // QUOTATION VALIDATIONS (§3.1 / §5.1 - §5.6)
    // =========================================================================

    [Fact]
    public async Task CreateQuotation_Rejects_When_Request_Not_Found()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, _, _, activeSupplier, _) = await SeedDataAsync(db);
        var controller = CreateQuotationsController(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dto = new CreateQuotationDto(
            activeSupplier.Id,
            today,
            today.AddDays(14),
            new List<QuotationItemInputDto> { new(1, 10m, 1000m) }
        );

        var result = await controller.Create(99999, dto);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateQuotation_Rejects_When_Request_Not_Approved()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, requestItem, activeSupplier, _) = await SeedDataAsync(db);
        request.Status = MaterialRequestStatus.PendingApproval;
        await db.SaveChangesAsync();

        var controller = CreateQuotationsController(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dto = new CreateQuotationDto(
            activeSupplier.Id,
            today,
            today.AddDays(14),
            new List<QuotationItemInputDto> { new(requestItem.Id, 10m, 1000m) }
        );

        var result = await controller.Create(request.Id, dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("must be 'Approved'", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task CreateQuotation_Rejects_When_Supplier_Not_Found()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, requestItem, _, _) = await SeedDataAsync(db);
        var controller = CreateQuotationsController(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dto = new CreateQuotationDto(
            88888,
            today,
            today.AddDays(14),
            new List<QuotationItemInputDto> { new(requestItem.Id, 10m, 1000m) }
        );

        var result = await controller.Create(request.Id, dto);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateQuotation_Rejects_When_Supplier_Not_Active()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, requestItem, _, inactiveSupplier) = await SeedDataAsync(db);
        var controller = CreateQuotationsController(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dto = new CreateQuotationDto(
            inactiveSupplier.Id,
            today,
            today.AddDays(14),
            new List<QuotationItemInputDto> { new(requestItem.Id, 10m, 1000m) }
        );

        var result = await controller.Create(request.Id, dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("is not active", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task CreateQuotation_Rejects_When_Items_Empty()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, _, activeSupplier, _) = await SeedDataAsync(db);
        var controller = CreateQuotationsController(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dto = new CreateQuotationDto(
            activeSupplier.Id,
            today,
            today.AddDays(14),
            new List<QuotationItemInputDto>()
        );

        var result = await controller.Create(request.Id, dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("At least one quotation item is required.", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task CreateQuotation_Rejects_When_QuotationDate_In_Future()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, requestItem, activeSupplier, _) = await SeedDataAsync(db);
        var controller = CreateQuotationsController(db);

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var dto = new CreateQuotationDto(
            activeSupplier.Id,
            tomorrow,
            tomorrow.AddDays(14),
            new List<QuotationItemInputDto> { new(requestItem.Id, 10m, 1000m) }
        );

        var result = await controller.Create(request.Id, dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Quotation date cannot be in the future.", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task CreateQuotation_Rejects_When_ValidUntil_Before_QuotationDate()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, requestItem, activeSupplier, _) = await SeedDataAsync(db);
        var controller = CreateQuotationsController(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dto = new CreateQuotationDto(
            activeSupplier.Id,
            today,
            today.AddDays(-1),
            new List<QuotationItemInputDto> { new(requestItem.Id, 10m, 1000m) }
        );

        var result = await controller.Create(request.Id, dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Valid-until must be on or after the quotation date.", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task CreateQuotation_Rejects_When_Quotation_Already_Expired()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, requestItem, activeSupplier, _) = await SeedDataAsync(db);
        var controller = CreateQuotationsController(db);

        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
        var pastValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var dto = new CreateQuotationDto(
            activeSupplier.Id,
            pastDate,
            pastValidUntil,
            new List<QuotationItemInputDto> { new(requestItem.Id, 10m, 1000m) }
        );

        var result = await controller.Create(request.Id, dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Quotation has already expired.", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task CreateQuotation_Rejects_When_Duplicate_Item_Ids()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, requestItem, activeSupplier, _) = await SeedDataAsync(db);
        var controller = CreateQuotationsController(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dto = new CreateQuotationDto(
            activeSupplier.Id,
            today,
            today.AddDays(14),
            new List<QuotationItemInputDto>
            {
                new(requestItem.Id, 10m, 1000m),
                new(requestItem.Id, 15m, 1100m)
            }
        );

        var result = await controller.Create(request.Id, dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Each request item may appear only once.", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task CreateQuotation_Rejects_When_Item_Does_Not_Belong_To_Request()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, _, activeSupplier, _) = await SeedDataAsync(db);
        var controller = CreateQuotationsController(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dto = new CreateQuotationDto(
            activeSupplier.Id,
            today,
            today.AddDays(14),
            new List<QuotationItemInputDto> { new(99999, 10m, 1000m) }
        );

        var result = await controller.Create(request.Id, dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("does not belong to request", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task CreateQuotation_Rejects_When_Quantity_NonPositive()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, requestItem, activeSupplier, _) = await SeedDataAsync(db);
        var controller = CreateQuotationsController(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dto = new CreateQuotationDto(
            activeSupplier.Id,
            today,
            today.AddDays(14),
            new List<QuotationItemInputDto> { new(requestItem.Id, 0m, 1000m) }
        );

        var result = await controller.Create(request.Id, dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("must be greater than zero", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task CreateQuotation_Rejects_When_UnitPrice_Negative()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, requestItem, activeSupplier, _) = await SeedDataAsync(db);
        var controller = CreateQuotationsController(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dto = new CreateQuotationDto(
            activeSupplier.Id,
            today,
            today.AddDays(14),
            new List<QuotationItemInputDto> { new(requestItem.Id, 10m, -5m) }
        );

        var result = await controller.Create(request.Id, dto);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("cannot be negative", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task CreateQuotation_Calculates_Total_ServerSide()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, requestItem, activeSupplier, _) = await SeedDataAsync(db);
        var controller = CreateQuotationsController(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dto = new CreateQuotationDto(
            activeSupplier.Id,
            today,
            today.AddDays(14),
            new List<QuotationItemInputDto> { new(requestItem.Id, 25m, 1200.50m) }
        );

        var result = await controller.Create(request.Id, dto);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var quotation = Assert.IsType<QuotationDto>(okResult.Value);

        Assert.Equal(30012.50m, quotation.TotalAmount);
        Assert.Equal("Submitted", quotation.Status);
    }

    // =========================================================================
    // MANAGER DECISION VALIDATIONS (§4.5 / §7)
    // =========================================================================

    [Fact]
    public async Task RecordDecision_Rejects_When_Workflow_Not_AwaitingApproval()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, _, _, _) = await SeedDataAsync(db);
        var workflowService = CreateWorkflowService(db);

        var workflow = new AgentWorkflow
        {
            MaterialRequestId = request.Id,
            Objective = "Analysis",
            Status = WorkflowStatus.Completed,
            ApprovalStatus = AgentApprovalStatus.Approved,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AgentWorkflows.Add(workflow);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflowService.RecordDecisionAsync(workflow.Id, new WorkflowDecisionDto("Approve", "Ok", 102)));

        Assert.Contains("not awaiting approval or a decision has already been recorded", ex.Message);
    }

    [Fact]
    public async Task RecordDecision_Rejects_Duplicate_Decision()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, _, _, _) = await SeedDataAsync(db);
        var workflowService = CreateWorkflowService(db);

        var workflow = new AgentWorkflow
        {
            MaterialRequestId = request.Id,
            Objective = "Analysis",
            Status = WorkflowStatus.AwaitingApproval,
            ApprovalStatus = AgentApprovalStatus.Approved, // already decided
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AgentWorkflows.Add(workflow);
        db.AgentApprovals.Add(new AgentApproval
        {
            AgentWorkflowId = workflow.Id,
            ReviewedByUserId = 102,
            Decision = AgentApprovalStatus.Approved,
            DecisionDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflowService.RecordDecisionAsync(workflow.Id, new WorkflowDecisionDto("Approve", "Second decision", 102)));

        Assert.Contains("not awaiting approval or a decision has already been recorded", ex.Message);
    }

    [Fact]
    public async Task RecordDecision_Rejects_Invalid_Decision_Verb()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, _, _, _) = await SeedDataAsync(db);
        var workflowService = CreateWorkflowService(db);

        var workflow = new AgentWorkflow
        {
            MaterialRequestId = request.Id,
            Objective = "Analysis",
            Status = WorkflowStatus.AwaitingApproval,
            ApprovalStatus = AgentApprovalStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AgentWorkflows.Add(workflow);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            workflowService.RecordDecisionAsync(workflow.Id, new WorkflowDecisionDto("Maybe", "Not sure", 102)));

        Assert.Contains("Invalid decision. Must be Approve, Reject, or RevisionRequested.", ex.Message);
    }

    // =========================================================================
    // PURCHASE ORDER VALIDATIONS (§4.6 / §5.8 - §5.10)
    // =========================================================================

    [Fact]
    public async Task CreatePurchaseOrder_Blocked_When_Workflow_Not_Approved()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, request, _, _, _) = await SeedDataAsync(db);
        var workflowService = CreateWorkflowService(db);

        var workflow = new AgentWorkflow
        {
            MaterialRequestId = request.Id,
            Objective = "Analysis",
            Status = WorkflowStatus.AwaitingApproval,
            ApprovalStatus = AgentApprovalStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AgentWorkflows.Add(workflow);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflowService.CreatePurchaseOrderFromWorkflowAsync(workflow.Id));

        Assert.Contains("has not been approved by a manager", ex.Message);
    }

    [Fact]
    public async Task CreatePurchaseOrder_Blocked_When_Duplicate_PO_Exists()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, _, request, requestItem, activeSupplier, _) = await SeedDataAsync(db);
        var workflowService = CreateWorkflowService(db);

        var quotation = new Quotation
        {
            MaterialRequestId = request.Id,
            SupplierId = activeSupplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            TotalAmount = 50000m,
            Status = QuotationStatus.Selected,
            Items = new List<QuotationItem> { new() { MaterialRequestItemId = requestItem.Id, Quantity = 50m, UnitPrice = 1000m } }
        };
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        var existingPo = new PurchaseOrder
        {
            QuotationId = quotation.Id,
            SupplierId = activeSupplier.Id,
            ProjectId = project.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PurchaseOrderStatus.Created,
            TotalAmount = 50000m
        };
        db.PurchaseOrders.Add(existingPo);

        var workflow = new AgentWorkflow
        {
            MaterialRequestId = request.Id,
            Objective = "Analysis",
            Status = WorkflowStatus.Completed,
            ApprovalStatus = AgentApprovalStatus.Approved,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AgentWorkflows.Add(workflow);
        db.AgentApprovals.Add(new AgentApproval
        {
            AgentWorkflowId = workflow.Id,
            ReviewedByUserId = 102,
            Decision = AgentApprovalStatus.Approved,
            DecisionDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflowService.CreatePurchaseOrderFromWorkflowAsync(workflow.Id));

        Assert.Contains("already exists for material request", ex.Message);
    }

    [Fact]
    public async Task PurchaseOrder_Status_Transitions_Enforced()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, _, _, _, activeSupplier, _) = await SeedDataAsync(db);
        var controller = CreatePurchaseOrdersController(db);

        var po = new PurchaseOrder
        {
            SupplierId = activeSupplier.Id,
            ProjectId = project.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PurchaseOrderStatus.Created,
            TotalAmount = 1000m
        };
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();

        // 1. Invalid transition from Created directly to Completed -> BadRequest
        var invalidResult = await controller.UpdateStatus(po.Id, new UpdatePurchaseOrderStatusDto("Completed"));
        var badRequest = Assert.IsType<BadRequestObjectResult>(invalidResult);
        Assert.Contains("Cannot change order from Created to Completed", badRequest.Value?.ToString());

        // 2. Allowed transition from Created to Confirmed -> NoContent
        var validResult = await controller.UpdateStatus(po.Id, new UpdatePurchaseOrderStatusDto("Confirmed"));
        Assert.IsType<NoContentResult>(validResult);

        // Verify status updated
        var updatedPo = await db.PurchaseOrders.FindAsync(po.Id);
        Assert.Equal(PurchaseOrderStatus.Confirmed, updatedPo!.Status);

        // 3. Invalid transition from Confirmed to InProgress via Procurement API -> BadRequest (receiving owns InProgress)
        var invalidResult2 = await controller.UpdateStatus(po.Id, new UpdatePurchaseOrderStatusDto("InProgress"));
        var badRequest2 = Assert.IsType<BadRequestObjectResult>(invalidResult2);
        Assert.Contains("Cannot change order from Confirmed to InProgress", badRequest2.Value?.ToString());

        // 4. Allowed transition from Confirmed to Cancelled -> NoContent
        var cancelResult = await controller.UpdateStatus(po.Id, new UpdatePurchaseOrderStatusDto("Cancelled"));
        Assert.IsType<NoContentResult>(cancelResult);

        var cancelledPo = await db.PurchaseOrders.FindAsync(po.Id);
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelledPo!.Status);
    }
}

