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
                && !_dbContext.Inspections.Any(i => i.DeliveryId == d.Id))
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
                    ReceivedQuantity = di.ReceivedQuantity,
                    DamagedQuantity = di.DamagedQuantity
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

        if (await _dbContext.Inspections.AnyAsync(i => i.DeliveryId == delivery.Id))
            throw Conflict("This delivery already has an inspection.");

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
        return await ToResponseAsync(inspection);
    }

    public async Task<QualityInspectionResponseDto> GetInspectionByIdAsync(int id)
    {
        var inspection = await _dbContext.Inspections.AsNoTracking().Include(i => i.Items)
            .SingleOrDefaultAsync(i => i.Id == id)
            ?? throw Missing("Inspection not found.");
        return await ToResponseAsync(inspection);
    }

    public async Task<QualityInspectionResponseDto> CompleteInspectionAsync(int id, CompleteInspectionDto dto)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var inspections = await _dbContext.Inspections.FromSqlInterpolated(
            $"SELECT * FROM inspections WHERE \"Id\" = {id} FOR UPDATE").ToListAsync();
        var inspection = inspections.SingleOrDefault()
            ?? throw Missing("Inspection not found.");
        // Defensive parent-row lock held through commit; inspection starts only after receiving.
        // This lock alone does not coordinate every possible receiving operation.
        var deliveries = await _dbContext.Deliveries.FromSqlInterpolated(
            $"SELECT * FROM deliveries WHERE \"Id\" = {inspection.DeliveryId} FOR UPDATE").ToListAsync();
        if (deliveries.Count == 0)
            throw Missing("Delivery not found.");

        var deliveryItems = await _dbContext.DeliveryItems.AsNoTracking()
            .Where(di => di.DeliveryId == inspection.DeliveryId)
            .ToDictionaryAsync(di => di.Id);
        await _dbContext.Entry(inspection).Collection(i => i.Items).LoadAsync();
        var obsolete = InspectionCompletion.Apply(inspection, dto, deliveryItems);
        _dbContext.InspectionItems.RemoveRange(obsolete);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return await ToResponseAsync(inspection);
    }

    private async Task<QualityInspectionResponseDto> ToResponseAsync(Inspection inspection)
    {
        var response = ToResponse(inspection);
        response.DeliveryReference = await _dbContext.Deliveries.AsNoTracking()
            .Where(d => d.Id == inspection.DeliveryId).Select(d => d.DeliveryReference).SingleAsync();
        response.DeliveryItems = await _dbContext.DeliveryItems.AsNoTracking()
            .Where(di => di.DeliveryId == inspection.DeliveryId && di.ReceivedQuantity > 0)
            .OrderBy(di => di.Id).Select(di => new InspectionDeliveryItemDto
            {
                DeliveryItemId = di.Id, PurchaseOrderItemId = di.PurchaseOrderItemId,
                ReceivedQuantity = di.ReceivedQuantity, DamagedQuantity = di.DamagedQuantity
            }).ToListAsync();
        return response;
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
