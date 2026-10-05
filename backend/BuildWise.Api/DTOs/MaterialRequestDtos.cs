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
    int RevisionNumber = 1,
    /// <summary>
    /// The material names on this request, in line order.
    /// <para>
    /// Optional and appended last so every existing caller and the React client
    /// keep working untouched: <c>ItemCount</c> alone cannot tell a Site Officer
    /// which material a row refers to, and the list needs a label a human can
    /// read. A client that ignores this field is unaffected.
    /// </para>
    /// </summary>
    IReadOnlyList<string>? MaterialNames = null,
    /// <summary>
    /// Who raised the request, as `FullName`, plus their id.
    /// <para>
    /// An approver works a queue raised by other people, so "Cement (50kg bag)"
    /// alone does not say who wants it or who to ask when the request is
    /// unclear. Optional and appended last, so the React client is unaffected.
    /// </para>
    /// </summary>
    string? RequestedByName = null,
    int? RequestedByUserId = null
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
