using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildWise.Api.Tests;

public class ScenarioReplayTests
{
    [Fact]
    public async Task Replay_Cement_Scenario_EndToEnd()
    {
        // 1. Setup Database and Material Request
        var db = TestDbFactory.CreateInMemory();
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // 2. Setup the 3 scenario suppliers (§11 / §230):
        // Supplier A: Active
        var supplierA = new Supplier { Name = "Supplier A Ltd", ContactPerson = "Alice", Status = SupplierStatus.Active };
        // Supplier B: Suspended
        var supplierB = new Supplier { Name = "Supplier B Corp", ContactPerson = "Bob", Status = SupplierStatus.Suspended };
        // Supplier C: Active
        var supplierC = new Supplier { Name = "Supplier C Supplies", ContactPerson = "Charlie", Status = SupplierStatus.Active };

        db.Suppliers.AddRange(supplierA, supplierB, supplierC);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var futureDate = today.AddDays(30);

        // 3. Setup Quotations:
        // Quotation A: 250 bags @ 2,100 = 525,000 (Full, Active)
        var quoteA = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplierA.Id,
            QuotationDate = today,
            ValidUntil = futureDate,
            Status = QuotationStatus.Submitted,
            TotalAmount = 525000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250m, UnitPrice = 2100m }
            }
        };

        // Quotation B: 250 bags @ 2,040 = 510,000 (Full, but Suspended!)
        var quoteB = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplierB.Id,
            QuotationDate = today,
            ValidUntil = futureDate,
            Status = QuotationStatus.Submitted,
            TotalAmount = 510000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250m, UnitPrice = 2040m }
            }
        };

        // Quotation C: 200 bags @ 2,050 = 410,000 (Partial 200/250, Active, cheapest total)
        var quoteC = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplierC.Id,
            QuotationDate = today,
            ValidUntil = futureDate,
            Status = QuotationStatus.Submitted,
            TotalAmount = 410000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 200m, UnitPrice = 2050m }
            }
        };

        db.Quotations.AddRange(quoteA, quoteB, quoteC);
        await db.SaveChangesAsync();

        // 4. Initialize Services
        var httpClient = new HttpClient();
        var agentLogger = NullLogger<QuotationAgentClient>.Instance;
        var agentClient = new QuotationAgentClient(httpClient, agentLogger);

        var validationService = new ProcurementValidationService(db);
        var workflowLogger = NullLogger<ProcurementWorkflowService>.Instance;
        var workflowService = new ProcurementWorkflowService(db, agentClient, validationService, new NoOpEmailService(), workflowLogger);

        // 5. Procurement Officer starts Agent workflow (§4.2 / §7)
        var startResponse = await workflowService.StartWorkflowAsync(data.Request.Id, initiatedByUserId: 1);

        Assert.Equal("AwaitingApproval", startResponse.Status);
        Assert.True(startResponse.WorkflowId > 0);

        // 6. Verify workflow state, AI recommendation & deterministic validation
        var details = await workflowService.GetWorkflowDetailsAsync(startResponse.WorkflowId);
        Assert.NotNull(details);
        Assert.Equal("AwaitingApproval", details.Status);
        Assert.NotNull(details.Recommendation);

        // Assert: Winner is Supplier A (quoteA.Id)
        Assert.Equal(quoteA.Id, details.Recommendation.RecommendedQuotationId);
        Assert.Equal(supplierA.Id, details.Recommendation.RecommendedSupplierId);

        // Assert: Warnings correctly identify B as Suspended and C as Partial coverage
        var warningsText = string.Join(" ", details.Recommendation.Warnings);
        Assert.Contains("Supplier B Corp", warningsText);
        Assert.Contains("Suspended", warningsText);
        Assert.Contains("Supplier C Supplies", warningsText);
        Assert.Contains("200", warningsText);

        // Assert: Deterministic validation passed
        Assert.NotNull(details.Validation);
        Assert.True(details.Validation.IsValid);
        Assert.Empty(details.Validation.Errors);

        // 7. Procurement Manager authorizes decision: "Approve" (§4.5 / §7)
        var decisionDto = new WorkflowDecisionDto(
            Decision: "Approve",
            Comment: "Supplier A satisfies full delivery volume and quality standing. Approved for purchase order creation.",
            ReviewedByUserId: 2
        );

        var approval = await workflowService.RecordDecisionAsync(startResponse.WorkflowId, decisionDto);
        Assert.Equal(AgentApprovalStatus.Approved, approval.Decision);

        // 8. Verify Purchase Order creation and state transitions (§4.6)
        var updatedWorkflow = await workflowService.GetWorkflowDetailsAsync(startResponse.WorkflowId);
        Assert.Equal("Completed", updatedWorkflow!.Status);
        Assert.Equal("Approved", updatedWorkflow.ApprovalStatus);

        // Verify winner quotation status is Selected
        var finalQuoteA = await db.Quotations.FindAsync(quoteA.Id);
        Assert.Equal(QuotationStatus.Selected, finalQuoteA!.Status);

        // Verify losing quotations are Rejected
        var finalQuoteB = await db.Quotations.FindAsync(quoteB.Id);
        var finalQuoteC = await db.Quotations.FindAsync(quoteC.Id);
        Assert.Equal(QuotationStatus.Rejected, finalQuoteB!.Status);
        Assert.Equal(QuotationStatus.Rejected, finalQuoteC!.Status);

        // Verify Purchase Order was created with correct quantity and amount
        var po = db.PurchaseOrders.FirstOrDefault(p => p.QuotationId == quoteA.Id);
        Assert.NotNull(po);
        Assert.Equal(525000m, po.TotalAmount);
        Assert.Equal(PurchaseOrderStatus.Created, po.Status);

        var poItems = db.PurchaseOrderItems.Where(poi => poi.PurchaseOrderId == po.Id).ToList();
        Assert.Single(poItems);
        Assert.Equal(250m, poItems[0].OrderedQuantity);
        Assert.Equal(2100m, poItems[0].UnitPrice);
    }
}
