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
            .IsRequired();

        builder.Property(po => po.TotalAmount)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.HasOne(po => po.Supplier)
            .WithMany()
            .HasForeignKey(po => po.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(po => po.Project)
            .WithMany()
            .HasForeignKey(po => po.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(po => po.Status);
    }
}
