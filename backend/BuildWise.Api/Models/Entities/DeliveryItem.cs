using BuildWise.Api.Models.Common;

namespace BuildWise.Api.Models.Entities;

public class DeliveryItem : BaseEntity
{
    public int DeliveryId { get; set; }

    public Delivery? Delivery { get; set; }

    public int MaterialId { get; set; }

    public Material? Material { get; set; }

    public decimal ReceivedQuantity { get; set; }

    public decimal DamagedQuantity { get; set; }
}
