using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class PurchaseOrderItemConfiguration : IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> builder)
    {
        builder.ToTable("purchase_order_items");

        builder.HasKey(poi => poi.Id);

        builder.Property(poi => poi.OrderedQuantity)
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(poi => poi.UnitPrice)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.HasOne(poi => poi.PurchaseOrder)
            .WithMany(po => po.Items)
            .HasForeignKey(poi => poi.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(poi => poi.QuotationItem)
            .WithOne(qi => qi.PurchaseOrderItem)
            .HasForeignKey<PurchaseOrderItem>(poi => poi.QuotationItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(poi => new { poi.PurchaseOrderId, poi.QuotationItemId })
            .IsUnique();
    }
}
