using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class MaterialRequestConfiguration : IEntityTypeConfiguration<MaterialRequest>
{
    public void Configure(EntityTypeBuilder<MaterialRequest> builder)
    {
        builder.ToTable("material_requests");

        builder.HasKey(mr => mr.Id);

        builder.Property(mr => mr.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(mr => mr.RequestDate)
            .HasDefaultValueSql("CURRENT_DATE");

        builder.Property(mr => mr.Priority)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(MaterialRequestPriority.Normal)
            .HasSentinel(MaterialRequestPriority.Low)
            .IsRequired();

        builder.Property(mr => mr.SiteNotes)
            .HasMaxLength(2000);

        builder.Property(mr => mr.RevisionNumber)
            .HasDefaultValue(1);

        builder.HasOne(mr => mr.Project)
            .WithMany()
            .HasForeignKey(mr => mr.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(mr => mr.RequestedByUser)
            .WithMany()
            .HasForeignKey(mr => mr.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(mr => mr.RevisionOfRequest)
            .WithMany()
            .HasForeignKey(mr => mr.RevisionOfRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(mr => mr.ProjectId);
        builder.HasIndex(mr => mr.RequestedByUserId);
        builder.HasIndex(mr => mr.Status);
    }
}
