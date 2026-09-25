namespace BuildWise.Api.DTOs;

public record QuotationItemInputDto(
    int MaterialRequestItemId,
    decimal Quantity,
    decimal UnitPrice
);

public record CreateQuotationDto(
    int SupplierId,
    DateOnly QuotationDate,
    DateOnly ValidUntil,
    int? RfqId,
    List<QuotationItemInputDto> Items,
    DateOnly? PromisedDeliveryDate = null,
    decimal TransportCharge = 0m,
    string? PaymentTerms = null
);

public record QuotationItemDto(
    int Id,
    int MaterialRequestItemId,
    string MaterialName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal
);

public record QuotationDto(
    int Id,
    int MaterialRequestId,
    int SupplierId,
    string SupplierName,
    string SupplierStatus,
    DateOnly QuotationDate,
    DateOnly ValidUntil,
    DateOnly? PromisedDeliveryDate,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAt,
    List<QuotationItemDto> Items,
    decimal TransportCharge = 0m,
    string? PaymentTerms = null
);

public record QuotationOfferDto(
    int QuotationId,
    int SupplierId,
    string SupplierName,
    string SupplierStatus,
    decimal QuantityOffered,
    decimal UnitPrice,
    decimal LineTotal,
    bool CoversFullQuantity
);

public record QuotationComparisonRowDto(
    int MaterialRequestItemId,
    string MaterialName,
    string Unit,
    decimal RequestedQuantity,
    List<QuotationOfferDto> Offers
);

public record QuotationComparisonResponseDto(
    int MaterialRequestId,
    string ProjectName,
    List<QuotationComparisonRowDto> Rows,
    List<QuotationDto> Quotations
);
