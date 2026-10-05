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

    /// <summary>
    /// Materials budget allocated to this project, in LKR.
    /// <para>
    /// Used by the deterministic validation step to flag a recommendation whose
    /// total exceeds the allocation (§ Step 5, "Budget validation"). Null means
    /// no budget has been set for the project, in which case the check is
    /// skipped rather than treated as a zero budget.
    /// </para>
    /// </summary>
    public decimal? MaterialBudgetAmount { get; set; }
}