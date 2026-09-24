using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Services;

// Database-independent completion rules. The service holds PostgreSQL row locks
// and commits the validated changes atomically; callers cannot bypass validation.
public static class InspectionCompletion
{
    public static IReadOnlyList<InspectionItem> Apply(Inspection inspection,
        CompleteInspectionDto dto, IReadOnlyDictionary<int, DeliveryItem> deliveryItems)
    {
        if (inspection.Status == InspectionStatus.Completed)
            throw Conflict("Inspection has already been completed.");
        if (inspection.Status != InspectionStatus.UnderInspection)
            throw Conflict("Only an inspection that is UnderInspection can be completed.");
        if (!dto.OverallDecision.HasValue || !Enum.IsDefined(dto.OverallDecision.Value))
            throw Invalid("A valid overall decision must be supplied by the inspector.");
        if (dto.Items == null || dto.Items.Count == 0 || dto.Items.Any(item => item == null))
            throw Invalid("At least one non-null inspection item is required.");
        if (dto.Items.Select(item => item.DeliveryItemId).Distinct().Count() != dto.Items.Count)
            throw Invalid("Duplicate DeliveryItemId values are not allowed.");

        var requiredIds = deliveryItems.Values.Where(di => di.ReceivedQuantity > 0)
            .Select(di => di.Id).ToHashSet();
        if (requiredIds.Count == 0)
            throw Invalid("Delivery has no items with positive received quantity to inspect.");

        foreach (var item in dto.Items)
        {
            if (!deliveryItems.TryGetValue(item.DeliveryItemId, out var deliveryItem))
                throw Invalid($"Delivery item {item.DeliveryItemId} does not exist or does not belong to this inspection's delivery.");
            if (deliveryItem.ReceivedQuantity <= 0)
                throw Invalid($"Delivery item {item.DeliveryItemId} is outside inspection scope because its received quantity is not positive.");
            if (item.Condition?.Length > 100)
                throw Invalid("Condition must not exceed 100 characters.");
            ValidateQuantity(item.AcceptedQuantity, "AcceptedQuantity");
            ValidateQuantity(item.RejectedQuantity, "RejectedQuantity");
            if (item.AcceptedQuantity + item.RejectedQuantity <= 0)
                throw Invalid($"Accepted plus rejected quantity for delivery item {item.DeliveryItemId} must be greater than zero.");
            if (item.AcceptedQuantity + item.RejectedQuantity > deliveryItem.ReceivedQuantity)
                throw Invalid($"Accepted plus rejected quantity for delivery item {item.DeliveryItemId} must not exceed received quantity ({deliveryItem.ReceivedQuantity}).");
        }

        var submittedIds = dto.Items.Select(item => item.DeliveryItemId).ToHashSet();
        var missingIds = requiredIds.Except(submittedIds).OrderBy(itemId => itemId).ToList();
        if (missingIds.Count > 0)
            throw Invalid($"Inspection must include every positive-received delivery item. Missing DeliveryItemId values: {string.Join(", ", missingIds)}.");

        // With nonnegative quantities, Any(> 0) is equivalent to a positive total.
        // Only the complete current submission determines the inspector's decision.
        var hasAccepted = dto.Items.Any(item => item.AcceptedQuantity > 0);
        var hasRejected = dto.Items.Any(item => item.RejectedQuantity > 0);
        switch (dto.OverallDecision.Value)
        {
            case InspectionDecision.Accepted when !hasAccepted || hasRejected:
                throw Invalid("Accepted requires positive total accepted quantity and zero total rejected quantity.");
            case InspectionDecision.Rejected when hasAccepted || !hasRejected:
                throw Invalid("Rejected requires zero total accepted quantity and positive total rejected quantity.");
            case InspectionDecision.PartiallyAccepted when !hasAccepted || !hasRejected:
                throw Invalid("PartiallyAccepted requires some accepted quantity and some rejected quantity.");
        }

        var obsolete = inspection.Items.Where(item => !submittedIds.Contains(item.DeliveryItemId)).ToList();
        foreach (var item in obsolete) inspection.Items.Remove(item);
        var now = DateTime.UtcNow;
        foreach (var itemDto in dto.Items)
        {
            var item = inspection.Items.SingleOrDefault(i => i.DeliveryItemId == itemDto.DeliveryItemId);
            if (item == null)
            {
                item = new InspectionItem
                {
                    InspectionId = inspection.Id,
                    DeliveryItemId = itemDto.DeliveryItemId,
                    CreatedAt = now
                };
                inspection.Items.Add(item);
            }
            item.Condition = itemDto.Condition;
            item.AcceptedQuantity = itemDto.AcceptedQuantity;
            item.RejectedQuantity = itemDto.RejectedQuantity;
            item.Remarks = itemDto.Remarks;
            item.UpdatedAt = now;
        }
        inspection.OverallDecision = dto.OverallDecision.Value;
        inspection.Notes = dto.Notes;
        inspection.Status = InspectionStatus.Completed;
        inspection.UpdatedAt = now;
        return obsolete;
    }

    private static void ValidateQuantity(decimal quantity, string name)
    {
        if (quantity < 0 || quantity > 9999999999.99m)
            throw Invalid($"{name} must be between 0 and 9999999999.99.");
        if (decimal.Round(quantity, 2) != quantity)
            throw Invalid($"{name} must have at most two decimal places.");
    }

    private static QualityInspectionException Invalid(string message) => new(400, message);
    private static QualityInspectionException Conflict(string message) => new(409, message);
}
