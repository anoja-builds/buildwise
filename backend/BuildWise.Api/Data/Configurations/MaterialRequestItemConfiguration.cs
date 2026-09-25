using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class MaterialRequestItemConfiguration : IEntityTypeConfiguration<MaterialRequestItem>
{
    public void Configure(EntityTypeBuilder<MaterialRequestItem> builder)
    {
        builder.ToTable("material_request_items");

        builder.HasKey(mri => mri.Id);

        builder.Property(mri => mri.RequestedQuantity)
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(mri => mri.Description).HasMaxLength(1000);
        builder.Property(mri => mri.Unit).HasMaxLength(30);
        builder.Property(mri => mri.Notes).HasMaxLength(2000);

        builder.HasOne(mri => mri.MaterialRequest)
            .WithMany(mr => mr.Items)
            .HasForeignKey(mri => mri.MaterialRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(mri => mri.Material)
            .WithMany()
            .HasForeignKey(mri => mri.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(mri => new { mri.MaterialRequestId, mri.MaterialId })
            .IsUnique();
    }
}
