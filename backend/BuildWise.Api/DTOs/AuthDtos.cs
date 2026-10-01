using System.ComponentModel.DataAnnotations;

namespace BuildWise.Api.DTOs;

public record RegisterRequestDto(
    [Required, StringLength(150)] string FullName,
    [Required, StringLength(255)] string Email,
    [Required, MinLength(8)] string Password,
    [Required] string RoleName
);

public record LoginRequestDto(
    [Required, StringLength(255)] string Email,
    [Required] string Password
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
