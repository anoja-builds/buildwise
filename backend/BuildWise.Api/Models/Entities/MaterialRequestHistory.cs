using BuildWise.Api.Models.Common;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Entities;

public class MaterialRequestHistory : BaseEntity
{
    public int MaterialRequestId { get; set; }
    public MaterialRequest MaterialRequest { get; set; } = null!;
    public int? ChangedByUserId { get; set; }
    public User? ChangedByUser { get; set; }
    public string Action { get; set; } = string.Empty;
    public MaterialRequestStatus? FromStatus { get; set; }
    public MaterialRequestStatus? ToStatus { get; set; }
    public string? Details { get; set; }
}
