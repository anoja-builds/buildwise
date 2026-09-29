using BuildWise.Api.Data;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildWise.Api.Tests;

public class QualityReinspectionTests
{
    private static async Task<(ApplicationDbContext Db, Delivery Delivery, User Inspector)> SeedScenario(string testName)
    {
        var db = TestDbFactory.CreateInMemory(testName);
        var inspectorRole = await db.Roles.FirstAsync(r => r.Name == "QualityInspector");

        var inspector = new User
        {
            Id = 5,
            FullName = "Quinn Inspector",
            Email = "quality.inspector@buildwise.demo",
            IsActive = true
        };
        inspector.UserRoles.Add(new UserRole { RoleId = inspectorRole.Id });
        db.Users.Add(inspector);

        var project = new Project { Id = 1, Name = "Test Project", Status = ProjectStatus.Active };
        var material = new Material { Id = 1, Name = "Cement (50kg bag)", Unit = "bag", IsActive = true };
        var supplier = new Supplier { Id = 1, Name = "Supplier A", Status = SupplierStatus.Active };
        db.Projects.Add(project);
        db.Materials.Add(material);
        db.Suppliers.Add(supplier);

        var po = new PurchaseOrder
        {
            Id = 1,
            ProjectId = 1,
            SupplierId = 1,
            Status = PurchaseOrderStatus.Confirmed,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        db.PurchaseOrders.Add(po);

        var poItem = new PurchaseOrderItem
        {
            Id = 1,
            PurchaseOrderId = 1,
            MaterialId = 1,
            OrderedQuantity = 100,
            UnitPrice = 1000
        };
        db.PurchaseOrderItems.Add(poItem);

        var delivery = new Delivery
        {
            Id = 10,
            PurchaseOrderId = 1,
            DeliveryReference = "DEL-TEST-001",
            Status = DeliveryStatus.Received,
            ReceivedAt = DateTime.UtcNow
        };
        db.Deliveries.Add(delivery);

        var di = new DeliveryItem
        {
            Id = 10,
            DeliveryId = 10,
            PurchaseOrderItemId = 1,
            ReceivedQuantity = 50,
            DamagedQuantity = 0
        };
        db.DeliveryItems.Add(di);

        await db.SaveChangesAsync();
        return (db, delivery, inspector);
    }

    [Fact]
    public async Task No_previous_inspection_can_start_inspection()
    {
        var (db, delivery, inspector) = await SeedScenario(nameof(No_previous_inspection_can_start_inspection));
        await using (db)
        {
            var service = new QualityInspectionService(db);
            var started = await service.StartInspectionAsync(new StartInspectionDto
            {
                DeliveryId = delivery.Id,
                Notes = "Initial inspection"
            }, inspector.Id);

            Assert.NotNull(started);
            Assert.Equal(InspectionStatus.UnderInspection, started.Status);
            Assert.Equal(delivery.Id, started.DeliveryId);
        }
    }

    [Fact]
    public async Task Existing_UnderInspection_inspection_returns_conflict()
    {
        var (db, delivery, inspector) = await SeedScenario(nameof(Existing_UnderInspection_inspection_returns_conflict));
        await using (db)
        {
            var service = new QualityInspectionService(db);
            await service.StartInspectionAsync(new StartInspectionDto
            {
                DeliveryId = delivery.Id,
                Notes = "Initial inspection"
            }, inspector.Id);

            // Attempt second start while first is UnderInspection
            var ex = await Assert.ThrowsAsync<QualityInspectionException>(() =>
                service.StartInspectionAsync(new StartInspectionDto
                {
                    DeliveryId = delivery.Id,
                    Notes = "Concurrent attempt"
                }, inspector.Id));

            Assert.Equal(409, ex.StatusCode);
            Assert.Equal("This delivery already has an active inspection.", ex.Message);
        }
    }

    [Fact]
    public async Task Completed_Accepted_inspection_cannot_start_another_inspection()
    {
        var (db, delivery, inspector) = await SeedScenario(nameof(Completed_Accepted_inspection_cannot_start_another_inspection));
        await using (db)
        {
            var service = new QualityInspectionService(db);
            var started = await service.StartInspectionAsync(new StartInspectionDto
            {
                DeliveryId = delivery.Id
            }, inspector.Id);

            await service.CompleteInspectionAsync(started.Id, new CompleteInspectionDto
            {
                OverallDecision = InspectionDecision.Accepted,
                Items = [new CompleteInspectionItemDto { DeliveryItemId = 10, AcceptedQuantity = 50, Condition = "Good" }]
            });

            var ex = await Assert.ThrowsAsync<QualityInspectionException>(() =>
                service.StartInspectionAsync(new StartInspectionDto
                {
                    DeliveryId = delivery.Id,
                    Notes = "Attempting re-inspection after full acceptance"
                }, inspector.Id));

            Assert.Equal(409, ex.StatusCode);
            Assert.Equal("This delivery has already been fully inspected and accepted.", ex.Message);
        }
    }

    [Fact]
    public async Task Completed_PartiallyAccepted_inspection_can_start_reinspection()
    {
        var (db, delivery, inspector) = await SeedScenario(nameof(Completed_PartiallyAccepted_inspection_can_start_reinspection));
        await using (db)
        {
            var service = new QualityInspectionService(db);
            var started = await service.StartInspectionAsync(new StartInspectionDto
            {
                DeliveryId = delivery.Id
            }, inspector.Id);

            await service.CompleteInspectionAsync(started.Id, new CompleteInspectionDto
            {
                OverallDecision = InspectionDecision.PartiallyAccepted,
                Items = [new CompleteInspectionItemDto
                {
                    DeliveryItemId = 10,
                    AcceptedQuantity = 40,
                    RejectedQuantity = 10,
                    Condition = "Partial damage"
                }]
            });

            // Re-inspection start should succeed
            var reinspection = await service.StartInspectionAsync(new StartInspectionDto
            {
                DeliveryId = delivery.Id,
                Notes = "Re-inspection after replacement of 10 rejected bags"
            }, inspector.Id);

            Assert.NotNull(reinspection);
            Assert.Equal(InspectionStatus.UnderInspection, reinspection.Status);
            Assert.NotEqual(started.Id, reinspection.Id);

            var all = await db.Inspections.Where(i => i.DeliveryId == delivery.Id).ToListAsync();
            Assert.Equal(2, all.Count);
        }
    }

    [Fact]
    public async Task Completed_Rejected_inspection_can_start_reinspection()
    {
        var (db, delivery, inspector) = await SeedScenario(nameof(Completed_Rejected_inspection_can_start_reinspection));
        await using (db)
        {
            var service = new QualityInspectionService(db);
            var started = await service.StartInspectionAsync(new StartInspectionDto
            {
                DeliveryId = delivery.Id
            }, inspector.Id);

            await service.CompleteInspectionAsync(started.Id, new CompleteInspectionDto
            {
                OverallDecision = InspectionDecision.Rejected,
                Items = [new CompleteInspectionItemDto
                {
                    DeliveryItemId = 10,
                    AcceptedQuantity = 0,
                    RejectedQuantity = 50,
                    Condition = "Severe water damage"
                }]
            });

            // Re-inspection start should succeed
            var reinspection = await service.StartInspectionAsync(new StartInspectionDto
            {
                DeliveryId = delivery.Id,
                Notes = "Re-inspecting after supplier rectifies batch"
            }, inspector.Id);

            Assert.NotNull(reinspection);
            Assert.Equal(InspectionStatus.UnderInspection, reinspection.Status);
        }
    }

    [Fact]
    public async Task Pending_deliveries_query_handles_reinspection_eligibility_correctly()
    {
        var db = TestDbFactory.CreateInMemory(nameof(Pending_deliveries_query_handles_reinspection_eligibility_correctly));
        await using (db)
        {
            var inspectorRole = await db.Roles.FirstAsync(r => r.Name == "QualityInspector");
            var inspector = new User { Id = 5, FullName = "Quinn", Email = "qi@demo.com", IsActive = true };
            inspector.UserRoles.Add(new UserRole { RoleId = inspectorRole.Id });
            db.Users.Add(inspector);

            var po = new PurchaseOrder { Id = 1, Status = PurchaseOrderStatus.Confirmed, OrderDate = DateOnly.FromDateTime(DateTime.UtcNow) };
            var poItem = new PurchaseOrderItem { Id = 1, PurchaseOrderId = 1, OrderedQuantity = 100, UnitPrice = 100 };
            db.PurchaseOrders.Add(po);
            db.PurchaseOrderItems.Add(poItem);

            // D1: Never inspected -> should be in pending
            var d1 = new Delivery { Id = 1, PurchaseOrderId = 1, Status = DeliveryStatus.Received, DeliveryReference = "D1", ReceivedAt = DateTime.UtcNow };
            var di1 = new DeliveryItem { Id = 1, DeliveryId = 1, PurchaseOrderItemId = 1, ReceivedQuantity = 50 };

            // D2: Active UnderInspection -> should be EXCLUDED
            var d2 = new Delivery { Id = 2, PurchaseOrderId = 1, Status = DeliveryStatus.Received, DeliveryReference = "D2", ReceivedAt = DateTime.UtcNow };
            var di2 = new DeliveryItem { Id = 2, DeliveryId = 2, PurchaseOrderItemId = 1, ReceivedQuantity = 50 };

            // D3: Completed Accepted -> should be EXCLUDED
            var d3 = new Delivery { Id = 3, PurchaseOrderId = 1, Status = DeliveryStatus.Received, DeliveryReference = "D3", ReceivedAt = DateTime.UtcNow };
            var di3 = new DeliveryItem { Id = 3, DeliveryId = 3, PurchaseOrderItemId = 1, ReceivedQuantity = 50 };

            // D4: Completed PartiallyAccepted -> should be INCLUDED
            var d4 = new Delivery { Id = 4, PurchaseOrderId = 1, Status = DeliveryStatus.Received, DeliveryReference = "D4", ReceivedAt = DateTime.UtcNow };
            var di4 = new DeliveryItem { Id = 4, DeliveryId = 4, PurchaseOrderItemId = 1, ReceivedQuantity = 50 };

            db.Deliveries.AddRange(d1, d2, d3, d4);
            db.DeliveryItems.AddRange(di1, di2, di3, di4);
            await db.SaveChangesAsync();

            var service = new QualityInspectionService(db);

            // Set up D2 active inspection
            await service.StartInspectionAsync(new StartInspectionDto { DeliveryId = 2 }, inspector.Id);

            // Set up D3 completed accepted inspection
            var insp3 = await service.StartInspectionAsync(new StartInspectionDto { DeliveryId = 3 }, inspector.Id);
            await service.CompleteInspectionAsync(insp3.Id, new CompleteInspectionDto
            {
                OverallDecision = InspectionDecision.Accepted,
                Items = [new CompleteInspectionItemDto { DeliveryItemId = 3, AcceptedQuantity = 50, Condition = "Good" }]
            });

            // Set up D4 completed partially accepted inspection
            var insp4 = await service.StartInspectionAsync(new StartInspectionDto { DeliveryId = 4 }, inspector.Id);
            await service.CompleteInspectionAsync(insp4.Id, new CompleteInspectionDto
            {
                OverallDecision = InspectionDecision.PartiallyAccepted,
                Items = [new CompleteInspectionItemDto { DeliveryItemId = 4, AcceptedQuantity = 30, RejectedQuantity = 20, Condition = "Damaged" }]
            });

            var pending = await service.GetPendingDeliveriesAsync();
            var pendingIds = pending.Select(p => p.DeliveryId).OrderBy(id => id).ToList();

            // Expected: D1 (never inspected) and D4 (partially accepted, eligible for re-inspection)
            Assert.Equal([1, 4], pendingIds);
            Assert.DoesNotContain(2, pendingIds); // Active inspection excluded
            Assert.DoesNotContain(3, pendingIds); // Fully accepted excluded
        }
    }
}
