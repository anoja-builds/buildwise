namespace BuildWise.Api.DTOs;

public record RfqSupplierDto(
    int Id,
    int SupplierId,
    string SupplierName,
    string? SupplierEmail,
    string? ContactPerson,
    string SupplierStatus,
    string InvitationStatus
);

public record RfqDto(
    int Id,
    int MaterialRequestId,
    string ProjectName,
    DateOnly RequiredResponseDate,
    string? Notes,
    string Status,
    int IssuedByUserId,
    DateTime CreatedAt,
    List<RfqSupplierDto> Suppliers
);

public record CreateRfqRequestDto(
    int MaterialRequestId,
    DateOnly RequiredResponseDate,
    string? Notes,
    List<int> SupplierIds,
    string? AdditionalEmail = null
);

public record AddRfqSuppliersRequestDto(List<int> SupplierIds);

public record CloseRfqRequestDto(string? Reason);

public record SendRfqEmailRequestDto(
    string RecipientEmail,
    string? CustomMessage = null
);
