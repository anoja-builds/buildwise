namespace BuildWise.Api.DTOs;

// --- Supplier portal -------------------------------------------------------
// Every record below is scoped to the supplier bound to the caller's signed
// JWT `supplier_id` claim. No DTO in this file accepts a supplier id from the
// client, so a supplier cannot read or write another supplier's data by
// tampering with a request body or query string.

/// <summary>The caller's own supplier profile.</summary>
public record SupplierPortalProfileDto(
    int SupplierId,
    string Name,
    string? ContactPerson,
    string? Email,
    string? Phone,
    string? Address,
    string Status
);

/// <summary>An RFQ invitation addressed to the caller's own supplier.</summary>
public record SupplierPortalRfqDto(
    int RfqId,
    int MaterialRequestId,
    string? ProjectName,
    DateOnly RequiredResponseDate,
    string? Notes,
    string RfqStatus,
    string InvitationStatus,
    DateTime IssuedAt,
    bool HasSubmittedQuotation
);

/// <summary>
/// A quotation the caller has submitted. Mirrors the procurement-side
/// <c>QuotationDto</c> but is bound to the caller's own supplier only.
/// </summary>
public record SupplierPortalQuotationDto(
    int Id,
    int? RfqId,
    int MaterialRequestId,
    DateOnly QuotationDate,
    DateOnly ValidUntil,
    DateOnly? PromisedDeliveryDate,
    string Status,
    decimal TotalAmount,
    string? PaymentTerms,
    decimal TransportCharge,
    DateTime CreatedAt,
    List<SupplierPortalQuotationItemDto> Items
);

public record SupplierPortalQuotationItemDto(
    int Id,
    int MaterialRequestItemId,
    string MaterialName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal
);

/// <summary>Payload a supplier uses to submit or replace its own quotation.</summary>
public record SupplierQuotationSubmissionDto(
    int? RfqId,
    DateOnly QuotationDate,
    DateOnly ValidUntil,
    DateOnly? PromisedDeliveryDate,
    string? PaymentTerms,
    decimal TransportCharge,
    List<SupplierQuotationLineDto> Items
);

public record SupplierQuotationLineDto(
    int MaterialRequestItemId,
    decimal Quantity,
    decimal UnitPrice
);

/// <summary>A purchase order awarded to the caller's own supplier.</summary>
public record SupplierPortalPurchaseOrderDto(
    int Id,
    int? MaterialRequestId,
    DateOnly OrderDate,
    DateOnly? ExpectedDeliveryDate,
    string Status,
    decimal TotalAmount,
    List<SupplierPortalPurchaseOrderItemDto> Items
);

public record SupplierPortalPurchaseOrderItemDto(
    int Id,
    string MaterialName,
    string Unit,
    decimal OrderedQuantity,
    decimal UnitPrice,
    decimal LineTotal
);