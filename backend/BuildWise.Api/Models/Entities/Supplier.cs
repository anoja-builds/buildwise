using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class Supplier : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? ContactPerson { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public SupplierStatus Status { get; set; } = SupplierStatus.Active;
}
