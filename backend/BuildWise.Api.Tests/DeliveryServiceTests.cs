using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildWise.Api.Tests;

public class DeliveryServiceTests
{
    [Fact]
    public async Task RecordDelivery_Requires_ConfirmedPurchaseOrder()
    {
        var scenario = await SeedScenarioAsync(PurchaseOrderStatus.Created);
        var service = new DeliveryService(scenario.Db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordDeliveryAsync(BuildDelivery(scenario)));

        Assert.Contains("Confirmed Purchase Orders", ex.Message);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(10, -1)]
    public async Task RecordDelivery_Rejects_NegativeQuantities(decimal received, decimal damaged)
    {
        var scenario = await SeedScenarioAsync();
        var service = new DeliveryService(scenario.Db);
        var delivery = BuildDelivery(scenario);
        delivery.Items[0].ReceivedQuantity = received;
        delivery.Items[0].DamagedQuantity = damaged;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordDeliveryAsync(delivery));
        Assert.Contains("cannot be negative", ex.Message);
    }

    [Fact]
    public async Task RecordDelivery_Rejects_Damage_Greater_Than_Received()
    {
        var scenario = await SeedScenarioAsync();
        var service = new DeliveryService(scenario.Db);
        var delivery = BuildDelivery(scenario);
        delivery.Items[0].ReceivedQuantity = 10;
        delivery.Items[0].DamagedQuantity = 11;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordDeliveryAsync(delivery));
        Assert.Contains("cannot exceed received", ex.Message);
    }

    [Fact]
    public async Task RecordDelivery_Rejects_Received_Greater_Than_Ordered()
    {
        // Regression: the API accepted 500 received against a 250-unit order and
        // stored it as an ordinary DiscrepancyReported delivery. A supplier cannot
        // deliver more than was ordered, so this must be refused outright.
        var scenario = await SeedScenarioAsync();
        var service = new DeliveryService(scenario.Db);
        var delivery = BuildDelivery(scenario);
        delivery.Items[0].ReceivedQuantity = 500;
        delivery.Items[0].DamagedQuantity = 50;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordDeliveryAsync(delivery));

        Assert.Contains("cannot exceed the ordered quantity", ex.Message);
        // Nothing may be persisted when validation fails.
        Assert.Empty(await scenario.Db.Deliveries.ToListAsync());
    }

    [Fact]
    public async Task RecordDelivery_Rejects_Over_Receipt_By_One_Unit()
    {
        // Boundary: exactly the ordered quantity is valid, one more is not.
        var scenario = await SeedScenarioAsync();
        var service = new DeliveryService(scenario.Db);
        var delivery = BuildDelivery(scenario);
        delivery.Items[0].ReceivedQuantity = 251;
        delivery.Items[0].DamagedQuantity = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordDeliveryAsync(delivery));

        Assert.Empty(await scenario.Db.Deliveries.ToListAsync());
    }

    [Fact]
    public async Task RecordDelivery_Accepts_Received_Exactly_Equal_To_Ordered()
    {
        var scenario = await SeedScenarioAsync();
        var service = new DeliveryService(scenario.Db);

        var created = await service.RecordDeliveryAsync(BuildDelivery(scenario));

        Assert.Equal(DeliveryStatus.Received, created.Status);
        Assert.Single(await scenario.Db.Deliveries.ToListAsync());
    }

    [Fact]
    public async Task RecordDelivery_Flags_Shortage_And_Damage_As_Discrepancy()
    {
        var scenario = await SeedScenarioAsync();
        var service = new DeliveryService(scenario.Db);
        var delivery = BuildDelivery(scenario);
        delivery.Items[0].ReceivedQuantity = 240;
        delivery.Items[0].DamagedQuantity = 5;

        var created = await service.RecordDeliveryAsync(delivery);

        Assert.Equal(DeliveryStatus.DiscrepancyReported, created.Status);
        Assert.Single(await scenario.Db.Deliveries.ToListAsync());
        var issues = await scenario.Db.DeliveryIssues.Where(issue => issue.DeliveryId == created.Id).ToListAsync();
        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, issue => issue.IssueType == DeliveryIssueType.Shortage && issue.Description.Contains("Shortage of 10"));
        Assert.Contains(issues, issue => issue.IssueType == DeliveryIssueType.Damage && issue.Description.Contains("Sent for inspection: 235"));
    }

    [Fact]
    public async Task RecordDelivery_Marks_Full_Undamaged_Receipt_Received()
    {
        var scenario = await SeedScenarioAsync();
        var service = new DeliveryService(scenario.Db);

        var created = await service.RecordDeliveryAsync(BuildDelivery(scenario));

        Assert.Equal(DeliveryStatus.Received, created.Status);
    }

    private static Delivery BuildDelivery(DeliveryScenario scenario) => new()
    {
        PurchaseOrderId = scenario.PurchaseOrder.Id,
        ReceivedByUserId = 7,
        DeliveryReference = "INV-9081",
        Items = new List<DeliveryItem>
        {
            new()
            {
                MaterialId = scenario.Material.Id,
                ReceivedQuantity = 250,
                DamagedQuantity = 0
            }
        }
    };

    [Fact]
    public async Task RecordDelivery_Requires_A_Delivery_Reference()
    {
        var scenario = await SeedScenarioAsync();
        var service = new DeliveryService(scenario.Db);
        var delivery = BuildDelivery(scenario);
        delivery.DeliveryReference = "   ";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordDeliveryAsync(delivery));

        Assert.Contains("delivery reference is required", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await scenario.Db.Deliveries.ToListAsync());
    }

    [Fact]
    public async Task RecordDelivery_Rejects_Cumulative_Over_Receipt_Across_Partial_Deliveries()
    {
        // Partial deliveries are normal, so the per-delivery check alone lets a
        // supplier deliver the full 250 twice. The cumulative guard must refuse the
        // second delivery.
        var scenario = await SeedScenarioAsync();
        var service = new DeliveryService(scenario.Db);

        var first = BuildDelivery(scenario);
        first.DeliveryReference = "INV-1";
        first.Items[0].ReceivedQuantity = 200;
        await service.RecordDeliveryAsync(first);

        var second = BuildDelivery(scenario);
        second.DeliveryReference = "INV-2";
        second.Items[0].ReceivedQuantity = 100; // 200 + 100 = 300 > 250 ordered

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordDeliveryAsync(second));

        Assert.Contains("Cumulative received quantity cannot exceed", ex.Message);
    }

    [Fact]
    public async Task RecordDelivery_Allows_Partial_Deliveries_That_Cumulatively_Fit()
    {
        // The boundary case for the cumulative guard: 200 + 50 is exactly the
        // ordered quantity, so it must be accepted.
        var scenario = await SeedScenarioAsync();
        var service = new DeliveryService(scenario.Db);

        var first = BuildDelivery(scenario);
        first.DeliveryReference = "INV-1";
        first.Items[0].ReceivedQuantity = 200;
        await service.RecordDeliveryAsync(first);

        var second = BuildDelivery(scenario);
        second.DeliveryReference = "INV-2";
        second.Items[0].ReceivedQuantity = 50;

        var created = await service.RecordDeliveryAsync(second);

        Assert.Equal(2, await scenario.Db.Deliveries.CountAsync());
        Assert.Equal("INV-2", created.DeliveryReference);
    }

    private static async Task<DeliveryScenario> SeedScenarioAsync(
        PurchaseOrderStatus status = PurchaseOrderStatus.Confirmed)
    {
        var db = TestDbFactory.CreateInMemory();
        var project = new Project
        {
            Name = "Riverside Apartments",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var material = new Material
        {
            Name = "OPC Cement",
            Unit = "Bags",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AddRange(project, material);
        await db.SaveChangesAsync();

        var purchaseOrder = new PurchaseOrder
        {
            ProjectId = project.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpectedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = status,
            TotalAmount = 525000,
            Items = new List<PurchaseOrderItem>
            {
                new() { MaterialId = material.Id, OrderedQuantity = 250, UnitPrice = 2100 }
            }
        };
        db.PurchaseOrders.Add(purchaseOrder);
        await db.SaveChangesAsync();

        return new DeliveryScenario(db, purchaseOrder, material);
    }

    private sealed record DeliveryScenario(
        ApplicationDbContext Db,
        PurchaseOrder PurchaseOrder,
        Material Material);
}
