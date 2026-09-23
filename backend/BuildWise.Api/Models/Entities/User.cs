using BuildWise.Api.Models.Common;

namespace BuildWise.Api.Models.Entities;

/// <summary>
/// Shared application user (Core/shared per the team ERD). Owned collectively —
/// any component may read it, only auth endpoints write it.
/// </summary>
public class User : BaseEntity
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
