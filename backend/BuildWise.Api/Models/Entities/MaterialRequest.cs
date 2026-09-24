using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class MaterialRequest : BaseEntity
{
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public int RequestedByUserId { get; set; }

    public DateOnly RequiredDate { get; set; }

    public string? Reason { get; set; }

    public MaterialRequestStatus Status { get; set; } = MaterialRequestStatus.Draft;

    public ICollection<MaterialRequestItem> Items { get; set; } = new List<MaterialRequestItem>();

    public ICollection<Quotation> Quotations { get; set; } = new List<Quotation>();

    public ICollection<AgentWorkflow> Workflows { get; set; } = new List<AgentWorkflow>();
}
