namespace BuildWise.Api.DTOs;

public record SupplierDto(
    int Id,
    string Name,
    string? ContactPerson,
    string? Email,
    string? Phone,
    string? Address,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateSupplierDto(
    string Name,
    string? ContactPerson,
    string? Email,
    string? Phone,
    string? Address
);

public record UpdateSupplierDto(
    string Name,
    string? ContactPerson,
    string? Email,
    string? Phone,
    string? Address
);

public record UpdateSupplierStatusDto(
    string Status
);

public record SupplierQuotationHistoryItemDto(
    int QuotationId,
    int MaterialRequestId,
    DateOnly QuotationDate,
    DateOnly ValidUntil,
    string Status,
    decimal TotalAmount
);

public record SupplierDetailDto(
    int Id,
    string Name,
    string? ContactPerson,
    string? Email,
    string? Phone,
    string? Address,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<SupplierQuotationHistoryItemDto> QuotationHistory
);
