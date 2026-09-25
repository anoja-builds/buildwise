namespace BuildWise.Api.DTOs;

public record MaterialRequestItemSummaryDto(
    int Id,
    int MaterialId,
    string MaterialName,
    string Unit,
    decimal RequestedQuantity,
    string? Notes,
    string? Description = null,
    DateOnly? RequiredDate = null
);

public record MaterialRequestSummaryDto(
    int Id,
    int ProjectId,
    string ProjectName,
    DateOnly RequiredDate,
    string? Reason,
    string Status,
    int ItemCount,
    int QuotationCount,
    DateOnly RequestDate = default,
    string Priority = "Normal",
    string? SiteNotes = null,
    int? RevisionOfRequestId = null,
    int RevisionNumber = 1
);

public record MaterialRequestDetailDto(
    int Id,
    int ProjectId,
    string ProjectName,
    DateOnly RequiredDate,
    string? Reason,
    string Status,
    List<MaterialRequestItemSummaryDto> Items,
    DateOnly RequestDate = default,
    string Priority = "Normal",
    string? SiteNotes = null,
    int? RevisionOfRequestId = null,
    int RevisionNumber = 1
);

public record MaterialRequestHistoryDto(
    int Id,
    string Action,
    string? FromStatus,
    string? ToStatus,
    int? ChangedByUserId,
    string? Details,
    DateTime CreatedAt
);

public record ReviseMaterialRequestRequestDto(
    DateOnly RequiredDate,
    string? Reason,
    string? SiteNotes,
    string Priority,
    List<MaterialRequestItemRevisionDto> Items
);

public record MaterialRequestItemRevisionDto(
    int MaterialId,
    decimal RequestedQuantity,
    string? Unit,
    string? Description,
    DateOnly? RequiredDate,
    string? Notes
);

/// <summary>
/// Deliberately minimal, read-only procurement status for the Site Engineer's
/// Flutter view (spec §9): no supplier names, prices, or quotation detail.
/// </summary>
public record ProcurementStatusDto(
    int MaterialRequestId,
    string Status, // "NotStarted" | "QuotationsInProgress" | "AwaitingApproval" | "PurchaseOrderCreated" | "Rejected"
    int? PurchaseOrderId,
    string? PurchaseOrderStatus
);
