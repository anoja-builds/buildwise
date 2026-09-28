using System.Net;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildWise.Api.Tests;

/// <summary>
/// End-to-end workflow and lifecycle tests for Component 4:
/// Quality Inspection, Non-Conformance Reports (NCR), Corrective Action lifecycle,
/// and Quality Risk Agent audit trail.
/// </summary>
public class QualityInspectionWorkflowTests
{
    [Fact]
    public async Task Full_Quality_Inspection_And_NCR_Lifecycle_Scenario()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, deliveryItem, inspector) = await SeedReceivedDeliveryWithDiscrepancyAsync(db);

        var inspectionService = new QualityInspectionService(db);
        var ncrService = new NonConformanceService(db);

        // 1. Pending Deliveries: Quality Inspector sees the received delivery ready for inspection
        var pending = await inspectionService.GetPendingDeliveriesAsync();
        var pendingDelivery = Assert.Single(pending);
        Assert.Equal(delivery.Id, pendingDelivery.DeliveryId);
        Assert.Equal("DEL-2026-001", pendingDelivery.DeliveryReference);

        // 2. Start Inspection
        var startDto = new StartInspectionDto { DeliveryId = delivery.Id, Notes = "Commencing cement inspection on site." };
        var startedInspection = await inspectionService.StartInspectionAsync(startDto, inspector.Id);
        Assert.Equal(InspectionStatus.UnderInspection, startedInspection.Status);
        Assert.Equal(delivery.Id, startedInspection.DeliveryId);
        Assert.Equal(inspector.Id, startedInspection.InspectorUserId);

        // Delivery is no longer in pending deliveries once under inspection
        var pendingAfterStart = await inspectionService.GetPendingDeliveriesAsync();
        Assert.DoesNotContain(pendingAfterStart, d => d.DeliveryId == delivery.Id);

        // 3. Complete Inspection: 235 Accepted, 5 Rejected (Cement scenario from 240 received)
        var completeDto = new CompleteInspectionDto
        {
            OverallDecision = InspectionDecision.PartiallyAccepted,
            Notes = "235 bags passed ASTM C150 standard; 5 bags water-damaged in transit.",
            Items =
            [
                new CompleteInspectionItemDto
                {
                    DeliveryItemId = deliveryItem.Id,
                    AcceptedQuantity = 235,
                    RejectedQuantity = 5,
                    Condition = "5 bags torn and caked from water exposure",
                    Remarks = "Reject 5 bags, accept 235 sound bags"
                }
            ]
        };
        var completed = await inspectionService.CompleteInspectionAsync(startedInspection.Id, completeDto);
        Assert.Equal(InspectionStatus.Completed, completed.Status);
        Assert.Equal(InspectionDecision.PartiallyAccepted, completed.OverallDecision);

        var completedItem = Assert.Single(completed.Items);
        Assert.Equal(235, completedItem.AcceptedQuantity);
        Assert.Equal(5, completedItem.RejectedQuantity);

        // 4. Create NCR for the 5 rejected bags
        var createNcrDto = new CreateNonConformanceDto
        {
            InspectionItemId = completedItem.Id,
            IssueDescription = "5 bags of Portland Cement exposed to moisture during transit, resulting in hydration and hardening.",
            Severity = NonConformanceSeverity.Medium,
            CorrectiveAction = null // Initially open without corrective action
        };
        var ncr = await ncrService.CreateAsync(createNcrDto);
        Assert.Equal(NonConformanceStatus.Open, ncr.Status);
        Assert.Equal(NonConformanceSeverity.Medium, ncr.Severity);
        Assert.Null(ncr.CorrectiveAction);
        Assert.Equal(completedItem.Id, ncr.InspectionItemId);

        // 5. Update Corrective Action
        var updateActionDto = new UpdateCorrectiveActionDto
        {
            CorrectiveAction = "Supplier ABC Materials agreed to credit 5 bags and replace with next scheduled delivery. Tarpaulins required for future transit."
        };
        var updatedNcr = await ncrService.UpdateCorrectiveActionAsync(ncr.Id, updateActionDto);
        Assert.Equal(NonConformanceStatus.CorrectiveActionRequired, updatedNcr.Status);
        Assert.Contains("Supplier ABC Materials agreed", updatedNcr.CorrectiveAction);

        // 6. Resolve NCR
        var resolvedNcr = await ncrService.ResolveAsync(ncr.Id);
        Assert.Equal(NonConformanceStatus.Resolved, resolvedNcr.Status);
        Assert.NotNull(resolvedNcr.ResolvedAt);

        // 7. Close NCR
        var closedNcr = await ncrService.CloseAsync(ncr.Id);
        Assert.Equal(NonConformanceStatus.Closed, closedNcr.Status);
        Assert.NotNull(closedNcr.ResolvedAt); // Preserved

        // 8. Verify history lists the completed inspection and closed NCR
        var history = await inspectionService.GetHistoryAsync();
        Assert.Contains(history, h => h.Id == startedInspection.Id && h.Status == InspectionStatus.Completed);

        var allNcrs = await ncrService.GetAllAsync();
        Assert.Contains(allNcrs, n => n.Id == ncr.Id && n.Status == NonConformanceStatus.Closed);
    }

    [Fact]
    public async Task NCR_Lifecycle_Strict_State_Machine_Validation()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, deliveryItem, inspector) = await SeedReceivedDeliveryWithDiscrepancyAsync(db);

        var inspectionService = new QualityInspectionService(db);
        var ncrService = new NonConformanceService(db);

        var started = await inspectionService.StartInspectionAsync(new StartInspectionDto { DeliveryId = delivery.Id }, inspector.Id);
        var completed = await inspectionService.CompleteInspectionAsync(started.Id, new CompleteInspectionDto
        {
            OverallDecision = InspectionDecision.PartiallyAccepted,
            Items = [new CompleteInspectionItemDto { DeliveryItemId = deliveryItem.Id, AcceptedQuantity = 200, RejectedQuantity = 40 }]
        });
        var item = Assert.Single(completed.Items);

        // Create Open NCR
        var ncr = await ncrService.CreateAsync(new CreateNonConformanceDto
        {
            InspectionItemId = item.Id,
            IssueDescription = "40 units failed compression strength test.",
            Severity = NonConformanceSeverity.High
        });
        Assert.Equal(NonConformanceStatus.Open, ncr.Status);

        // Rule 1: Cannot resolve without corrective action
        var resolveError = await Assert.ThrowsAsync<NonConformanceException>(() => ncrService.ResolveAsync(ncr.Id));
        Assert.Equal(400, resolveError.StatusCode);

        // Rule 2: Cannot close directly from Open
        var closeError = await Assert.ThrowsAsync<NonConformanceException>(() => ncrService.CloseAsync(ncr.Id));
        Assert.Equal(409, closeError.StatusCode);

        // Add corrective action -> moves to CorrectiveActionRequired
        await ncrService.UpdateCorrectiveActionAsync(ncr.Id, new UpdateCorrectiveActionDto { CorrectiveAction = "Quarantine batch and request supplier test certificate." });

        // Rule 3: Cannot close directly from CorrectiveActionRequired (must be Resolved first)
        var closeError2 = await Assert.ThrowsAsync<NonConformanceException>(() => ncrService.CloseAsync(ncr.Id));
        Assert.Equal(409, closeError2.StatusCode);

        // Resolve -> moves to Resolved
        await ncrService.ResolveAsync(ncr.Id);

        // Close -> moves to Closed
        var closed = await ncrService.CloseAsync(ncr.Id);
        Assert.Equal(NonConformanceStatus.Closed, closed.Status);

        // Rule 4: Cannot edit corrective action on Closed NCR
        var editClosedError = await Assert.ThrowsAsync<NonConformanceException>(() =>
            ncrService.UpdateCorrectiveActionAsync(ncr.Id, new UpdateCorrectiveActionDto { CorrectiveAction = "New action" }));
        Assert.Equal(409, editClosedError.StatusCode);

        // Rule 5: Cannot re-resolve Closed NCR
        var reResolveError = await Assert.ThrowsAsync<NonConformanceException>(() => ncrService.ResolveAsync(ncr.Id));
        Assert.Equal(409, reResolveError.StatusCode);
    }

    [Fact]
    public async Task QualityInspection_Rejects_Invalid_Quantity_Combinations()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, deliveryItem, inspector) = await SeedReceivedDeliveryWithDiscrepancyAsync(db);

        var inspectionService = new QualityInspectionService(db);
        var started = await inspectionService.StartInspectionAsync(new StartInspectionDto { DeliveryId = delivery.Id }, inspector.Id);

        // Case 1: Accepted + Rejected exceeds received quantity (240)
        var ex1 = await Assert.ThrowsAsync<QualityInspectionException>(() =>
            inspectionService.CompleteInspectionAsync(started.Id, new CompleteInspectionDto
            {
                OverallDecision = InspectionDecision.PartiallyAccepted,
                Items = [new CompleteInspectionItemDto { DeliveryItemId = deliveryItem.Id, AcceptedQuantity = 240, RejectedQuantity = 5 }]
            }));
        Assert.Equal(400, ex1.StatusCode);

        // Case 2: Negative quantity
        var ex2 = await Assert.ThrowsAsync<QualityInspectionException>(() =>
            inspectionService.CompleteInspectionAsync(started.Id, new CompleteInspectionDto
            {
                OverallDecision = InspectionDecision.Accepted,
                Items = [new CompleteInspectionItemDto { DeliveryItemId = deliveryItem.Id, AcceptedQuantity = -1, RejectedQuantity = 0 }]
            }));
        Assert.Equal(400, ex2.StatusCode);

        // Case 3: Zero accepted and zero rejected
        var ex3 = await Assert.ThrowsAsync<QualityInspectionException>(() =>
            inspectionService.CompleteInspectionAsync(started.Id, new CompleteInspectionDto
            {
                OverallDecision = InspectionDecision.Accepted,
                Items = [new CompleteInspectionItemDto { DeliveryItemId = deliveryItem.Id, AcceptedQuantity = 0, RejectedQuantity = 0 }]
            }));
        Assert.Equal(400, ex3.StatusCode);

        // Case 4: Decision mismatch — Accepted decision with rejected items
        var ex4 = await Assert.ThrowsAsync<QualityInspectionException>(() =>
            inspectionService.CompleteInspectionAsync(started.Id, new CompleteInspectionDto
            {
                OverallDecision = InspectionDecision.Accepted,
                Items = [new CompleteInspectionItemDto { DeliveryItemId = deliveryItem.Id, AcceptedQuantity = 235, RejectedQuantity = 5 }]
            }));
        Assert.Equal(400, ex4.StatusCode);
    }

    [Fact]
    public async Task QualityRiskAgent_Persists_Audit_Steps_And_Handles_Unreachable_Service_Safely()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, deliveryItem, inspector) = await SeedReceivedDeliveryWithDiscrepancyAsync(db);

        var inspectionService = new QualityInspectionService(db);
        var started = await inspectionService.StartInspectionAsync(new StartInspectionDto { DeliveryId = delivery.Id }, inspector.Id);
        var completed = await inspectionService.CompleteInspectionAsync(started.Id, new CompleteInspectionDto
        {
            OverallDecision = InspectionDecision.PartiallyAccepted,
            Items = [new CompleteInspectionItemDto { DeliveryItemId = deliveryItem.Id, AcceptedQuantity = 235, RejectedQuantity = 5 }]
        });

        // Set up QualityRiskAgentService with an unconfigured/unreachable client to test safe failure
        var evidenceService = new QualityRiskEvidenceService(db);
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentService:BaseUrl"] = "http://127.0.0.1:59999", // Unreachable port
                ["AgentService:ApiKey"] = "test-key"
            }).Build();
        var httpClient = new HttpClient();
        var client = new QualityRiskAgentClient(httpClient, config);
        var validator = new QualityRiskRecommendationValidator();
        var agentService = new QualityRiskAgentService(db, evidenceService, client, validator, NullLogger<QualityRiskAgentService>.Instance);

        // Act: Run agent analysis — service is unreachable, must result in safe failure
        var workflowResponse = await agentService.AnalyseAsync(completed.Id, inspector.Id, CancellationToken.None);

        // Assert: Workflow is marked as Failed with auditable steps and no business changes
        Assert.Equal("Failed", workflowResponse.Status);
        Assert.Contains("failed", workflowResponse.FinalOutcome.ToLower());
        Assert.Equal(3, workflowResponse.Steps.Count);

        // Step 1: Collect Evidence succeeded
        Assert.Equal("Completed", workflowResponse.Steps[0].Status);
        Assert.Equal("Collect Quality Evidence", workflowResponse.Steps[0].StepName);

        // Step 2: Agent Analysis failed safely
        Assert.Equal("Failed", workflowResponse.Steps[1].Status);
        Assert.Equal("Run Agentic Quality Analysis", workflowResponse.Steps[1].StepName);
        Assert.NotNull(workflowResponse.Steps[1].Error);

        // Step 3: Validate Recommendation was skipped
        Assert.Equal("Skipped", workflowResponse.Steps[2].Status);

        // Verify DB persistence of the workflow
        var savedWorkflow = await db.AgentWorkflows.Include(w => w.Steps).SingleAsync(w => w.Id == workflowResponse.WorkflowId);
        Assert.Equal(WorkflowStatus.Failed, savedWorkflow.Status);
        Assert.Equal(delivery.Id, savedWorkflow.DeliveryId);
        Assert.Equal(3, savedWorkflow.Steps.Count);
    }

    // ──────── Helpers ────────

    private static async Task<(Delivery delivery, DeliveryItem item, User inspector)> SeedReceivedDeliveryWithDiscrepancyAsync(
        ApplicationDbContext db)
    {
        var inspector = new User
        {
            FullName = "Amara Silva",
            Email = $"inspector-{Guid.NewGuid()}@buildwise.test",
            PasswordHash = "hash",
            IsActive = true
        };
        var supplier = new Supplier { Name = "ABC Building Materials", Status = SupplierStatus.Active };
        var material = new Material { Name = "Portland Cement", Unit = "bags" };
        var project = new Project { Name = "Highway Project", Status = ProjectStatus.Active };

        var po = new PurchaseOrder
        {
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PurchaseOrderStatus.InProgress,
            Supplier = supplier,
            Project = project,
            Items = [new PurchaseOrderItem { Material = material, OrderedQuantity = 250, UnitPrice = 5 }]
        };
        var poItem = po.Items.Single();

        var delivery = new Delivery
        {
            PurchaseOrder = po,
            DeliveryReference = "DEL-2026-001",
            Status = DeliveryStatus.DiscrepancyReported,
            ReceivedAt = DateTime.UtcNow,
            ReceivedByUser = inspector,
            Items = [new DeliveryItem { PurchaseOrderItem = poItem, ReceivedQuantity = 240, DamagedQuantity = 5 }]
        };
        var deliveryItem = delivery.Items.Single();

        db.Users.Add(inspector);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();

        return (delivery, deliveryItem, inspector);
    }
}
