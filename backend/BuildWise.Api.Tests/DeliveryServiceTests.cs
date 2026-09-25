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
