using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(ApplicationDbContext context)
    {
        context.Database.EnsureCreated();

        // 1. Seed Users
        if (!await context.Users.AnyAsync())
        {
            var users = new List<User>
            {
                new() { FullName = "Jordan Doe", Email = "jordan@buildwise.lk", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { FullName = "Ramya Fernando", Email = "ramya@buildwise.lk", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            };
            await context.Users.AddRangeAsync(users);
            await context.SaveChangesAsync();
        }

        // 2. Seed Projects
        if (!await context.Projects.AnyAsync())
        {
            var projects = new List<Project>
            {
                new() { Name = "Apartment Development - Colombo", Location = "Colombo 03", Description = "High-rise luxury apartments", Status = ProjectStatus.Active, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(300)), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { Name = "Kandy Highway Project", Location = "Kadawatha to Kandy", Description = "Highway extension work", Status = ProjectStatus.Active, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90)), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(180)), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            };
            await context.Projects.AddRangeAsync(projects);
            await context.SaveChangesAsync();
        }

        // 3. Seed Materials
        if (!await context.Materials.AnyAsync())
        {
            var materials = new List<Material>
            {
                new() { Name = "OPC Cement", Description = "Ordinary Portland Cement Grade 42.5", Unit = "bags", Category = "Cement", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { Name = "Reinforcement Steel", Description = "Tor steel bars 12mm", Unit = "tonnes", Category = "Steel", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { Name = "River Sand", Description = "Fine river sand for plastering", Unit = "cubic metres", Category = "Sand", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            };
            await context.Materials.AddRangeAsync(materials);
            await context.SaveChangesAsync();
        }

        // 4. Seed Suppliers
        if (!await context.Suppliers.AnyAsync())
        {
            var suppliers = new List<Supplier>
            {
                new() { Name = "ABC Building Materials", ContactPerson = "Kasun Perera", Email = "kasun@abc.lk", Phone = "+94771234567", Address = "123 Galle Road, Colombo 03", Status = SupplierStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { Name = "Lanka Steel Corp", ContactPerson = "Nishantha Silva", Email = "info@lankasteel.lk", Phone = "+94112345678", Address = "45 Industrial Zone, Oruwela", Status = SupplierStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { Name = "Colombo Sand Suppliers", ContactPerson = "Ruwan Kumara", Email = "ruwan@colombosand.lk", Phone = "+94711234567", Address = "78 River Side Road, Hanwella", Status = SupplierStatus.Suspended, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            };
            await context.Suppliers.AddRangeAsync(suppliers);
            await context.SaveChangesAsync();
        }

        // 5. Seed Purchase Orders & Items
        if (!await context.PurchaseOrders.AnyAsync())
        {
            var project = await context.Projects.FirstAsync();
            var material = await context.Materials.FirstAsync(m => m.Name == "OPC Cement");
            var supplier = await context.Suppliers.FirstAsync(s => s.Name == "ABC Building Materials");

            // PO 1
            var po1 = new PurchaseOrder
            {
                SupplierId = supplier.Id,
                ProjectId = project.Id,
                OrderDate = DateTime.UtcNow.AddDays(-2),
                ExpectedDeliveryDate = DateTime.UtcNow.AddDays(3),
                Status = PurchaseOrderStatus.Created,
                TotalAmount = 1050000.00m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await context.PurchaseOrders.AddAsync(po1);
            await context.SaveChangesAsync();

            var poItem1 = new PurchaseOrderItem
            {
                PurchaseOrderId = po1.Id,
                MaterialId = material.Id,
                OrderedQuantity = 500.00m,
                UnitPrice = 2100.00m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await context.PurchaseOrderItems.AddAsync(poItem1);
            await context.SaveChangesAsync();

            // Create a scheduled delivery for PO 1
            var delivery1 = new Delivery
            {
                PurchaseOrderId = po1.Id,
                DeliveryReference = "DN-ABC-9921",
                Status = DeliveryStatus.Scheduled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await context.Deliveries.AddAsync(delivery1);
            await context.SaveChangesAsync();

            var deliveryItem1 = new DeliveryItem
            {
                DeliveryId = delivery1.Id,
                PurchaseOrderItemId = poItem1.Id,
                ReceivedQuantity = 0,
                DamagedQuantity = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await context.DeliveryItems.AddAsync(deliveryItem1);
            await context.SaveChangesAsync();


            // PO 2 (Steel)
            var steelMaterial = await context.Materials.FirstAsync(m => m.Name == "Reinforcement Steel");
            var steelSupplier = await context.Suppliers.FirstAsync(s => s.Name == "Lanka Steel Corp");

            var po2 = new PurchaseOrder
            {
                SupplierId = steelSupplier.Id,
                ProjectId = project.Id,
                OrderDate = DateTime.UtcNow.AddDays(-5),
                ExpectedDeliveryDate = DateTime.UtcNow.AddDays(-1), // Already expected
                Status = PurchaseOrderStatus.InProgress,
                TotalAmount = 3200000.00m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await context.PurchaseOrders.AddAsync(po2);
            await context.SaveChangesAsync();

            var poItem2 = new PurchaseOrderItem
            {
                PurchaseOrderId = po2.Id,
                MaterialId = steelMaterial.Id,
                OrderedQuantity = 10.00m,
                UnitPrice = 320000.00m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await context.PurchaseOrderItems.AddAsync(poItem2);
            await context.SaveChangesAsync();

            // Create a delivery that was received with discrepancy
            var user = await context.Users.FirstAsync();
            var delivery2 = new Delivery
            {
                PurchaseOrderId = po2.Id,
                DeliveryReference = "DN-LSC-4012",
                ActualArrivalDate = DateTime.UtcNow.AddDays(-1),
                ReceivedAt = DateTime.UtcNow.AddDays(-1),
                ReceivedByUserId = user.Id,
                Status = DeliveryStatus.DiscrepancyReported,
                Notes = "Shortage of 2 tonnes. 1 tonne damaged during transit.",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            };
            await context.Deliveries.AddAsync(delivery2);
            await context.SaveChangesAsync();

            var deliveryItem2 = new DeliveryItem
            {
                DeliveryId = delivery2.Id,
                PurchaseOrderItemId = poItem2.Id,
                ReceivedQuantity = 8.00m,
                DamagedQuantity = 1.00m,
                Notes = "Received 8 instead of 10. 1 tonne bent/damaged.",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            };
            await context.DeliveryItems.AddAsync(deliveryItem2);
            await context.SaveChangesAsync();

            // 6. Seed Delivery Schedules
            if (!await context.DeliverySchedules.AnyAsync())
            {
                var schedule = new DeliverySchedule
                {
                    PurchaseOrderId = po1.Id,
                    ScheduledDate = DateTime.UtcNow.AddDays(3),
                    ScheduledTimeSlot = "Morning (09:00 - 12:00)",
                    Status = "Confirmed",
                    Notes = "Site crane will be available.",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await context.DeliverySchedules.AddAsync(schedule);
                await context.SaveChangesAsync();
            }

            // 7. Seed Delivery Issues
            if (!await context.DeliveryIssues.AnyAsync())
            {
                var issue = new DeliveryIssue
                {
                    DeliveryId = delivery2.Id,
                    DeliveryItemId = deliveryItem2.Id,
                    IssueType = DeliveryIssueType.Shortage,
                    Description = "Received 8 tonnes of steel instead of 10 tonnes.",
                    Severity = DeliveryIssueSeverity.High,
                    Status = "Open",
                    ReportedByUserId = user.Id,
                    ReportedAt = DateTime.UtcNow.AddDays(-1),
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                };
                await context.DeliveryIssues.AddAsync(issue);
                await context.SaveChangesAsync();
            }

            // 8. Seed Delivery Evidence
            if (!await context.DeliveryEvidences.AnyAsync())
            {
                var evidence = new DeliveryEvidence
                {
                    DeliveryId = delivery2.Id,
                    DeliveryItemId = deliveryItem2.Id,
                    FileUrl = "http://localhost:5078/uploads/evidence_steel_damage.jpg",
                    FileType = "Image",
                    UploadedByUserId = user.Id,
                    UploadedAt = DateTime.UtcNow.AddDays(-1),
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                };
                await context.DeliveryEvidences.AddAsync(evidence);
                await context.SaveChangesAsync();
            }
        }
    }
}
