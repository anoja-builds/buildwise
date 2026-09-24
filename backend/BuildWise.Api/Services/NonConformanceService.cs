using BuildWise.Api.Data;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class NonConformanceService
{
    private readonly ApplicationDbContext _dbContext;

    public NonConformanceService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<NonConformanceResponseDto>> GetAllAsync()
    {
        var records = await _dbContext.NonConformances.AsNoTracking()
            .Include(nc => nc.InspectionItem)
            .OrderByDescending(nc => nc.CreatedAt).ThenByDescending(nc => nc.Id)
            .ToListAsync();
        return records.Select(ToResponse).ToList();
    }

    public async Task<NonConformanceResponseDto> GetByIdAsync(int id)
    {
        var record = await _dbContext.NonConformances.AsNoTracking()
            .Include(nc => nc.InspectionItem).SingleOrDefaultAsync(nc => nc.Id == id)
            ?? throw Missing("Non-conformance not found.");
        return ToResponse(record);
    }

    public async Task<NonConformanceResponseDto> CreateAsync(CreateNonConformanceDto dto)
    {
        if (dto.InspectionItemId <= 0)
            throw Invalid("InspectionItemId must be positive.");
        if (string.IsNullOrWhiteSpace(dto.IssueDescription))
            throw Invalid("IssueDescription must not be blank.");
        if (!Enum.IsDefined(dto.Severity))
            throw Invalid("Severity must be a valid non-conformance severity.");

        var item = await _dbContext.InspectionItems.Include(i => i.Inspection)
            .SingleOrDefaultAsync(i => i.Id == dto.InspectionItemId)
            ?? throw Missing("Inspection item not found.");
        if (item.Inspection == null)
            throw Missing("Parent inspection not found.");
        if (item.Inspection.Status != InspectionStatus.Completed)
            throw Conflict("Non-conformances can only be created for completed inspections.");
        if (item.RejectedQuantity <= 0)
            throw Invalid("Inspection item must have positive rejected quantity.");

        var correctiveAction = string.IsNullOrWhiteSpace(dto.CorrectiveAction)
            ? null : dto.CorrectiveAction.Trim();
        var now = DateTime.UtcNow;
        var record = new NonConformance
        {
            InspectionItemId = item.Id,
            InspectionItem = item,
            IssueDescription = dto.IssueDescription.Trim(),
            Severity = dto.Severity,
            CorrectiveAction = correctiveAction,
            Status = correctiveAction == null
                ? NonConformanceStatus.Open : NonConformanceStatus.CorrectiveActionRequired,
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.NonConformances.Add(record);
        await _dbContext.SaveChangesAsync();
        return ToResponse(record);
    }

    public Task<NonConformanceResponseDto> UpdateCorrectiveActionAsync(int id, UpdateCorrectiveActionDto dto)
        => UpdateAsync(id, (record, now) =>
        {
            if (record.Status != NonConformanceStatus.Open
                && record.Status != NonConformanceStatus.CorrectiveActionRequired)
                throw Conflict("Corrective action can only be edited for Open or CorrectiveActionRequired non-conformances.");
            if (string.IsNullOrWhiteSpace(dto.CorrectiveAction))
                throw Invalid("CorrectiveAction must not be blank.");

            record.CorrectiveAction = dto.CorrectiveAction.Trim();
            if (record.Status == NonConformanceStatus.Open)
                record.Status = NonConformanceStatus.CorrectiveActionRequired;
            record.UpdatedAt = now;
        });

    public Task<NonConformanceResponseDto> ResolveAsync(int id)
        => UpdateAsync(id, (record, now) =>
        {
            if (record.Status == NonConformanceStatus.Closed)
                throw Conflict("Closed non-conformances cannot be resolved.");
            if (record.Status == NonConformanceStatus.Resolved)
                throw Conflict("Non-conformance has already been resolved.");
            if (string.IsNullOrWhiteSpace(record.CorrectiveAction))
                throw Invalid("A non-empty corrective action is required before resolution.");

            record.Status = NonConformanceStatus.Resolved;
            record.ResolvedAt = now;
            record.UpdatedAt = now;
        });

    public Task<NonConformanceResponseDto> CloseAsync(int id)
        => UpdateAsync(id, (record, now) =>
        {
            if (record.Status != NonConformanceStatus.Resolved)
                throw Conflict("Only resolved non-conformances can be closed.");

            record.Status = NonConformanceStatus.Closed;
            record.UpdatedAt = now;
            // Preserve ResolvedAt.
        });

    private async Task<NonConformanceResponseDto> UpdateAsync(int id, Action<NonConformance, DateTime> update)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        // Serialize competing edits and transitions for this NCR without changing its schema.
        var records = await _dbContext.NonConformances.FromSqlInterpolated(
            $"SELECT * FROM non_conformances WHERE \"Id\" = {id} FOR UPDATE").ToListAsync();
        var record = records.SingleOrDefault() ?? throw Missing("Non-conformance not found.");
        update(record, DateTime.UtcNow);
        await _dbContext.Entry(record).Reference(nc => nc.InspectionItem).LoadAsync();
        var response = ToResponse(record);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return response;
    }

    private static NonConformanceResponseDto ToResponse(NonConformance record)
    {
        var item = record.InspectionItem
            ?? throw Missing("Related inspection item not found.");
        return new NonConformanceResponseDto
        {
            Id = record.Id,
            InspectionItemId = record.InspectionItemId,
            IssueDescription = record.IssueDescription,
            Severity = record.Severity,
            CorrectiveAction = record.CorrectiveAction,
            Status = record.Status,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            ResolvedAt = record.ResolvedAt,
            InspectionId = item.InspectionId,
            DeliveryItemId = item.DeliveryItemId,
            AcceptedQuantity = item.AcceptedQuantity,
            RejectedQuantity = item.RejectedQuantity,
            Condition = item.Condition,
            Remarks = item.Remarks
        };
    }

    private static NonConformanceException Invalid(string message) => new(400, message);
    private static NonConformanceException Missing(string message) => new(404, message);
    private static NonConformanceException Conflict(string message) => new(409, message);
}

public class NonConformanceException : Exception
{
    public int StatusCode { get; }

    public NonConformanceException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
