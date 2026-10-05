namespace BuildWise.Api.DTOs;

// --- Project materials budget ---------------------------------------------
// A budget is commercial information: readable and writable only by the
// procurement side, never by site, receiving or quality roles.

public record ProjectBudgetDto(
    int ProjectId,
    string ProjectName,
    decimal? MaterialBudgetAmount
);

public record UpdateProjectBudgetDto(
    // Null clears the allocation, which makes the budget check not applicable
    // rather than treating the project as having a zero budget.
    decimal? MaterialBudgetAmount
);

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
