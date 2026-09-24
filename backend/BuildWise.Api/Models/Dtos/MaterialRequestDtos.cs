using System.ComponentModel.DataAnnotations;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Models.Dtos;

public class CreateMaterialRequestItemDto
{
    [Required]
    public int MaterialId { get; set; }

    [Required]
    [Range(0.01, 1000000)]
    public decimal Quantity { get; set; }

    [Required]
    public string Unit { get; set; } = string.Empty;

    public DateTime RequiredDate { get; set; }

    public string? Notes { get; set; }
}

public class CreateMaterialRequestDto
{
    [Required]
    public int ProjectId { get; set; }

    [Required]
    public int RequestedByUserId { get; set; }

    public DateTime RequiredDate { get; set; } = DateTime.UtcNow.AddDays(7);

    public string Priority { get; set; } = "Medium";

    public string Reason { get; set; } = string.Empty;

    public string? SiteNotes { get; set; }

    public bool SubmitImmediately { get; set; } = true;

    [Required]
    [MinLength(1, ErrorMessage = "Material request must contain at least one item.")]
    public List<CreateMaterialRequestItemDto> Items { get; set; } = new();
}

public class UpdateMaterialRequestDto
{
    public DateTime RequiredDate { get; set; }
    public string Priority { get; set; } = "Medium";
    public string Reason { get; set; } = string.Empty;
    public string? SiteNotes { get; set; }
    public List<CreateMaterialRequestItemDto> Items { get; set; } = new();
}

public class ApproveMaterialRequestDto
{
    [Required]
    public int UserId { get; set; }

    public ApprovalDecision Decision { get; set; } = ApprovalDecision.Approved;

    public string? Comment { get; set; }
}

public class ReviseMaterialRequestDto
{
    [Required]
    public int UserId { get; set; }

    [Required]
    public string RevisionNotes { get; set; } = string.Empty;

    public List<CreateMaterialRequestItemDto> UpdatedItems { get; set; } = new();
}
