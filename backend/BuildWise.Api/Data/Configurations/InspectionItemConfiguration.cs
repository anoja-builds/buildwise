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

        builder.Property(ii => ii.InspectedQuantity)
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(ii => ii.AcceptedQuantity)
            .HasPrecision(12, 2)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(ii => ii.RejectedQuantity)
            .HasPrecision(12, 2)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(ii => ii.RejectionReason)
            .HasMaxLength(500);

        builder.HasOne(ii => ii.Material)
            .WithMany()
            .HasForeignKey(ii => ii.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ii => ii.InspectionId);
        builder.HasIndex(ii => ii.MaterialId);
    }
}
