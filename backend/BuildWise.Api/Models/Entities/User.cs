using BuildWise.Api.Models.Common;

namespace BuildWise.Api.Models.Entities;

public class User : BaseEntity
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
