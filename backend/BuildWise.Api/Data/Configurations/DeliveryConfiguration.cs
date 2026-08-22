using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("deliveries");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.DeliveryReference)
            .HasMaxLength(100);

        builder.Property(d => d.Status)
            .HasConversion<string>()
            .IsRequired();

        builder.HasOne(d => d.PurchaseOrder)
            .WithMany(po => po.Deliveries)
            .HasForeignKey(d => d.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.ReceivedByUser)
            .WithMany()
            .HasForeignKey(d => d.ReceivedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(d => d.Status);
        builder.HasIndex(d => d.DeliveryReference);
    }
}
