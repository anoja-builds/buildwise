namespace BuildWise.Api.Models.Entities;

/// <summary>
/// Shared application role (Core/shared per the team ERD). Examples: SiteEngineer,
/// ProjectManager, ProcurementOfficer, ProcurementManager, ReceivingOfficer,
/// QualityInspector, Administrator.
/// </summary>
public class Role
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
