using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class MaterialRequestHistoryConfiguration : IEntityTypeConfiguration<MaterialRequestHistory>
{
    public void Configure(EntityTypeBuilder<MaterialRequestHistory> builder)
    {
        builder.ToTable("material_request_history");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Action).HasMaxLength(100).IsRequired();
        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(50);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(50);
        builder.Property(h => h.Details).HasMaxLength(2000);
        builder.HasOne(h => h.MaterialRequest).WithMany(r => r.History).HasForeignKey(h => h.MaterialRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(h => h.ChangedByUser).WithMany().HasForeignKey(h => h.ChangedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(h => new { h.MaterialRequestId, h.CreatedAt });
    }
}
