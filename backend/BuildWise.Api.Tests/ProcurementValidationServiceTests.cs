using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Xunit;

namespace BuildWise.Api.Tests;

public class ProcurementValidationServiceTests
{
    [Fact]
    public async Task Rejects_MaterialRequest_NotApproved()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // Change request status to Draft (not Approved)
        data.Request.Status = MaterialRequestStatus.Draft;
        await db.SaveChangesAsync();

        var supplier = new Supplier { Name = "Alpha Supplies", Status = SupplierStatus.Active };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var quotation = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            TotalAmount = 525000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250m, UnitPrice = 2100m }
            }
        };
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        var service = new ProcurementValidationService(db);
        var result = await service.ValidateRecommendationAsync(quotation.Id, data.Request.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("must be 'Approved'"));
    }

    [Fact]
    public async Task Rejects_Suspended_Supplier()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // Supplier B is Suspended
        var supplier = new Supplier { Name = "Beta Cement Corp", Status = SupplierStatus.Suspended };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var quotation = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            TotalAmount = 510000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250m, UnitPrice = 2040m }
            }
        };
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        var service = new ProcurementValidationService(db);
        var result = await service.ValidateRecommendationAsync(quotation.Id, data.Request.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Suspended") && e.Contains("Only Active"));
    }

    [Fact]
    public async Task Rejects_Expired_Quotation()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var supplier = new Supplier { Name = "Gamma Cement", Status = SupplierStatus.Active };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        // Expired yesterday
        var quotation = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            TotalAmount = 500000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250m, UnitPrice = 2000m }
            }
        };
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        var service = new ProcurementValidationService(db);
        var result = await service.ValidateRecommendationAsync(quotation.Id, data.Request.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("expired"));
    }

    [Fact]
    public async Task Rejects_Total_Mismatch()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var supplier = new Supplier { Name = "Delta Cement", Status = SupplierStatus.Active };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        // Stored total is 500,000 but actual calculated sum is 250 * 2100 = 525,000
        var quotation = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            TotalAmount = 500000m, // MISMATCH!
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250m, UnitPrice = 2100m }
            }
        };
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        var service = new ProcurementValidationService(db);
        var result = await service.ValidateRecommendationAsync(quotation.Id, data.Request.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Quotation total mismatch"));
    }

    [Fact]
    public async Task Rejects_Partial_Coverage_As_Sole_Winner()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var supplier = new Supplier { Name = "Epsilon Supplies", Status = SupplierStatus.Active };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        // Supplier quotes only 200 bags out of 250 requested
        var quotation = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            TotalAmount = 400000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 200m, UnitPrice = 2000m }
            }
        };
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        var service = new ProcurementValidationService(db);
        var result = await service.ValidateRecommendationAsync(quotation.Id, data.Request.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Partial coverage cannot be recommended as sole winner"));
    }

    [Fact]
    public async Task Accepts_Valid_Active_FullCoverage_Quotation()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var supplier = new Supplier { Name = "Supplier A Ltd", Status = SupplierStatus.Active };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var quotation = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            TotalAmount = 525000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250m, UnitPrice = 2100m }
            }
        };
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        var service = new ProcurementValidationService(db);
        var result = await service.ValidateRecommendationAsync(quotation.Id, data.Request.Id);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task PO_Creation_Blocked_Without_Approved_Decision()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var workflow = new AgentWorkflow
        {
            MaterialRequestId = data.Request.Id,
            InitiatedByUserId = 1,
            Objective = "Test objective",
            Status = WorkflowStatus.AwaitingApproval,
            ApprovalStatus = AgentApprovalStatus.Pending
        };
        db.AgentWorkflows.Add(workflow);
        await db.SaveChangesAsync();

        var service = new ProcurementValidationService(db);
        var result = await service.ValidatePurchaseOrderCreationAsync(workflow.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("does not have an Approved manager decision"));
    }

    [Fact]
    public async Task PO_Creation_Blocked_For_Duplicate_Order()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var supplier = new Supplier { Name = "Supplier X", Status = SupplierStatus.Active };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var quotation = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            TotalAmount = 525000m,
            Status = QuotationStatus.Selected
        };
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        // Already has an existing PO for this request
        var existingPo = new PurchaseOrder
        {
            QuotationId = quotation.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PurchaseOrderStatus.Created,
            TotalAmount = 525000m
        };
        db.PurchaseOrders.Add(existingPo);

        var workflow = new AgentWorkflow
        {
            MaterialRequestId = data.Request.Id,
            InitiatedByUserId = 1,
            Objective = "Test",
            Status = WorkflowStatus.Completed,
            ApprovalStatus = AgentApprovalStatus.Approved
        };
        workflow.Approvals.Add(new AgentApproval
        {
            ReviewedByUserId = 2,
            Decision = AgentApprovalStatus.Approved,
            DecisionDate = DateTime.UtcNow
        });
        db.AgentWorkflows.Add(workflow);
        await db.SaveChangesAsync();

        var service = new ProcurementValidationService(db);
        var result = await service.ValidatePurchaseOrderCreationAsync(workflow.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("already exists for material request"));
    }

    // ── Additional rule-path coverage (§5.1 - §5.10) ─────────────────────────

    [Fact]
    public async Task Rejects_Unknown_Material_Request()
    {
        var db = TestDbFactory.CreateInMemory();
        await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var service = new ProcurementValidationService(db);
        var result = await service.ValidateRecommendationAsync(recommendedQuotationId: 1, materialRequestId: 9999);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Material request #9999 does not exist."));
    }

    [Fact]
    public async Task Rejects_Unknown_Quotation()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var service = new ProcurementValidationService(db);
        var result = await service.ValidateRecommendationAsync(recommendedQuotationId: 9999, materialRequestId: data.Request.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Recommended quotation #9999 does not exist."));
    }

    [Fact]
    public async Task Rejects_Quotation_From_Different_Request()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // A second approved request; the quotation is recorded against it…
        var otherRequest = new MaterialRequest
        {
            ProjectId = data.Project.Id,
            RequestedByUserId = 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21)),
            Reason = "Different request",
            Status = MaterialRequestStatus.Approved,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.MaterialRequests.Add(otherRequest);
        await db.SaveChangesAsync();

        var supplier = new Supplier { Name = "Cross Request Supplies", Status = SupplierStatus.Active };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var quotation = new Quotation
        {
            MaterialRequestId = otherRequest.Id,
            SupplierId = supplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            TotalAmount = 525000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250m, UnitPrice = 2100m }
            }
        };
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        // …but the recommendation claims it belongs to the original request.
        var service = new ProcurementValidationService(db);
        var result = await service.ValidateRecommendationAsync(quotation.Id, data.Request.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains($"belongs to request #{otherRequest.Id}, not #{data.Request.Id}"));
    }

    [Fact]
    public async Task Rejects_Line_Item_Reference_Outside_Request()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // A foreign request item that does NOT belong to the recommended request.
        var otherRequest = new MaterialRequest
        {
            ProjectId = data.Project.Id,
            RequestedByUserId = 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21)),
            Reason = "Foreign item holder",
            Status = MaterialRequestStatus.Approved,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.MaterialRequests.Add(otherRequest);
        await db.SaveChangesAsync();

        var foreignItem = new MaterialRequestItem
        {
            MaterialRequestId = otherRequest.Id,
            MaterialId = data.Material.Id,
            RequestedQuantity = 100.0m
        };
        db.MaterialRequestItems.Add(foreignItem);
        await db.SaveChangesAsync();

        var supplier = new Supplier { Name = "Line Item Supplies", Status = SupplierStatus.Active };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        // Quotation item references the foreign request item -> Rule 4 violation.
        var quotation = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            TotalAmount = 210000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = foreignItem.Id, Quantity = 250m, UnitPrice = 2100m }
            }
        };
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        var service = new ProcurementValidationService(db);
        var result = await service.ValidateRecommendationAsync(quotation.Id, data.Request.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("which does not belong to request"));
    }

    [Fact]
    public async Task Rejects_Inactive_Supplier()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var supplier = new Supplier { Name = "Zeta Cement", Status = SupplierStatus.Inactive };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var quotation = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            TotalAmount = 525000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250m, UnitPrice = 2100m }
            }
        };
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        var service = new ProcurementValidationService(db);
        var result = await service.ValidateRecommendationAsync(quotation.Id, data.Request.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("is Inactive. Only Active suppliers are eligible."));
    }

    [Fact]
    public async Task PO_Creation_Blocked_For_Unknown_Workflow()
    {
        var db = TestDbFactory.CreateInMemory();
        await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var service = new ProcurementValidationService(db);
        var result = await service.ValidatePurchaseOrderCreationAsync(workflowId: 9999);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Workflow #9999 does not exist."));
    }

    [Fact]
    public async Task PO_Creation_Blocked_When_Request_Not_Approved()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // The request regressed to Draft even though the manager approved.
        data.Request.Status = MaterialRequestStatus.Draft;
        await db.SaveChangesAsync();

        var workflow = new AgentWorkflow
        {
            MaterialRequestId = data.Request.Id,
            InitiatedByUserId = 1,
            Objective = "Test",
            Status = WorkflowStatus.AwaitingApproval,
            ApprovalStatus = AgentApprovalStatus.Approved
        };
        workflow.Approvals.Add(new AgentApproval
        {
            ReviewedByUserId = 2,
            Decision = AgentApprovalStatus.Approved,
            DecisionDate = DateTime.UtcNow
        });
        db.AgentWorkflows.Add(workflow);
        await db.SaveChangesAsync();

        var service = new ProcurementValidationService(db);
        var result = await service.ValidatePurchaseOrderCreationAsync(workflow.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("is not in Approved state"));
    }
}
