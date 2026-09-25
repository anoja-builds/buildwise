using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class DeliveryItemConfiguration : IEntityTypeConfiguration<DeliveryItem>
{
    public void Configure(EntityTypeBuilder<DeliveryItem> builder)
    {
        builder.ToTable("delivery_items");

        builder.HasKey(di => di.Id);

        builder.Property(di => di.ReceivedQuantity)
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(di => di.DamagedQuantity)
            .HasPrecision(12, 2)
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasOne(di => di.Delivery)
            .WithMany(d => d.Items)
            .HasForeignKey(di => di.DeliveryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(di => di.Material)
            .WithMany()
            .HasForeignKey(di => di.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
