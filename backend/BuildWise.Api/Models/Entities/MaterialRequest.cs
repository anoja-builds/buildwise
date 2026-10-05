using System.ComponentModel.DataAnnotations.Schema;
using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class MaterialRequest : BaseEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>
    /// The project named as plain text on the request form, used when no
    /// <see cref="ProjectId"/> is supplied.
    /// <para>
    /// A site engineer types the project they are working on; they should not
    /// have to know an internal id, and a project raised on a new site may not
    /// exist yet. <c>MaterialRequestService</c> resolves this name against the
    /// project list (case-insensitively) and creates the project when nothing
    /// matches. An id, when present, always wins because it is exact.
    /// </para>
    /// <para>
    /// Not a column: this is request input only, and the request itself always
    /// ends up pointing at a real <see cref="ProjectId"/>.
    /// </para>
    /// </summary>
    [NotMapped]
    public string? ProjectName { get; set; }

    public int RequestedByUserId { get; set; }
    public User? RequestedByUser { get; set; }
    public DateOnly RequestDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly RequiredDate { get; set; }
    public MaterialRequestPriority Priority { get; set; } = MaterialRequestPriority.Normal;
    public string? Reason { get; set; }
    public string? SiteNotes { get; set; }
    public MaterialRequestStatus Status { get; set; } = MaterialRequestStatus.Draft;
    public int? RevisionOfRequestId { get; set; }
    public MaterialRequest? RevisionOfRequest { get; set; }
    public int RevisionNumber { get; set; } = 1;

    public ICollection<MaterialRequestItem> Items { get; set; } = new List<MaterialRequestItem>();
    public ICollection<Quotation> Quotations { get; set; } = new List<Quotation>();
    public ICollection<Rfq> Rfqs { get; set; } = new List<Rfq>();
    public ICollection<AgentWorkflow> Workflows { get; set; } = new List<AgentWorkflow>();
    public ICollection<Approval> Approvals { get; set; } = new List<Approval>();
    public ICollection<MaterialRequestHistory> History { get; set; } = new List<MaterialRequestHistory>();
}
