namespace BuildWise.Api.DTOs;

public record PurchaseOrderItemDto(
    int Id,
    int QuotationItemId,
    string MaterialName,
    string Unit,
    decimal OrderedQuantity,
    decimal UnitPrice,
    decimal LineTotal
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

public record UpdatePurchaseOrderStatusDto(
    string Status
);
