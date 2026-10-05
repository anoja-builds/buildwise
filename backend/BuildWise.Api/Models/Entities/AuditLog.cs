using BuildWise.Api.Models.Common;

namespace BuildWise.Api.Models.Entities;

public class AuditLog : BaseEntity
{
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string HttpMethod { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string? IpAddress { get; set; }
    public string? MetadataJson { get; set; }
}
