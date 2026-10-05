using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class InspectionEvidenceConfiguration : IEntityTypeConfiguration<InspectionEvidence>
{
    public void Configure(EntityTypeBuilder<InspectionEvidence> builder)
    {
        builder.ToTable("inspection_evidences");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.FileName).HasMaxLength(255).IsRequired();
        builder.Property(e => e.FileUrl).HasMaxLength(2000).IsRequired();
        builder.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.FileSizeBytes).IsRequired();
        builder.HasOne(e => e.Inspection).WithMany(i => i.Evidence).HasForeignKey(e => e.InspectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.InspectionItem).WithMany().HasForeignKey(e => e.InspectionItemId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.UploadedByUser).WithMany().HasForeignKey(e => e.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.InspectionId, e.UploadedAt });
    }
}
