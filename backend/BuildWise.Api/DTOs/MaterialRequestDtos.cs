namespace BuildWise.Api.DTOs;

public record MaterialRequestItemSummaryDto(
    int Id,
    int MaterialId,
    string MaterialName,
    string Unit,
    decimal RequestedQuantity,
    string? Notes
);

public record MaterialRequestSummaryDto(
    int Id,
    int ProjectId,
    string ProjectName,
    DateOnly RequiredDate,
    string? Reason,
    string Status,
    int ItemCount,
    int QuotationCount
);

public record MaterialRequestDetailDto(
    int Id,
    int ProjectId,
    string ProjectName,
    DateOnly RequiredDate,
    string? Reason,
    string Status,
    List<MaterialRequestItemSummaryDto> Items
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
