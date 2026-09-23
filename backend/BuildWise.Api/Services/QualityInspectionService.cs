using BuildWise.Api.Data;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class QualityInspectionService
{
    private readonly ApplicationDbContext _dbContext;
    private static readonly DeliveryStatus[] EligibleStatuses =
    {
        DeliveryStatus.Received, DeliveryStatus.DiscrepancyReported
    };

    public QualityInspectionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<PendingInspectionDeliveryDto>> GetPendingDeliveriesAsync()
    {
        return await _dbContext.Deliveries.AsNoTracking()
            .Where(d => EligibleStatuses.Contains(d.Status) && d.Items.Any(di => di.ReceivedQuantity > 0)
                && !_dbContext.Inspections.Any(i => i.DeliveryId == d.Id
                    && i.Status == InspectionStatus.UnderInspection))
            .OrderBy(d => d.Id)
            .Select(d => new PendingInspectionDeliveryDto
            {
                DeliveryId = d.Id,
                DeliveryReference = d.DeliveryReference,
                Status = d.Status,
                Items = d.Items.Where(di => di.ReceivedQuantity > 0).OrderBy(di => di.Id).Select(di => new InspectionDeliveryItemDto
                {
                    DeliveryItemId = di.Id,
                    PurchaseOrderItemId = di.PurchaseOrderItemId,
                    ReceivedQuantity = di.ReceivedQuantity
                }).ToList()
            }).ToListAsync();
    }

    public async Task<QualityInspectionResponseDto> StartInspectionAsync(StartInspectionDto dto, int actingUserId)
    {
        if (actingUserId <= 0)
            throw new QualityInspectionException(401, "A valid authenticated user is required.");
        if (dto.DeliveryId <= 0)
            throw Invalid("DeliveryId must be positive.");

        // The controller supplies this ID from the validated JWT, never the body.
        var inspector = await _dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == actingUserId);
        if (inspector == null || !inspector.IsActive)
            throw new QualityInspectionException(403, "An active inspector user is required.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        // Serialize starts for this delivery across requests and application instances.
        var deliveries = await _dbContext.Deliveries.FromSqlInterpolated(
            $"SELECT * FROM deliveries WHERE \"Id\" = {dto.DeliveryId} FOR UPDATE").ToListAsync();
        var delivery = deliveries.SingleOrDefault()
            ?? throw Missing("Delivery not found.");

        if (!EligibleStatuses.Contains(delivery.Status))
            throw Conflict("Delivery must be Received or DiscrepancyReported before inspection.");
        if (!await _dbContext.DeliveryItems.AnyAsync(di => di.DeliveryId == delivery.Id && di.ReceivedQuantity > 0))
            throw Invalid("Delivery must contain at least one item with positive received quantity.");

        if (await _dbContext.Inspections.AnyAsync(i => i.DeliveryId == delivery.Id
            && i.Status == InspectionStatus.UnderInspection))
            throw Conflict("This delivery already has an active inspection.");

        var now = DateTime.UtcNow;
        var inspection = new Inspection
        {
            DeliveryId = delivery.Id,
            InspectorUserId = inspector.Id,
            InspectionDate = now,
            Status = InspectionStatus.UnderInspection,
            Notes = dto.Notes,
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.Inspections.Add(inspection);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return ToResponse(inspection);
    }

    public async Task<QualityInspectionResponseDto> GetInspectionByIdAsync(int id)
    {
        var inspection = await _dbContext.Inspections.AsNoTracking().Include(i => i.Items)
            .SingleOrDefaultAsync(i => i.Id == id)
            ?? throw Missing("Inspection not found.");
        return ToResponse(inspection);
    }

    public async Task<QualityInspectionResponseDto> CompleteInspectionAsync(int id, CompleteInspectionDto dto)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var inspections = await _dbContext.Inspections.FromSqlInterpolated(
            $"SELECT * FROM inspections WHERE \"Id\" = {id} FOR UPDATE").ToListAsync();
        var inspection = inspections.SingleOrDefault()
            ?? throw Missing("Inspection not found.");
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

        // Defensive parent-row lock held through commit; inspection starts only after receiving.
        // This lock alone does not coordinate every possible receiving operation.
        var deliveries = await _dbContext.Deliveries.FromSqlInterpolated(
            $"SELECT * FROM deliveries WHERE \"Id\" = {inspection.DeliveryId} FOR UPDATE").ToListAsync();
        if (deliveries.Count == 0)
            throw Missing("Delivery not found.");

        var deliveryItems = await _dbContext.DeliveryItems.AsNoTracking()
            .Where(di => di.DeliveryId == inspection.DeliveryId)
            .ToDictionaryAsync(di => di.Id);
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

        await _dbContext.Entry(inspection).Collection(i => i.Items).LoadAsync();
        // Remove obsolete draft items so completion contains only the validated scope.
        foreach (var obsoleteItem in inspection.Items.Where(item => !submittedIds.Contains(item.DeliveryItemId)).ToList())
        {
            _dbContext.InspectionItems.Remove(obsoleteItem);
            inspection.Items.Remove(obsoleteItem);
        }

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
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return ToResponse(inspection);
    }

    private static void ValidateQuantity(decimal quantity, string name)
    {
        if (quantity < 0 || quantity > 9999999999.99m)
            throw Invalid($"{name} must be between 0 and 9999999999.99.");
        if (decimal.Round(quantity, 2) != quantity)
            throw Invalid($"{name} must have at most two decimal places.");
    }

    private static QualityInspectionResponseDto ToResponse(Inspection inspection) => new()
    {
        Id = inspection.Id,
        DeliveryId = inspection.DeliveryId,
        InspectorUserId = inspection.InspectorUserId,
        InspectionDate = inspection.InspectionDate,
        Status = inspection.Status,
        OverallDecision = inspection.OverallDecision,
        Notes = inspection.Notes,
        CreatedAt = inspection.CreatedAt,
        UpdatedAt = inspection.UpdatedAt,
        Items = inspection.Items.OrderBy(item => item.Id).Select(item => new QualityInspectionItemResponseDto
        {
            Id = item.Id,
            DeliveryItemId = item.DeliveryItemId,
            Condition = item.Condition,
            AcceptedQuantity = item.AcceptedQuantity,
            RejectedQuantity = item.RejectedQuantity,
            Remarks = item.Remarks
        }).ToList()
    };

    private static QualityInspectionException Invalid(string message) => new(400, message);
    private static QualityInspectionException Missing(string message) => new(404, message);
    private static QualityInspectionException Conflict(string message) => new(409, message);
}

public class QualityInspectionException : Exception
{
    public int StatusCode { get; }

    public QualityInspectionException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
