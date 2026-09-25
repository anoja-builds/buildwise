using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildWise.Api.Tests;

/// <summary>
/// Component 4 — Quality Inspection &amp; Non-Conformance Management.
///
/// Exercises <see cref="QualityInspectionService"/>:
///  - Rule 1: the inspection must reference an existing delivery.
///  - Rule 2: Accepted + Rejected may never exceed Inspected quantity.
///  - Rule 3: rejected materials automatically raise a Non-Conformance Report (NCR).
/// </summary>
public class QualityInspectionServiceTests
{
    private const int InspectorUserId = 12;
    private const string DeliveryReference = "INV-9081";

    private sealed record DeliveryScenario(
        ApplicationDbContext Db,
        Delivery Delivery,
        Material Material);

    /// <summary>
    /// Seeds the Component 3 hand-off state: a Confirmed purchase order with a
    /// received delivery (default 240 bags received / 5 damaged) for OPC Cement.
    /// </summary>
    private static async Task<DeliveryScenario> SeedConfirmedDeliveryAsync(
        decimal receivedQuantity = 240m,
        decimal damagedQuantity = 5m)
    {
        var db = TestDbFactory.CreateInMemory();

        var project = new Project
        {
            Name = "Riverside Apartments - Block C",
            Location = "Colombo",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Projects.Add(project);

        var material = new Material
        {
            Name = "OPC Cement",
            Unit = "Bags",
            Category = "Structural Materials",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var purchaseOrder = new PurchaseOrder
        {
            ProjectId = project.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpectedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            Status = PurchaseOrderStatus.Confirmed,
            TotalAmount = 525000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.PurchaseOrders.Add(purchaseOrder);
        await db.SaveChangesAsync();

        var delivery = new Delivery
        {
            PurchaseOrderId = purchaseOrder.Id,
            ReceivedByUserId = 7,
            DeliveryReference = DeliveryReference,
            Status = DeliveryStatus.DiscrepancyReported,
            DeliveredAt = DateTime.UtcNow
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();

        db.DeliveryItems.Add(new DeliveryItem
        {
            DeliveryId = delivery.Id,
            MaterialId = material.Id,
            ReceivedQuantity = receivedQuantity,
            DamagedQuantity = damagedQuantity,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return new DeliveryScenario(db, delivery, material);
    }

    private static Inspection BuildInspection(
        int deliveryId,
        int materialId,
        decimal inspected,
        decimal accepted,
        decimal rejected,
        string reason = "")
    {
        return new Inspection
        {
            DeliveryId = deliveryId,
            InspectorUserId = InspectorUserId,
            Items = new List<InspectionItem>
            {
                new()
                {
                    MaterialId = materialId,
                    InspectedQuantity = inspected,
                    AcceptedQuantity = accepted,
                    RejectedQuantity = rejected,
                    RejectionReason = reason
                }
            }
        };
    }

    // ---------------------------------------------------------------- Rule 1

    [Fact]
    public async Task CompleteInspection_Throws_When_Delivery_Does_Not_Exist()
    {
        var db = TestDbFactory.CreateInMemory();
        var service = new QualityInspectionService(db);

        var inspection = BuildInspection(deliveryId: 9999, materialId: 1, inspected: 235m, accepted: 235m, rejected: 0m);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.CompleteInspectionAsync(inspection));

        Assert.Contains("Delivery record not found", ex.Message);
    }

    // ---------------------------------------------------------------- Rule 2

    [Fact]
    public async Task CompleteInspection_Throws_When_Accepted_Plus_Rejected_Exceeds_Inspected()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        // 235 accepted + 10 rejected = 245 > 235 inspected
        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 235m, accepted: 235m, rejected: 10m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteInspectionAsync(inspection));

        Assert.Contains("must equal Inspected quantity", ex.Message);
    }

    [Fact]
    public async Task CompleteInspection_Throws_And_Persists_Nothing_When_Arithmetic_Invalid()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 100m, accepted: 90m, rejected: 20m);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteInspectionAsync(inspection));

        Assert.Empty(await scenario.Db.Inspections.ToListAsync());
        Assert.Empty(await scenario.Db.NonConformances.ToListAsync());
    }
    [Fact]
    public async Task CompleteInspection_Throws_When_Accepted_Plus_Rejected_Is_Less_Than_Inspected()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);
        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 230m, rejected: 5m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteInspectionAsync(inspection));

        Assert.Contains("must equal Inspected quantity", ex.Message);
        Assert.Empty(await scenario.Db.Inspections.ToListAsync());
    }

    [Fact]
    public async Task CompleteInspection_Throws_For_Negative_Quantities()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);
        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 10m, accepted: -1m, rejected: 11m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteInspectionAsync(inspection));
        Assert.Contains("cannot be negative", ex.Message);
    }


    [Fact]
    public async Task CompleteInspection_Allows_Accepted_Plus_Rejected_Equal_To_Inspected()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 235m, rejected: 5m,
            reason: "Damaged bags");

        var created = await service.CompleteInspectionAsync(inspection);

        Assert.Equal(InspectionStatus.Completed, created.Status);
    }

    // ------------------------------------------- Happy path / decision logic

    [Fact]
    public async Task CompleteInspection_Returns_Accepted_And_Creates_No_Ncr_When_Nothing_Rejected()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 240m, rejected: 0m);

        var created = await service.CompleteInspectionAsync(inspection);

        Assert.Equal(InspectionDecision.Accepted, created.OverallDecision);
        Assert.Equal(InspectionStatus.Completed, created.Status);
        Assert.Empty(await scenario.Db.NonConformances.ToListAsync());
    }

    [Fact]
    public async Task CompleteInspection_Returns_PartiallyAccepted_And_Creates_Ncr_When_Rejections_Exist()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 235m, rejected: 5m,
            reason: "5 bags damaged and water-soaked upon arrival.");

        var created = await service.CompleteInspectionAsync(inspection);

        Assert.Equal(InspectionDecision.PartiallyAccepted, created.OverallDecision);
        Assert.Equal(InspectionStatus.Completed, created.Status);

        var ncr = Assert.Single(await scenario.Db.NonConformances.ToListAsync());
        Assert.Equal(NonConformanceStatus.CorrectiveActionRequired, ncr.Status);
        Assert.Equal("5 bags damaged and water-soaked upon arrival.", ncr.IssueDescription);
    }

    [Fact]
    public async Task CompleteInspection_Stamps_Inspector_Delivery_And_Timestamp()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var before = DateTime.UtcNow.AddSeconds(-1);
        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 240m, rejected: 0m);

        var created = await service.CompleteInspectionAsync(inspection);

        Assert.Equal(InspectionStatus.Completed, created.Status);
        Assert.Equal(InspectorUserId, created.InspectorUserId);
        Assert.Equal(scenario.Delivery.Id, created.DeliveryId);
        Assert.True(created.InspectedAt >= before);
    }

    [Fact]
    public async Task CompleteInspection_Persists_Inspection_Items_With_Quantities()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 235m, rejected: 5m,
            reason: "Damaged");

        var created = await service.CompleteInspectionAsync(inspection);

        var item = Assert.Single(
            await scenario.Db.InspectionItems.Where(i => i.InspectionId == created.Id).ToListAsync());

        Assert.Equal(240m, item.InspectedQuantity);
        Assert.Equal(235m, item.AcceptedQuantity);
        Assert.Equal(5m, item.RejectedQuantity);
        Assert.Equal(scenario.Material.Id, item.MaterialId);
    }

    // ---------------------------------------------------------------- Rule 3

    [Fact]
    public async Task Ncr_Is_Severity_Medium_When_Rejected_Quantity_Is_50_Or_Less()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 190m, rejected: 50m,
            reason: "Cracked bags");

        await service.CompleteInspectionAsync(inspection);

        var ncr = Assert.Single(await scenario.Db.NonConformances.ToListAsync());
        Assert.Equal(NonConformanceSeverity.Medium, ncr.Severity);
    }

    [Fact]
    public async Task Ncr_Is_Severity_High_When_Rejected_Quantity_Exceeds_50()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 179m, rejected: 61m,
            reason: "Water damaged batch");

        await service.CompleteInspectionAsync(inspection);

        var ncr = Assert.Single(await scenario.Db.NonConformances.ToListAsync());
        Assert.Equal(NonConformanceSeverity.High, ncr.Severity);
    }

    [Fact]
    public async Task Ncr_Rejects_Blank_Rejection_Reason()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 232m, rejected: 8m,
            reason: "   ");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteInspectionAsync(inspection));
        Assert.Contains("rejection reason", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await scenario.Db.NonConformances.ToListAsync());
    }

    [Fact]
    public async Task Ncr_Number_Follows_Ncr_Six_Digit_Format()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 235m, rejected: 5m,
            reason: "Damaged");

        await service.CompleteInspectionAsync(inspection);

        var ncr = Assert.Single(await scenario.Db.NonConformances.ToListAsync());
        Assert.StartsWith("NCR-", ncr.NcrNumber);
        Assert.Equal(10, ncr.NcrNumber.Length);
        Assert.True(int.TryParse(ncr.NcrNumber[4..], out _),
            $"NCR number '{ncr.NcrNumber}' should end with six digits.");
    }

    [Fact]
    public async Task Ncr_Is_Linked_To_The_Rejected_Inspection_Item()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 235m, rejected: 5m,
            reason: "Damaged");

        var created = await service.CompleteInspectionAsync(inspection);

        var ncr = Assert.Single(await scenario.Db.NonConformances.ToListAsync());
        Assert.Equal(created.Items[0].Id, ncr.InspectionItemId);
    }

    [Fact]
    public async Task Ncr_Uses_Agent_Corrective_Action_Recommendation()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var inspection = BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 235m, rejected: 5m,
            reason: "Damaged");

        await service.CompleteInspectionAsync(inspection);

        var ncr = Assert.Single(await scenario.Db.NonConformances.ToListAsync());
        Assert.Equal("Issue NCR and require supplier corrective action.", ncr.CorrectiveActionPlan);
    }

    [Fact]
    public async Task Multiple_Rejected_Items_Generate_One_Ncr_Per_Item()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        var rebar = new Material
        {
            Name = "Steel Rebar 12mm",
            Unit = "Tonnes",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        scenario.Db.Materials.Add(rebar);
        await scenario.Db.SaveChangesAsync();

        var inspection = new Inspection
        {
            DeliveryId = scenario.Delivery.Id,
            InspectorUserId = InspectorUserId,
            Items = new List<InspectionItem>
            {
                new()
                {
                    MaterialId = scenario.Material.Id,
                    InspectedQuantity = 240m,
                    AcceptedQuantity = 235m,
                    RejectedQuantity = 5m,
                    RejectionReason = "Damaged bags"
                },
                new()
                {
                    MaterialId = rebar.Id,
                    InspectedQuantity = 10m,
                    AcceptedQuantity = 8m,
                    RejectedQuantity = 2m,
                    RejectionReason = "Out of tolerance"
                },
                new()
                {
                    MaterialId = rebar.Id,
                    InspectedQuantity = 5m,
                    AcceptedQuantity = 5m,
                    RejectedQuantity = 0m
                }
            }
        };

        await service.CompleteInspectionAsync(inspection);

        var ncrs = await scenario.Db.NonConformances.ToListAsync();
        Assert.Equal(2, ncrs.Count);
        Assert.All(ncrs, n => Assert.Equal(NonConformanceStatus.CorrectiveActionRequired, n.Status));
    }

    // ------------------------------------------------- Non-conformance reads

    [Fact]
    public async Task GetOpenNonConformances_Excludes_Closed_Reports()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        await service.CompleteInspectionAsync(BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 235m, rejected: 5m,
            reason: "Damaged"));

        var open = Assert.Single(await service.GetOpenNonConformancesAsync());
        await service.UpdateNonConformanceStatusAsync(open.Id, NonConformanceStatus.Closed);

        Assert.Empty(await service.GetOpenNonConformancesAsync());
    }

    [Fact]
    public async Task GetOpenNonConformances_Includes_Related_Inspection_Item_And_Material()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        await service.CompleteInspectionAsync(BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 235m, rejected: 5m,
            reason: "Damaged"));

        var ncr = Assert.Single(await service.GetOpenNonConformancesAsync());
        Assert.NotNull(ncr.InspectionItem);
        Assert.NotNull(ncr.InspectionItem!.Material);
        Assert.Equal("OPC Cement", ncr.InspectionItem.Material!.Name);
    }

    [Fact]
    public async Task GetNonConformanceById_Returns_Null_For_Unknown_Id()
    {
        var db = TestDbFactory.CreateInMemory();
        var service = new QualityInspectionService(db);

        Assert.Null(await service.GetNonConformanceByIdAsync(4242));
    }

    // ------------------------------------------- Non-conformance status flow

    [Fact]
    public async Task DtoCompletion_PersistsCriteriaAndEvidenceMetadata()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);
        var created = await service.CompleteInspectionAsync(new CompleteInspectionDto
        {
            DeliveryId = scenario.Delivery.Id,
            InspectionCriteria = "Visual and moisture check",
            ObservedResult = "Partially usable",
            Notes = "Segregate damaged bags",
            Items = new List<InspectionItemInputDto>
            {
                new(scenario.Material.Id, 240m, 235m, 5m, "Damaged")
            },
            Evidence = new List<InspectionEvidenceDto>
            {
                new("damage.jpg", "https://example.test/evidence/damage.jpg", "image/jpeg", 1024)
            }
        }, InspectorUserId);

        Assert.Equal("Visual and moisture check", created.InspectionCriteria);
        Assert.Equal("Partially usable", created.ObservedResult);
        Assert.Single(await scenario.Db.InspectionEvidences.ToListAsync());
    }

    [Fact]
    public async Task TransitionNonConformance_RequiresResolutionForResolvedState()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);
        await service.CompleteInspectionAsync(BuildInspection(scenario.Delivery.Id, scenario.Material.Id, 240m, 235m, 5m, "Damaged"));
        var ncr = Assert.Single(await scenario.Db.NonConformances.ToListAsync());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TransitionNonConformanceAsync(
            ncr.Id, new NcrReviewRequest(NonConformanceStatus.Resolved, "Replacement complete", null, null), 1));
        Assert.Contains("resolution", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TransitionNonConformance_AllowsReviewAndResolution()
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);
        await service.CompleteInspectionAsync(BuildInspection(scenario.Delivery.Id, scenario.Material.Id, 240m, 235m, 5m, "Damaged"));
        var ncr = Assert.Single(await scenario.Db.NonConformances.ToListAsync());

        var underReview = await service.TransitionNonConformanceAsync(ncr.Id, new NcrReviewRequest(NonConformanceStatus.UnderReview, "Supplier contacted", null, null), 1);
        Assert.Equal(NonConformanceStatus.UnderReview, underReview.Status);
        var resolved = await service.TransitionNonConformanceAsync(ncr.Id, new NcrReviewRequest(NonConformanceStatus.CorrectiveActionRequired, "Replacement requested", null, "Replacement arranged"), 1);
        Assert.Equal(NonConformanceStatus.CorrectiveActionRequired, resolved.Status);
    }

    [Fact]
    public async Task UpdateNonConformanceStatus_Throws_For_Unknown_Id()
    {
        var db = TestDbFactory.CreateInMemory();
        var service = new QualityInspectionService(db);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.UpdateNonConformanceStatusAsync(4242, NonConformanceStatus.Resolved));

        Assert.Contains("not found", ex.Message);
    }

    [Theory]
    [InlineData(NonConformanceStatus.Resolved)]
    [InlineData(NonConformanceStatus.Closed)]
    public async Task UpdateNonConformanceStatus_Persists_New_Status(NonConformanceStatus newStatus)
    {
        var scenario = await SeedConfirmedDeliveryAsync();
        var service = new QualityInspectionService(scenario.Db);

        await service.CompleteInspectionAsync(BuildInspection(
            scenario.Delivery.Id, scenario.Material.Id, inspected: 240m, accepted: 235m, rejected: 5m,
            reason: "Damaged"));

        var ncr = Assert.Single(await scenario.Db.NonConformances.ToListAsync());
        var updated = await service.UpdateNonConformanceStatusAsync(ncr.Id, newStatus);

        Assert.Equal(newStatus, updated.Status);

        var reloaded = await scenario.Db.NonConformances.FindAsync(ncr.Id);
        Assert.Equal(newStatus, reloaded!.Status);
    }
}
