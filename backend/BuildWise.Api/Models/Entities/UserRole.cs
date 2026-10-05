namespace BuildWise.Api.Models.Entities;

/// <summary>Join entity for the many-to-many user/role assignment (composite key, no surrogate id).</summary>
public class UserRole
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
