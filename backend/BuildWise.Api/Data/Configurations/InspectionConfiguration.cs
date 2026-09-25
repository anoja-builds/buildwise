using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("inspections");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(i => i.OverallDecision)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(i => i.InspectionCriteria).HasMaxLength(2000);
        builder.Property(i => i.ObservedResult).HasMaxLength(2000);
        builder.Property(i => i.Notes).HasMaxLength(2000);

        builder.Property(i => i.InspectedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(i => i.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(i => i.UpdatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(i => i.Delivery)
            .WithMany()
            .HasForeignKey(i => i.DeliveryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Items)
            .WithOne(ii => ii.Inspection)
            .HasForeignKey(ii => ii.InspectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.Evidence)
            .WithOne(e => e.Inspection)
            .HasForeignKey(e => e.InspectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.DeliveryId);
        builder.HasIndex(i => i.Status);
    }
}
