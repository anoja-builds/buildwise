using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");

        builder.HasKey(po => po.Id);

        builder.Property(po => po.TotalAmount)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(po => po.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.HasOne(po => po.Quotation)
            .WithOne(q => q.PurchaseOrder)
            .HasForeignKey<PurchaseOrder>(po => po.QuotationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(po => po.QuotationId)
            .IsUnique();

        builder.HasIndex(po => po.Status);
        builder.HasIndex(po => po.OrderDate);
    }
}
