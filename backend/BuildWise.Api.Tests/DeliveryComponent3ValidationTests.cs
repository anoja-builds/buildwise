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

public class DeliveryComponent3ValidationTests
{
    private static DeliveriesController CreateController(ApplicationDbContext db, int actorId = 101, string role = "SiteEngineer")
    {
        var config = new ConfigurationBuilder().Build();
        var riskAgent = new DeliveryRiskAgentService(db, config);
        var discrepancyAgent = new DeliveryDiscrepancyAgentService(db);
        var controller = new DeliveriesController(db, riskAgent, discrepancyAgent);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorId.ToString()),
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

    private static async Task<(Project Project, Material Material, PurchaseOrder Po, PurchaseOrderItem PoItem, User Engineer)> SeedDeliveryBaseAsync(ApplicationDbContext db)
    {
        var user = new User
        {
            FullName = "Site Engineer 1",
            Email = "site_eng@buildwise.test",
            PasswordHash = "hash"
        };
        db.Users.Add(user);

        var project = new Project
        {
            Name = "Terminal 3 Airport Project",
            Location = "Zone 4",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Projects.Add(project);

        var material = new Material
        {
            Name = "Ready-Mix Concrete Grade 35",
            Unit = "m3",
            Category = "Concrete",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var po = new PurchaseOrder
        {
            ProjectId = project.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PurchaseOrderStatus.Confirmed,
            TotalAmount = 100000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();

        var poItem = new PurchaseOrderItem
        {
            PurchaseOrderId = po.Id,
            MaterialId = material.Id,
            OrderedQuantity = 100m,
            UnitPrice = 1000m
        };
        db.PurchaseOrderItems.Add(poItem);
        await db.SaveChangesAsync();

        return (project, material, po, poItem, user);
    }

    // =========================================================================
    // 1. DELIVERY SCHEDULING VALIDATIONS
    // =========================================================================

    [Fact]
    public async Task ScheduleDelivery_Rejects_When_PO_Not_Found()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var controller = CreateController(db, role: "ProcurementOfficer");

        var result = await controller.ScheduleDelivery(new CreateDeliveryDto
        {
            PurchaseOrderId = 99999,
            DeliveryReference = "DEL-001"
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Purchase Order not found.", badRequest.Value);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Created)]
    [InlineData(PurchaseOrderStatus.Completed)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public async Task ScheduleDelivery_Rejects_When_PO_Status_Not_Allowed(PurchaseOrderStatus invalidStatus)
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, _, _) = await SeedDeliveryBaseAsync(db);
        po.Status = invalidStatus;
        await db.SaveChangesAsync();

        var controller = CreateController(db, role: "ProcurementOfficer");
        var result = await controller.ScheduleDelivery(new CreateDeliveryDto
        {
            PurchaseOrderId = po.Id,
            DeliveryReference = "DEL-002"
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Only confirmed or in-progress purchase orders can be scheduled.", badRequest.Value);
    }

    [Fact]
    public async Task ScheduleDelivery_Rejects_When_PO_Has_No_Items()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (project, _, _, poItem, _) = await SeedDeliveryBaseAsync(db);
        var emptyPo = new PurchaseOrder
        {
            ProjectId = project.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PurchaseOrderStatus.Confirmed,
            TotalAmount = 0m
        };
        db.PurchaseOrders.Add(emptyPo);
        await db.SaveChangesAsync();

        var controller = CreateController(db, role: "ProcurementOfficer");
        var result = await controller.ScheduleDelivery(new CreateDeliveryDto
        {
            PurchaseOrderId = emptyPo.Id,
            DeliveryReference = "DEL-EMPTY"
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Purchase order has no items to deliver.", badRequest.Value);
    }

    [Fact]
    public async Task ScheduleDelivery_Success_Transitions_PO_To_InProgress()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, _, _) = await SeedDeliveryBaseAsync(db);

        var controller = CreateController(db, role: "ProcurementOfficer");
        var result = await controller.ScheduleDelivery(new CreateDeliveryDto
        {
            PurchaseOrderId = po.Id,
            DeliveryReference = "DEL-VALID-01"
        });

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var delivery = Assert.IsType<Delivery>(created.Value);
        Assert.Equal(DeliveryStatus.Scheduled, delivery.Status);
        Assert.Equal(po.Id, delivery.PurchaseOrderId);

        var updatedPo = await db.PurchaseOrders.FindAsync(po.Id);
        Assert.Equal(PurchaseOrderStatus.InProgress, updatedPo!.Status);
    }

    // =========================================================================
    // 2. DELIVERY SCHEDULE (DATE/TIME) VALIDATIONS
    // =========================================================================

    [Fact]
    public async Task CreateSchedule_Rejects_When_PO_Not_Found()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var controller = CreateController(db, role: "ProcurementOfficer");

        var result = await controller.CreateSchedule(new ScheduleDeliveryDto
        {
            PurchaseOrderId = 99999,
            ScheduledDate = DateTime.UtcNow.AddDays(1)
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Purchase Order not found.", badRequest.Value);
    }

    [Fact]
    public async Task CreateSchedule_Rejects_When_PO_Status_Invalid()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, _, _) = await SeedDeliveryBaseAsync(db);
        po.Status = PurchaseOrderStatus.Cancelled;
        await db.SaveChangesAsync();

        var controller = CreateController(db, role: "ProcurementOfficer");
        var result = await controller.CreateSchedule(new ScheduleDeliveryDto
        {
            PurchaseOrderId = po.Id,
            ScheduledDate = DateTime.UtcNow.AddDays(1)
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Only confirmed or in-progress purchase orders can be scheduled.", badRequest.Value);
    }

    [Fact]
    public async Task CreateSchedule_Rejects_When_ScheduledDate_In_The_Past()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, _, _) = await SeedDeliveryBaseAsync(db);

        var controller = CreateController(db, role: "ProcurementOfficer");
        var result = await controller.CreateSchedule(new ScheduleDeliveryDto
        {
            PurchaseOrderId = po.Id,
            ScheduledDate = DateTime.UtcNow.AddDays(-2)
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Scheduled delivery date cannot be before today.", badRequest.Value);
    }

    [Fact]
    public async Task CreateSchedule_Success_When_Valid()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, _, _) = await SeedDeliveryBaseAsync(db);

        var controller = CreateController(db, role: "ProcurementOfficer");
        var targetDate = DateTime.UtcNow.AddDays(3);
        var result = await controller.CreateSchedule(new ScheduleDeliveryDto
        {
            PurchaseOrderId = po.Id,
            ScheduledDate = targetDate,
            TimeSlot = "Morning 09:00 - 12:00",
            Notes = "Site crane required"
        });

        var okResult = Assert.IsType<OkObjectResult>(result);
        var schedule = Assert.IsType<DeliverySchedule>(okResult.Value);
        Assert.Equal(po.Id, schedule.PurchaseOrderId);
        Assert.Equal(targetDate, schedule.ScheduledDate);
    }

    // =========================================================================
    // 3. RECEIVING AND QUANTITY RECONCILIATION VALIDATIONS
    // =========================================================================

    [Fact]
    public async Task ReceiveDelivery_Rejects_When_Already_Processed()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, poItem, user) = await SeedDeliveryBaseAsync(db);

        var delivery = new Delivery
        {
            PurchaseOrderId = po.Id,
            DeliveryReference = "DEL-DONE-1",
            Status = DeliveryStatus.Received,
            ReceivedAt = DateTime.UtcNow,
            ReceivedByUserId = user.Id
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id, "SiteEngineer");
        var result = await controller.ReceiveDelivery(delivery.Id, new ReceiveDeliveryDto
        {
            ReceivedByUserId = 999, // Should be ignored in favor of JWT
            Items = new List<ReceiveDeliveryItemDto>
            {
                new() { PurchaseOrderItemId = poItem.Id, ReceivedQuantity = 10, DamagedQuantity = 0 }
            }
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("This delivery has already been processed.", badRequest.Value);
    }

    [Fact]
    public async Task ReceiveDelivery_Rejects_When_PO_Status_Not_Allowed()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, poItem, user) = await SeedDeliveryBaseAsync(db);
        po.Status = PurchaseOrderStatus.Completed;

        var delivery = new Delivery
        {
            PurchaseOrderId = po.Id,
            DeliveryReference = "DEL-PO-COMPLETED",
            Status = DeliveryStatus.Scheduled
        };
        db.Deliveries.Add(delivery);
        db.DeliveryItems.Add(new DeliveryItem
        {
            Delivery = delivery,
            PurchaseOrderItemId = poItem.Id
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id, "SiteEngineer");
        var result = await controller.ReceiveDelivery(delivery.Id, new ReceiveDeliveryDto
        {
            Items = new List<ReceiveDeliveryItemDto>
            {
                new() { PurchaseOrderItemId = poItem.Id, ReceivedQuantity = 10, DamagedQuantity = 0 }
            }
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Only confirmed or in-progress purchase orders can be received.", badRequest.Value);
    }

    [Fact]
    public async Task ReceiveDelivery_Rejects_When_ReceivedQuantity_Exceeds_Outstanding()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, poItem, user) = await SeedDeliveryBaseAsync(db);

        var delivery = new Delivery
        {
            PurchaseOrderId = po.Id,
            DeliveryReference = "DEL-OVER",
            Status = DeliveryStatus.Scheduled
        };
        db.Deliveries.Add(delivery);
        db.DeliveryItems.Add(new DeliveryItem
        {
            Delivery = delivery,
            PurchaseOrderItemId = poItem.Id
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id, "SiteEngineer");
        // Ordered quantity is 100, attempting to receive 101
        var result = await controller.ReceiveDelivery(delivery.Id, new ReceiveDeliveryDto
        {
            Items = new List<ReceiveDeliveryItemDto>
            {
                new() { PurchaseOrderItemId = poItem.Id, ReceivedQuantity = 101, DamagedQuantity = 0 }
            }
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("exceeds the outstanding quantity", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task ReceiveDelivery_Rejects_When_DamagedQuantity_Exceeds_ReceivedQuantity()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, poItem, user) = await SeedDeliveryBaseAsync(db);

        var delivery = new Delivery
        {
            PurchaseOrderId = po.Id,
            DeliveryReference = "DEL-DAMAGE-EXCESS",
            Status = DeliveryStatus.Scheduled
        };
        db.Deliveries.Add(delivery);
        db.DeliveryItems.Add(new DeliveryItem
        {
            Delivery = delivery,
            PurchaseOrderItemId = poItem.Id
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id, "SiteEngineer");
        // Received 50, damaged 60
        var result = await controller.ReceiveDelivery(delivery.Id, new ReceiveDeliveryDto
        {
            Items = new List<ReceiveDeliveryItemDto>
            {
                new() { PurchaseOrderItemId = poItem.Id, ReceivedQuantity = 50, DamagedQuantity = 60 }
            }
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Quantities cannot be negative and damaged quantity cannot exceed received quantity.", badRequest.Value);
    }

    [Fact]
    public async Task ReceiveDelivery_Rejects_When_Quantities_Negative()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, poItem, user) = await SeedDeliveryBaseAsync(db);

        var delivery = new Delivery
        {
            PurchaseOrderId = po.Id,
            DeliveryReference = "DEL-NEG",
            Status = DeliveryStatus.Scheduled
        };
        db.Deliveries.Add(delivery);
        db.DeliveryItems.Add(new DeliveryItem
        {
            Delivery = delivery,
            PurchaseOrderItemId = poItem.Id
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id, "SiteEngineer");
        var result = await controller.ReceiveDelivery(delivery.Id, new ReceiveDeliveryDto
        {
            Items = new List<ReceiveDeliveryItemDto>
            {
                new() { PurchaseOrderItemId = poItem.Id, ReceivedQuantity = -10, DamagedQuantity = 0 }
            }
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Quantities cannot be negative and damaged quantity cannot exceed received quantity.", badRequest.Value);
    }

    [Fact]
    public async Task ReceiveDelivery_Rejects_Duplicate_Item_Ids()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, poItem, user) = await SeedDeliveryBaseAsync(db);

        var delivery = new Delivery
        {
            PurchaseOrderId = po.Id,
            DeliveryReference = "DEL-DUP",
            Status = DeliveryStatus.Scheduled
        };
        db.Deliveries.Add(delivery);
        db.DeliveryItems.Add(new DeliveryItem
        {
            Delivery = delivery,
            PurchaseOrderItemId = poItem.Id
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id, "SiteEngineer");
        var result = await controller.ReceiveDelivery(delivery.Id, new ReceiveDeliveryDto
        {
            Items = new List<ReceiveDeliveryItemDto>
            {
                new() { PurchaseOrderItemId = poItem.Id, ReceivedQuantity = 20, DamagedQuantity = 0 },
                new() { PurchaseOrderItemId = poItem.Id, ReceivedQuantity = 30, DamagedQuantity = 0 }
            }
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Duplicate purchase-order item IDs are not allowed.", badRequest.Value);
    }

    [Fact]
    public async Task ReceiveDelivery_Rejects_Foreign_Item()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, poItem, user) = await SeedDeliveryBaseAsync(db);

        var delivery = new Delivery
        {
            PurchaseOrderId = po.Id,
            DeliveryReference = "DEL-FOREIGN",
            Status = DeliveryStatus.Scheduled
        };
        db.Deliveries.Add(delivery);
        db.DeliveryItems.Add(new DeliveryItem
        {
            Delivery = delivery,
            PurchaseOrderItemId = poItem.Id
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id, "SiteEngineer");
        var result = await controller.ReceiveDelivery(delivery.Id, new ReceiveDeliveryDto
        {
            Items = new List<ReceiveDeliveryItemDto>
            {
                new() { PurchaseOrderItemId = 88888, ReceivedQuantity = 10, DamagedQuantity = 0 }
            }
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Every item must belong to this delivery and its purchase order.", badRequest.Value);
    }

    [Fact]
    public async Task ReceiveDelivery_Reconciles_Partial_And_Damaged_Fulfilment_Correctly()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, poItem, user) = await SeedDeliveryBaseAsync(db);

        // Delivery 1: ordered 100, received 60, damaged 10 -> Fulfilled = 50, Remaining = 50
        var d1 = new Delivery
        {
            PurchaseOrderId = po.Id,
            DeliveryReference = "DEL-PART-1",
            Status = DeliveryStatus.Scheduled
        };
        db.Deliveries.Add(d1);
        db.DeliveryItems.Add(new DeliveryItem { Delivery = d1, PurchaseOrderItemId = poItem.Id });
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id, "SiteEngineer");
        var r1 = await controller.ReceiveDelivery(d1.Id, new ReceiveDeliveryDto
        {
            ReceivedByUserId = 999, // Should be overridden by actorId
            Items = new List<ReceiveDeliveryItemDto>
            {
                new() { PurchaseOrderItemId = poItem.Id, ReceivedQuantity = 60, DamagedQuantity = 10 }
            }
        });

        Assert.IsType<OkObjectResult>(r1);
        var savedD1 = await db.Deliveries.FindAsync(d1.Id);
        Assert.Equal(DeliveryStatus.DiscrepancyReported, savedD1!.Status);
        Assert.Equal(user.Id, savedD1.ReceivedByUserId); // Authoritative actor enforced
        Assert.NotNull(savedD1.ActualArrivalDate); // Server generated
        Assert.NotNull(savedD1.ReceivedAt); // Server generated

        var updatedPo = await db.PurchaseOrders.FindAsync(po.Id);
        Assert.Equal(PurchaseOrderStatus.InProgress, updatedPo!.Status); // Not yet complete (50 remaining)

        // Delivery 2: attempt to receive 60 -> exceeds remaining 50 -> must be rejected
        var d2 = new Delivery
        {
            PurchaseOrderId = po.Id,
            DeliveryReference = "DEL-PART-2",
            Status = DeliveryStatus.Scheduled
        };
        db.Deliveries.Add(d2);
        db.DeliveryItems.Add(new DeliveryItem { Delivery = d2, PurchaseOrderItemId = poItem.Id });
        await db.SaveChangesAsync();

        var r2Over = await controller.ReceiveDelivery(d2.Id, new ReceiveDeliveryDto
        {
            Items = new List<ReceiveDeliveryItemDto>
            {
                new() { PurchaseOrderItemId = poItem.Id, ReceivedQuantity = 60, DamagedQuantity = 0 }
            }
        });
        var badOver = Assert.IsType<BadRequestObjectResult>(r2Over);
        Assert.Contains("exceeds the outstanding quantity (50)", badOver.Value?.ToString());

        // Delivery 2: receive exactly remaining 50 with 0 damaged -> PO completes!
        var r2Exact = await controller.ReceiveDelivery(d2.Id, new ReceiveDeliveryDto
        {
            Items = new List<ReceiveDeliveryItemDto>
            {
                new() { PurchaseOrderItemId = poItem.Id, ReceivedQuantity = 50, DamagedQuantity = 0 }
            }
        });
        Assert.IsType<OkObjectResult>(r2Exact);

        var savedD2 = await db.Deliveries.FindAsync(d2.Id);
        Assert.Equal(DeliveryStatus.Received, savedD2!.Status);

        var completedPo = await db.PurchaseOrders.FindAsync(po.Id);
        Assert.Equal(PurchaseOrderStatus.Completed, completedPo!.Status);
    }

    // =========================================================================
    // 4. DELIVERY ISSUE REPORTING VALIDATIONS
    // =========================================================================

    [Fact]
    public async Task ReportIssue_Rejects_When_Description_Empty()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, _, user) = await SeedDeliveryBaseAsync(db);

        var delivery = new Delivery { PurchaseOrderId = po.Id, DeliveryReference = "DEL-ISS-1" };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id, "SiteEngineer");
        var result = await controller.ReportIssue(new ReportIssueDto
        {
            DeliveryId = delivery.Id,
            Description = "   ",
            IssueType = DeliveryIssueType.Damage,
            Severity = DeliveryIssueSeverity.High
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Issue description is required.", badRequest.Value);
    }

    [Fact]
    public async Task ReportIssue_Rejects_When_Delivery_Not_Found()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, _, _, user) = await SeedDeliveryBaseAsync(db);

        var controller = CreateController(db, user.Id, "SiteEngineer");
        var result = await controller.ReportIssue(new ReportIssueDto
        {
            DeliveryId = 99999,
            Description = "Truck arrived with cracked cement bags",
            IssueType = DeliveryIssueType.Damage,
            Severity = DeliveryIssueSeverity.Medium
        });

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Delivery #99999 not found.", notFound.Value);
    }

    [Fact]
    public async Task ReportIssue_Rejects_When_DeliveryItemId_Does_Not_Belong()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, _, user) = await SeedDeliveryBaseAsync(db);

        var delivery = new Delivery { PurchaseOrderId = po.Id, DeliveryReference = "DEL-ISS-2" };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id, "SiteEngineer");
        var result = await controller.ReportIssue(new ReportIssueDto
        {
            DeliveryId = delivery.Id,
            DeliveryItemId = 77777, // Not part of delivery
            Description = "Wrong grade label",
            IssueType = DeliveryIssueType.WrongMaterial,
            Severity = DeliveryIssueSeverity.Low
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Delivery item #77777 does not belong to delivery #" + delivery.Id + ".", badRequest.Value);
    }

    [Fact]
    public async Task ReportIssue_Success_Enforces_Authenticated_User()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (_, _, po, poItem, user) = await SeedDeliveryBaseAsync(db);

        var delivery = new Delivery { PurchaseOrderId = po.Id, DeliveryReference = "DEL-ISS-3" };
        db.Deliveries.Add(delivery);
        var dItem = new DeliveryItem { Delivery = delivery, PurchaseOrderItemId = poItem.Id };
        db.DeliveryItems.Add(dItem);
        await db.SaveChangesAsync();

        var controller = CreateController(db, user.Id, "SiteEngineer");
        var result = await controller.ReportIssue(new ReportIssueDto
        {
            DeliveryId = delivery.Id,
            DeliveryItemId = dItem.Id,
            Description = "Moisture damage detected on pallet 3",
            IssueType = DeliveryIssueType.Damage,
            Severity = DeliveryIssueSeverity.High,
            ReportedByUserId = 9999 // Should be ignored in favor of actorId
        });

        var okResult = Assert.IsType<OkObjectResult>(result);
        var issue = Assert.IsType<DeliveryIssue>(okResult.Value);
        Assert.Equal(user.Id, issue.ReportedByUserId); // Authenticated actor is authoritative
        Assert.Equal("Open", issue.Status);
        Assert.Equal(dItem.Id, issue.DeliveryItemId);
    }
}
