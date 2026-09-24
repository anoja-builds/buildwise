using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("inspections");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.DeliveryId).IsRequired();
        builder.Property(i => i.InspectorUserId).IsRequired();
        builder.Property(i => i.InspectionDate).IsRequired();

        builder.Property(i => i.Status)
            .HasConversion<string>()
            .HasDefaultValue(InspectionStatus.Pending)
            .IsRequired();

        builder.Property(i => i.OverallDecision)
            .HasConversion<string>()
            .IsRequired(false);

        builder.Property(i => i.Notes).IsRequired(false);

        builder.HasOne(i => i.Delivery)
            .WithMany()
            .HasForeignKey(i => i.DeliveryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Inspector)
            .WithMany()
            .HasForeignKey(i => i.InspectorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.DeliveryId);
        builder.HasIndex(i => i.InspectorUserId);
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.InspectionDate);
    }
}
