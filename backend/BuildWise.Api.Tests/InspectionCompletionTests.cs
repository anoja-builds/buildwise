using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Xunit;

namespace BuildWise.Api.Tests;

public class InspectionCompletionTests
{
    private static Dictionary<int, DeliveryItem> DeliveryItems() => new()
    {
        [10] = new DeliveryItem { Id = 10, DeliveryId = 3, ReceivedQuantity = 240, DamagedQuantity = 5 }
    };
    private static Inspection Inspection() => new()
    {
        Id = 1, DeliveryId = 3, InspectorUserId = 7, Status = InspectionStatus.UnderInspection
    };
    private static CompleteInspectionDto Request(decimal accepted = 235, decimal rejected = 5,
        InspectionDecision decision = InspectionDecision.PartiallyAccepted) => new()
    {
        OverallDecision = decision, Notes = "Inspected on site",
        Items = [new() { DeliveryItemId = 10, AcceptedQuantity = accepted,
            RejectedQuantity = rejected, Condition = "Five damaged bags", Remarks = "Inspector observation" }]
    };

    [Theory]
    [InlineData(235, 5, InspectionDecision.PartiallyAccepted)]
    [InlineData(240, 0, InspectionDecision.Accepted)]
    [InlineData(0, 240, InspectionDecision.Rejected)]
    [InlineData(10, 0, InspectionDecision.Accepted)] // ERD permits totals below received.
    public void CompletionRecordsHumanDecisionAndPreservesIdentity(decimal accepted, decimal rejected, InspectionDecision decision)
    {
        var inspection = Inspection();
        var delivery = DeliveryItems();
        Assert.Empty(InspectionCompletion.Apply(inspection, Request(accepted, rejected, decision), delivery));
        Assert.Equal(InspectionStatus.Completed, inspection.Status);
        Assert.Equal(decision, inspection.OverallDecision);
        Assert.Equal(7, inspection.InspectorUserId);
        var item = Assert.Single(inspection.Items);
        Assert.Equal(accepted, item.AcceptedQuantity);
        Assert.Equal(rejected, item.RejectedQuantity);
        Assert.Equal("Five damaged bags", item.Condition);
        Assert.Equal("Inspector observation", item.Remarks);
        Assert.Equal("Inspected on site", inspection.Notes);
        Assert.Empty(item.NonConformances);
        Assert.Equal(240, delivery[10].ReceivedQuantity);
        Assert.Equal(5, delivery[10].DamagedQuantity);
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(235, -1)]
    [InlineData(236, 5)]
    [InlineData(0, 0)]
    [InlineData(1.001, 5)]
    [InlineData(1, 5.001)]
    [InlineData(10000000000, 5)]
    public void InvalidQuantitiesDoNotMutateInspection(decimal accepted, decimal rejected)
    {
        var inspection = Inspection();
        var error = Assert.Throws<QualityInspectionException>(() =>
            InspectionCompletion.Apply(inspection, Request(accepted, rejected), DeliveryItems()));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(InspectionStatus.UnderInspection, inspection.Status);
        Assert.Null(inspection.OverallDecision);
        Assert.Empty(inspection.Items);
    }

    [Fact]
    public void DuplicateCompletionIsRejectedWithoutChangingRecordedResults()
    {
        var inspection = Inspection();
        InspectionCompletion.Apply(inspection, Request(), DeliveryItems());
        var error = Assert.Throws<QualityInspectionException>(() =>
            InspectionCompletion.Apply(inspection, Request(240, 0, InspectionDecision.Accepted), DeliveryItems()));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal(235, Assert.Single(inspection.Items).AcceptedQuantity);
        Assert.Equal(InspectionDecision.PartiallyAccepted, inspection.OverallDecision);
    }

    [Theory]
    [InlineData(InspectionStatus.Pending)]
    [InlineData((InspectionStatus)99)]
    public void OnlyUnderInspectionCanComplete(InspectionStatus status)
    {
        var inspection = Inspection(); inspection.Status = status;
        Assert.Equal(409, Assert.Throws<QualityInspectionException>(() =>
            InspectionCompletion.Apply(inspection, Request(), DeliveryItems())).StatusCode);
    }

    [Theory]
    [InlineData(235, 5, InspectionDecision.Accepted)]
    [InlineData(235, 5, InspectionDecision.Rejected)]
    [InlineData(240, 0, InspectionDecision.PartiallyAccepted)]
    [InlineData(0, 240, InspectionDecision.PartiallyAccepted)]
    public void DecisionMustMatchQuantities(decimal accepted, decimal rejected, InspectionDecision decision) =>
        Assert.Equal(400, Assert.Throws<QualityInspectionException>(() =>
            InspectionCompletion.Apply(Inspection(), Request(accepted, rejected, decision), DeliveryItems())).StatusCode);

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("zero-received")]
    [InlineData("condition")]
    [InlineData("decision")]
    public void InvalidScopeAndFieldsAreRejected(string invalid)
    {
        var request = Request(); var delivery = DeliveryItems();
        switch (invalid)
        {
            case "missing": delivery[11] = new DeliveryItem { Id = 11, ReceivedQuantity = 1 }; break;
            case "duplicate": request.Items.Add(request.Items[0]); break;
            case "foreign": request.Items[0].DeliveryItemId = 99; break;
            case "zero-received": delivery[10].ReceivedQuantity = 0; break;
            case "condition": request.Items[0].Condition = new string('x', 101); break;
            case "decision": request.OverallDecision = null; break;
        }
        Assert.Equal(400, Assert.Throws<QualityInspectionException>(() =>
            InspectionCompletion.Apply(Inspection(), request, delivery)).StatusCode);
    }
}
