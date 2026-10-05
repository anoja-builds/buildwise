namespace BuildWise.Api.DTOs;

/// <summary>
/// Purchase order item as seen by the procurement desk. Carries commercial
/// terms (unit price, line total) and is therefore only ever returned to roles
/// inside the procurement function.
/// </summary>
public record PurchaseOrderItemDto(
    int Id,
    int? QuotationItemId,
    int? MaterialId,
    string MaterialName,
    string Unit,
    decimal OrderedQuantity,
    decimal UnitPrice,
    decimal LineTotal
);

/// <summary>
/// Purchase order item as seen by site, receiving and quality roles.
/// Deliberately omits <c>UnitPrice</c> and <c>LineTotal</c>: those are
/// commercially sensitive and are not needed to receive or inspect material.
/// </summary>
public record PurchaseOrderItemReceivingDto(
    int Id,
    int? MaterialId,
    string MaterialName,
    string Unit,
    decimal OrderedQuantity
);

public record PurchaseOrderDto(
    int Id,
    int QuotationId,
    int MaterialRequestId,
    int SupplierId,
    string SupplierName,
    DateOnly OrderDate,
    DateOnly? ExpectedDeliveryDate,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<PurchaseOrderItemDto> Items
);

/// <summary>
/// Purchase order as seen by site, receiving and quality roles.
/// The identical contract to <see cref="PurchaseOrderDto"/> except that
/// <c>totalAmount</c> and every per-line price are <c>null</c>. Keeping the
/// shape identical means the React and Flutter clients need no special-casing —
/// they render the same screen and simply show a redaction notice.
/// </summary>
public record PurchaseOrderReceivingDto(
    int Id,
    int? MaterialRequestId,
    string SupplierName,
    DateOnly OrderDate,
    DateOnly? ExpectedDeliveryDate,
    string Status,
    decimal? TotalAmount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<PurchaseOrderItemReceivingDto> Items
);

public record UpdatePurchaseOrderStatusDto(
    string Status
);
