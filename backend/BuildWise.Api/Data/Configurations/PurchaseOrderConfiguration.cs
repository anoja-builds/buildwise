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

        builder.Property(po => po.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(po => po.TotalAmount)
            .HasPrecision(14, 2)
            .IsRequired();

        // C2: optional link to the winning quotation (1 PO per quotation).
        builder.HasOne(po => po.Quotation)
            .WithOne(q => q.PurchaseOrder)
            .HasForeignKey<PurchaseOrder>(po => po.QuotationId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(po => po.QuotationId)
            .IsUnique();

        // C3: optional direct supplier / project links.
        builder.HasOne(po => po.Supplier)
            .WithMany()
            .HasForeignKey(po => po.SupplierId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(po => po.Project)
            .WithMany()
            .HasForeignKey(po => po.ProjectId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(po => po.Status);
        builder.HasIndex(po => po.OrderDate);
    }
}
