using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BuildWise.Tests;

public class DeliveryTests
{
    private ApplicationDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task ReconcileDelivery_WithShortageAndDamage_FlagsDiscrepancy()
    {
        // Arrange
        using var context = GetInMemoryDbContext();
        
        var user = new User { Id = 1, FullName = "Ramya Fernando", Email = "ramya@buildwise.lk" };
        var project = new Project { Id = 1, Name = "Apartment Development - Colombo", Status = ProjectStatus.Active };
        var supplier = new Supplier { Id = 1, Name = "ABC Building Materials", Status = SupplierStatus.Active };
        var material = new Material { Id = 1, Name = "OPC Cement", Unit = "bags" };
        
        await context.Users.AddAsync(user);
        await context.Projects.AddAsync(project);
        await context.Suppliers.AddAsync(supplier);
        await context.Materials.AddAsync(material);
        await context.SaveChangesAsync();

        var po = new PurchaseOrder
        {
            Id = 1,
            SupplierId = supplier.Id,
            ProjectId = project.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            TotalAmount = 1000m,
            Status = PurchaseOrderStatus.InProgress
        };
        await context.PurchaseOrders.AddAsync(po);
        await context.SaveChangesAsync();

        var poItem = new PurchaseOrderItem
        {
            Id = 1,
            PurchaseOrderId = po.Id,
            MaterialId = material.Id,
            OrderedQuantity = 500m,
            UnitPrice = 2m
        };
        await context.PurchaseOrderItems.AddAsync(poItem);
        await context.SaveChangesAsync();

        var delivery = new Delivery
        {
            Id = 1,
            PurchaseOrderId = po.Id,
            Status = DeliveryStatus.Scheduled
        };
        await context.Deliveries.AddAsync(delivery);
        await context.SaveChangesAsync();

        var deliveryItem = new DeliveryItem
        {
            Id = 1,
            DeliveryId = delivery.Id,
            PurchaseOrderItemId = poItem.Id,
            ReceivedQuantity = 0,
            DamagedQuantity = 0
        };
        await context.DeliveryItems.AddAsync(deliveryItem);
        await context.SaveChangesAsync();

        // Act
        // Reconcile: Ordered 500, Received 480, Damaged 10
        delivery.ReceivedByUserId = user.Id;
        delivery.ActualArrivalDate = DateTime.UtcNow;
        delivery.ReceivedAt = DateTime.UtcNow;

        deliveryItem.ReceivedQuantity = 480m;
        deliveryItem.DamagedQuantity = 10m;

        var shortage = poItem.OrderedQuantity - deliveryItem.ReceivedQuantity; // 20
        var sentForInspection = deliveryItem.ReceivedQuantity - deliveryItem.DamagedQuantity; // 470

        if (shortage > 0 || deliveryItem.DamagedQuantity > 0)
        {
            delivery.Status = DeliveryStatus.DiscrepancyReported;
        }

        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(DeliveryStatus.DiscrepancyReported, delivery.Status);
        Assert.Equal(20m, shortage);
        Assert.Equal(470m, sentForInspection);
        Assert.Equal(10m, deliveryItem.DamagedQuantity);
    }

    [Fact]
    public async Task EvaluateDeliveryRisk_PromisedDateAfterRequiredDate_ReturnsHighRisk()
    {
        // Arrange
        using var context = GetInMemoryDbContext();
        
        var project = new Project { Id = 1, Name = "Apartment Development - Colombo", Status = ProjectStatus.Active };
        var supplier = new Supplier { Id = 1, Name = "ABC Building Materials", Status = SupplierStatus.Active };
        var material = new Material { Id = 1, Name = "OPC Cement", Unit = "bags" };
        
        await context.Projects.AddAsync(project);
        await context.Suppliers.AddAsync(supplier);
        await context.Materials.AddAsync(material);
        await context.SaveChangesAsync();

        // Order date = Aug 20. Expected Delivery = Aug 28 (which is after required date of Aug 25)
        var po = new PurchaseOrder
        {
            Id = 1,
            SupplierId = supplier.Id,
            ProjectId = project.Id,
            OrderDate = new DateOnly(2026, 08, 20),
            ExpectedDeliveryDate = new DateOnly(2026, 08, 28), // 3 days late
            TotalAmount = 1000m,
            Status = PurchaseOrderStatus.Created
        };
        await context.PurchaseOrders.AddAsync(po);
        await context.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build(); // No Gemini API key config
        var riskAgent = new DeliveryRiskAgentService(context, config);

        // Act
        var assessment = await riskAgent.EvaluateDeliveryRiskAsync(po.Id);

        // Assert
        Assert.Equal("High", assessment.RiskLevel);
        Assert.Equal(3, assessment.DelayDays);
        Assert.Contains("Committed delivery is 3 days late", assessment.Reasons[0]);
    }
}
