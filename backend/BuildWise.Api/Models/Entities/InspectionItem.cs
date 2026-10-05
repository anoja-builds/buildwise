using BuildWise.Api.Models.Entities;

namespace BuildWise.Api.Models.Entities;

public class InspectionItem
{
    public int Id { get; set; }
    public int InspectionId { get; set; }
    public Inspection? Inspection { get; set; }
    public int MaterialId { get; set; }
    public Material? Material { get; set; }
    public decimal InspectedQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public string RejectionReason { get; set; } = string.Empty;
}