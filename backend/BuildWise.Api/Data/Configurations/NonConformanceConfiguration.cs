using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class NonConformanceConfiguration : IEntityTypeConfiguration<NonConformance>
{
    public void Configure(EntityTypeBuilder<NonConformance> builder)
    {
        builder.ToTable("non_conformances");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.NcrNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(n => n.Severity)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(n => n.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(n => n.IssueDescription)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(n => n.CorrectiveActionPlan).HasMaxLength(2000);
        builder.Property(n => n.Resolution).HasMaxLength(2000);
        builder.Property(n => n.ReviewNotes).HasMaxLength(2000);
        builder.Property(n => n.QuantityAffected).HasPrecision(12, 2).HasDefaultValue(0).IsRequired();

        builder.HasIndex(n => n.DeliveryId);
        builder.HasIndex(n => n.MaterialId);
        builder.HasIndex(n => n.SupplierId);
        builder.HasIndex(n => n.ResponsibleUserId);

        builder.HasOne(n => n.InspectionItem)
            .WithMany()
            .HasForeignKey(n => n.InspectionItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.ResponsibleUser).WithMany().HasForeignKey(n => n.ResponsibleUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(n => n.ReviewedByUser).WithMany().HasForeignKey(n => n.ReviewedByUserId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(n => n.NcrNumber).IsUnique();
        builder.HasIndex(n => n.Status);
        builder.HasIndex(n => n.InspectionItemId);
    }
}
