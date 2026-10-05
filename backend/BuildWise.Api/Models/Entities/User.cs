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

    /// <summary>
    /// The supplier this login represents. Set only for <c>Supplier</c> portal
    /// users; null for every internal staff account. Emitted as a signed
    /// <c>supplier_id</c> JWT claim so supplier-scoped endpoints can filter
    /// without trusting a supplier id supplied by the client.
    /// </summary>
    public int? SupplierId { get; set; }

    public Supplier? Supplier { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
