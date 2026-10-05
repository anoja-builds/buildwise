namespace BuildWise.Api.Models.Entities;

public class MaterialRequestItem
{
    public int Id { get; set; }
    public int MaterialRequestId { get; set; }
    public MaterialRequest? MaterialRequest { get; set; }
    public int MaterialId { get; set; }
<<<<<<< HEAD
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? MaterialName { get; set; }
=======
>>>>>>> origin/intergration/final-buildwise
    public Material? Material { get; set; }
    public string? Description { get; set; }
    public decimal RequestedQuantity { get; set; }
    public string? Unit { get; set; }
    public DateOnly? RequiredDate { get; set; }
    public string? Notes { get; set; }
    public ICollection<QuotationItem> QuotationItems { get; set; } = new List<QuotationItem>();
}
