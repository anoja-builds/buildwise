using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class InspectionItemConfiguration : IEntityTypeConfiguration<InspectionItem>
{
    public void Configure(EntityTypeBuilder<InspectionItem> builder)
    {
        builder.ToTable("inspection_items");

        builder.HasKey(ii => ii.Id);

        builder.Property(ii => ii.AcceptedQuantity)
            .HasPrecision(12, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(ii => ii.RejectedQuantity)
            .HasPrecision(12, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(ii => ii.Condition)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(ii => ii.Remarks).IsRequired(false);

        builder.HasOne(ii => ii.Inspection)
            .WithMany(i => i.Items)
            .HasForeignKey(ii => ii.InspectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ii => ii.DeliveryItem)
            .WithMany()
            .HasForeignKey(ii => ii.DeliveryItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ii => ii.InspectionId);
        builder.HasIndex(ii => ii.DeliveryItemId);
        builder.HasIndex(ii => new { ii.InspectionId, ii.DeliveryItemId }).IsUnique();
    }
}
