using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class Project : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Location { get; set; }

    public string? Description { get; set; }

    public ProjectStatus Status { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }
}