using System.ComponentModel.DataAnnotations;

namespace BuildWise.Api.Models.Dtos;

public record CreateUserDto(
    [Required, StringLength(150)] string FullName,
    [Required, StringLength(255)] string Email,
    [Required, MinLength(8)] string Password,
    [Required] string RoleName);

public record UserStatusDto([Required] bool? IsActive);

public record ManagedUserDto(int Id, string FullName, string Email, bool IsActive,
    List<string> Roles, DateTime CreatedAt);
