using BuildWise.Api.Models.Common;

namespace BuildWise.Api.Models.Entities;

public class Material : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Unit { get; set; } = string.Empty;

    public string? Category { get; set; }

    public bool IsActive { get; set; } = true;
}