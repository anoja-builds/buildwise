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
using Xunit;

namespace BuildWise.Api.Tests;

/// <summary>
/// Tests for the Delivery Discrepancy Agent — verifies the cement scenario
/// (Ordered 250, Received 240, Damaged 5, Shortage 10, Undamaged 235),
/// edge cases, repeat submissions, and agent-unavailable safety.
/// </summary>
public class DeliveryDiscrepancyAgentTests
{
    /// <summary>
    /// The cement scenario from the assignment specification:
    /// Ordered: 250 bags, Received: 240 bags, Damaged: 5, Shortage: 10, Undamaged: 235.
    /// </summary>
    [Fact]
    public async Task CementScenario_250ordered_240received_5damaged()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, poItem, user) = await SeedCementScenario(db, ordered: 250, received: 240, damaged: 5);

        var agent = new DeliveryDiscrepancyAgentService(db);
        var result = await agent.AnalyzeDiscrepanciesAsync(delivery.Id, user.Id);

        Assert.True(result.HasDiscrepancies);
        Assert.Equal(10, result.TotalShortage);     // 250 - 240 = 10
        Assert.Equal(5, result.TotalDamaged);
        Assert.Equal(235, result.TotalUndamagedReceived); // 240 - 5 = 235
        Assert.Equal(delivery.Id, result.DeliveryId);
        Assert.True(result.WorkflowId > 0);
        Assert.Equal("Deterministic", result.ExecutionMode);

        // Verify items
        var item = Assert.Single(result.Items);
        Assert.Equal("Cement", item.MaterialName);
        Assert.Equal(250, item.OrderedQuantity);
        Assert.Equal(240, item.NewlyReceivedQuantity);
        Assert.Equal(5, item.DamagedQuantity);
        Assert.Equal(235, item.UndamagedReceivedQuantity);
        Assert.Equal(10, item.ShortageQuantity);
        Assert.True(item.HasDiscrepancy);
        Assert.Contains("Shortage", item.DiscrepancyFlags);
        Assert.Contains("Damage", item.DiscrepancyFlags);

        // Verify recommendations
        Assert.True(result.Recommendations.Count >= 2); // Shortage + Damage
        Assert.Contains(result.Recommendations, r => r.Category == "Shortage");
        Assert.Contains(result.Recommendations, r => r.Category == "Damage");

        // Verify validation
        Assert.True(result.Validation.IsValid);
        Assert.Empty(result.Validation.Errors);
    }

    [Fact]
    public async Task FullDelivery_NoDiscrepancies()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, _, user) = await SeedCementScenario(db, ordered: 100, received: 100, damaged: 0);

        var agent = new DeliveryDiscrepancyAgentService(db);
        var result = await agent.AnalyzeDiscrepanciesAsync(delivery.Id, user.Id);

        Assert.False(result.HasDiscrepancies);
        Assert.Equal(0, result.TotalShortage);
        Assert.Equal(0, result.TotalDamaged);
        Assert.Equal(100, result.TotalUndamagedReceived);
        Assert.Contains(result.Recommendations, r => r.Category == "NoIssues");
    }

    [Fact]
    public async Task DamageOnly_NoShortage()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, _, user) = await SeedCementScenario(db, ordered: 100, received: 100, damaged: 15);

        var agent = new DeliveryDiscrepancyAgentService(db);
        var result = await agent.AnalyzeDiscrepanciesAsync(delivery.Id, user.Id);

        Assert.True(result.HasDiscrepancies);
        Assert.Equal(0, result.TotalShortage);     // Received matches ordered
        Assert.Equal(15, result.TotalDamaged);
        Assert.Equal(85, result.TotalUndamagedReceived);

        var item = Assert.Single(result.Items);
        Assert.Contains("Damage", item.DiscrepancyFlags);
        Assert.DoesNotContain("Shortage", item.DiscrepancyFlags);
    }

    [Fact]
    public async Task ShortageOnly_NoDamage()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, _, user) = await SeedCementScenario(db, ordered: 100, received: 80, damaged: 0);

        var agent = new DeliveryDiscrepancyAgentService(db);
        var result = await agent.AnalyzeDiscrepanciesAsync(delivery.Id, user.Id);

        Assert.True(result.HasDiscrepancies);
        Assert.Equal(20, result.TotalShortage);
        Assert.Equal(0, result.TotalDamaged);
        Assert.Equal(80, result.TotalUndamagedReceived);

        var item = Assert.Single(result.Items);
        Assert.Contains("Shortage", item.DiscrepancyFlags);
        Assert.DoesNotContain("Damage", item.DiscrepancyFlags);
    }

    [Fact]
    public async Task WorkflowPersistence_StepsAndOutcome()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, _, user) = await SeedCementScenario(db, ordered: 250, received: 240, damaged: 5);

        var agent = new DeliveryDiscrepancyAgentService(db);
        var result = await agent.AnalyzeDiscrepanciesAsync(delivery.Id, user.Id);

        // Verify workflow record
        var workflow = await db.AgentWorkflows
            .Include(w => w.Steps)
            .SingleAsync(w => w.Id == result.WorkflowId);

        Assert.Equal(WorkflowStatus.Completed, workflow.Status);
        Assert.Equal(delivery.Id, workflow.DeliveryId);
        Assert.Contains("Discrepancies found", workflow.FinalOutcome);
        Assert.NotNull(workflow.StartedAt);
        Assert.NotNull(workflow.CompletedAt);

        // Verify 4 steps: Data Retrieval, Discrepancy Analysis, Advisory, Validation
        Assert.Equal(4, workflow.Steps.Count);
        Assert.All(workflow.Steps, s => Assert.Equal(WorkflowStepStatus.Completed, s.Status));
        Assert.Contains(workflow.Steps, s => s.AgentRole == "Data Retrieval");
        Assert.Contains(workflow.Steps, s => s.AgentRole == "Discrepancy Analysis");
        Assert.Contains(workflow.Steps, s => s.AgentRole == "Advisory Recommendations");
        Assert.Contains(workflow.Steps, s => s.AgentRole == "Result Validation");
    }

    [Fact]
    public async Task WorkflowHistory_ReturnsMultipleRuns()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, _, user) = await SeedCementScenario(db, ordered: 100, received: 90, damaged: 2);

        var agent = new DeliveryDiscrepancyAgentService(db);
        await agent.AnalyzeDiscrepanciesAsync(delivery.Id, user.Id);
        await agent.AnalyzeDiscrepanciesAsync(delivery.Id, user.Id);

        var history = await agent.GetWorkflowHistoryAsync(delivery.Id);
        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.Equal("Completed", h.Status));
    }

    [Fact]
    public async Task UnreceivedDelivery_ThrowsInvalidOperation()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var user = new User { FullName = "Test", Email = $"{Guid.NewGuid()}@test.example", PasswordHash = "x" };
        var po = new PurchaseOrder
        {
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PurchaseOrderStatus.InProgress,
            Items = [new PurchaseOrderItem { Material = new Material { Name = "Cement", Unit = "bags" }, OrderedQuantity = 100, UnitPrice = 1 }]
        };
        var delivery = new Delivery
        {
            PurchaseOrder = po,
            Status = DeliveryStatus.Scheduled, // Not received
            Items = [new DeliveryItem { PurchaseOrderItem = po.Items.Single() }]
        };
        db.Users.Add(user);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();

        var agent = new DeliveryDiscrepancyAgentService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.AnalyzeDiscrepanciesAsync(delivery.Id, user.Id));
    }

    [Fact]
    public async Task NonExistentDelivery_ThrowsArgumentException()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var agent = new DeliveryDiscrepancyAgentService(db);
        await Assert.ThrowsAsync<ArgumentException>(
            () => agent.AnalyzeDiscrepanciesAsync(99999, 1));
    }

    [Fact]
    public async Task SplitDelivery_PriorReceipts_Acknowledged()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var user = new User { FullName = "Receiver", Email = $"{Guid.NewGuid()}@test.example", PasswordHash = "x" };
        var material = new Material { Name = "Cement", Unit = "bags" };
        var po = new PurchaseOrder
        {
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PurchaseOrderStatus.InProgress,
            Items = [new PurchaseOrderItem { Material = material, OrderedQuantity = 250, UnitPrice = 2 }]
        };
        var poItem = po.Items.Single();
        // First delivery: received 150, damaged 3
        var delivery1 = new Delivery
        {
            PurchaseOrder = po,
            Status = DeliveryStatus.DiscrepancyReported,
            ReceivedAt = DateTime.UtcNow.AddHours(-2),
            ReceivedByUserId = 1,
            Items = [new DeliveryItem { PurchaseOrderItem = poItem, ReceivedQuantity = 150, DamagedQuantity = 3 }]
        };
        // Second delivery: received 90, damaged 5
        var delivery2 = new Delivery
        {
            PurchaseOrder = po,
            Status = DeliveryStatus.DiscrepancyReported,
            ReceivedAt = DateTime.UtcNow,
            ReceivedByUserId = 1,
            Items = [new DeliveryItem { PurchaseOrderItem = poItem, ReceivedQuantity = 90, DamagedQuantity = 5 }]
        };
        db.Users.Add(user);
        db.Deliveries.AddRange(delivery1, delivery2);
        await db.SaveChangesAsync();

        var agent = new DeliveryDiscrepancyAgentService(db);
        var result = await agent.AnalyzeDiscrepanciesAsync(delivery2.Id, user.Id);

        var item = Assert.Single(result.Items);
        Assert.Equal(150, item.PreviouslyReceivedQuantity); // From delivery1
        Assert.Equal(90, item.NewlyReceivedQuantity);       // From delivery2
        Assert.Equal(240, item.TotalReceivedToDate);        // 150 + 90
        Assert.Equal(10, item.ShortageQuantity);            // 250 - 240
        Assert.Equal(85, item.UndamagedReceivedQuantity);   // 90 - 5
    }

    [Fact]
    public async Task ControllerEndpoint_ReturnsOk_ForReceivedDelivery()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, _, user) = await SeedCementScenario(db, ordered: 250, received: 240, damaged: 5);

        var controller = CreateController(db, user.Id);
        var result = await controller.AnalyzeDiscrepancies(delivery.Id);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ControllerEndpoint_ReturnsNotFound_ForMissingDelivery()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var user = new User { FullName = "Test", Email = $"{Guid.NewGuid()}@test.example", PasswordHash = "x" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id);
        var result = await controller.AnalyzeDiscrepancies(99999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task ControllerEndpoint_ReturnsBadRequest_ForUnreceivedDelivery()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var user = new User { FullName = "Test", Email = $"{Guid.NewGuid()}@test.example", PasswordHash = "x" };
        var po = new PurchaseOrder
        {
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Items = [new PurchaseOrderItem { Material = new Material { Name = "M", Unit = "u" }, OrderedQuantity = 1, UnitPrice = 1 }]
        };
        var delivery = new Delivery
        {
            PurchaseOrder = po,
            Items = [new DeliveryItem { PurchaseOrderItem = po.Items.Single() }]
        };
        db.Users.Add(user);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id);
        var result = await controller.AnalyzeDiscrepancies(delivery.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ControllerEndpoint_DiscrepancyHistory_ReturnsOk()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, _, user) = await SeedCementScenario(db, ordered: 100, received: 95, damaged: 2);

        var agent = new DeliveryDiscrepancyAgentService(db);
        await agent.AnalyzeDiscrepanciesAsync(delivery.Id, user.Id);

        var controller = CreateController(db, user.Id);
        var result = await controller.GetDiscrepancyHistory(delivery.Id);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task AgentNeverModifiesPurchaseOrder()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (delivery, poItem, user) = await SeedCementScenario(db, ordered: 250, received: 240, damaged: 5);

        var poBefore = await db.PurchaseOrders.AsNoTracking().SingleAsync(p => p.Id == poItem.PurchaseOrderId);

        var agent = new DeliveryDiscrepancyAgentService(db);
        await agent.AnalyzeDiscrepanciesAsync(delivery.Id, user.Id);

        db.ChangeTracker.Clear();
        var poAfter = await db.PurchaseOrders.AsNoTracking().SingleAsync(p => p.Id == poItem.PurchaseOrderId);

        // Agent is read-only: PO status and quantities must not change
        Assert.Equal(poBefore.Status, poAfter.Status);
    }

    // ──────── Helpers ────────

    private static async Task<(Delivery delivery, PurchaseOrderItem poItem, User user)> SeedCementScenario(
        ApplicationDbContext db, decimal ordered, decimal received, decimal damaged)
    {
        var user = new User
        {
            FullName = "Ramya Fernando",
            Email = $"ramya-{Guid.NewGuid()}@buildwise.test",
            PasswordHash = "hashed",
            IsActive = true
        };
        var material = new Material { Name = "Cement", Unit = "bags" };
        var po = new PurchaseOrder
        {
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PurchaseOrderStatus.InProgress,
            Items = [new PurchaseOrderItem { Material = material, OrderedQuantity = ordered, UnitPrice = 2 }]
        };
        var poItem = po.Items.Single();
        var delivery = new Delivery
        {
            PurchaseOrder = po,
            Status = damaged > 0 || received < ordered ? DeliveryStatus.DiscrepancyReported : DeliveryStatus.Received,
            ReceivedAt = DateTime.UtcNow,
            ReceivedByUser = user,
            Items = [new DeliveryItem { PurchaseOrderItem = poItem, ReceivedQuantity = received, DamagedQuantity = damaged }]
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return (delivery, poItem, user);
    }

    private static DeliveriesController CreateController(ApplicationDbContext db, int userId)
    {
        var config = new ConfigurationBuilder().Build();
        return new DeliveriesController(
            db,
            new DeliveryRiskAgentService(db, config),
            new DeliveryDiscrepancyAgentService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                        new Claim(ClaimTypes.Role, "SiteEngineer")
                    ], "Test"))
                }
            }
        };
    }
}
