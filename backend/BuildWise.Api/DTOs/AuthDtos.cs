namespace BuildWise.Api.DTOs;

public record RegisterRequestDto(
    string FullName,
    string Email,
    string Password,
    string RoleName
);

public record LoginRequestDto(
    string Email,
    string Password
);

public record UserSummaryDto(
    int Id,
    string FullName,
    string Email,
    List<string> Roles
);

public record AuthResponseDto(
    string Token,
    DateTime ExpiresAtUtc,
    UserSummaryDto User
);
