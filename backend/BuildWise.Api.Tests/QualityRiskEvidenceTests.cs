using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Xunit;

namespace BuildWise.Api.Tests;

public class QualityRiskEvidenceTests
{
    [Fact]
    public async Task Quotation_provenance_overrides_legacy_fields_for_material_supplier_and_history()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var scenario = await TestDbFactory.SeedStandardScenarioDataAsync(db);
        var correct = new Supplier { Name = "Quotation supplier" };
        var legacy = new Supplier { Name = "Stale direct supplier" };
        var quotation = new Quotation { MaterialRequestId = scenario.Request.Id, Supplier = correct,
            Items = [new QuotationItem { MaterialRequestItemId = scenario.RequestItem.Id, Quantity = 250 }] };
        db.Quotations.Add(quotation);
        db.Suppliers.Add(legacy);
        await db.SaveChangesAsync();
        var order = new PurchaseOrder { Quotation = quotation, Supplier = legacy };
        var delivery = new Delivery { PurchaseOrder = order, Status = DeliveryStatus.DiscrepancyReported,
            Items = [new DeliveryItem { ReceivedQuantity = 240, DamagedQuantity = 5,
                PurchaseOrderItem = new PurchaseOrderItem { PurchaseOrder = order,
                    QuotationItem = quotation.Items.Single(), Material = new Material { Name = "Stale direct material", Unit = "Wrong" } } }] };
        var subject = new Inspection { Delivery = delivery, Status = InspectionStatus.Completed,
            OverallDecision = InspectionDecision.PartiallyAccepted, UpdatedAt = DateTime.UtcNow,
            Items = [new InspectionItem { DeliveryItem = delivery.Items.Single(), AcceptedQuantity = 235, RejectedQuantity = 5 }] };
        var prior = new Inspection { Delivery = delivery, Status = InspectionStatus.Completed,
            OverallDecision = InspectionDecision.Rejected, UpdatedAt = DateTime.UtcNow.AddDays(-1),
            Items = [new InspectionItem { DeliveryItem = delivery.Items.Single(), RejectedQuantity = 5 }] };
        db.Inspections.AddRange(subject, prior);
        db.NonConformances.Add(new NonConformance { InspectionItem = prior.Items.Single(), IssueDescription = "Earlier rejection", CreatedAt = DateTime.UtcNow.AddDays(-1) });
        db.DeliveryIssues.Add(new DeliveryIssue { Delivery = delivery, Description = "Damaged bags" });
        await db.SaveChangesAsync();
        var evidence = await new QualityRiskEvidenceService(db).CollectAsync(subject.Id, default);
        Assert.Equal(correct.Id, evidence.SupplierId);
        Assert.Equal(scenario.Material.Id, Assert.Single(evidence.Items).MaterialId);
        Assert.Equal(scenario.Material.Name, evidence.Items[0].MaterialName);
        Assert.Equal(prior.Id, Assert.Single(evidence.History).InspectionId);
        Assert.Equal(1, evidence.PriorNcrCount);
        Assert.Single(evidence.Discrepancies);
        Assert.Single(evidence.DeliveryIssues);
    }

    [Fact]
    public async Task Quotation_supplier_is_available_without_a_direct_supplier()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var scenario = await TestDbFactory.SeedStandardScenarioDataAsync(db);
        var inspection = new Inspection { Status = InspectionStatus.Completed, OverallDecision = InspectionDecision.Accepted,
            Delivery = new Delivery { PurchaseOrder = new PurchaseOrder {
                Quotation = new Quotation { MaterialRequestId = scenario.Request.Id, Supplier = new Supplier { Name = "Supplier A" } }
            } } };
        db.Inspections.Add(inspection); await db.SaveChangesAsync();
        var subject = await new QualityRiskEvidenceService(db).GetSubjectAsync(inspection.Id, default);
        Assert.Equal("Supplier A", subject.Delivery!.PurchaseOrder!.Quotation!.Supplier.Name);
    }
}
