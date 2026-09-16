using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class NonConformanceConfiguration : IEntityTypeConfiguration<NonConformance>
{
    public void Configure(EntityTypeBuilder<NonConformance> builder)
    {
        builder.ToTable("non_conformances");

        builder.HasKey(nc => nc.Id);

        builder.Property(nc => nc.IssueDescription).IsRequired();

        builder.Property(nc => nc.Severity)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(nc => nc.CorrectiveAction).IsRequired(false);

        builder.Property(nc => nc.Status)
            .HasConversion<string>()
            .HasDefaultValue(NonConformanceStatus.Open)
            .IsRequired();

        builder.Property(nc => nc.ResolvedAt).IsRequired(false);

        builder.HasOne(nc => nc.InspectionItem)
            .WithMany(ii => ii.NonConformances)
            .HasForeignKey(nc => nc.InspectionItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(nc => nc.InspectionItemId);
        builder.HasIndex(nc => nc.Severity);
        builder.HasIndex(nc => nc.Status);
    }
}
