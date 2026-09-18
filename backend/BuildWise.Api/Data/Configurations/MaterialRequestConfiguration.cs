using BuildWise.Api.Models.Entities;
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

        builder.HasOne(mr => mr.Project)
            .WithMany()
            .HasForeignKey(mr => mr.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(mr => mr.ProjectId);
        builder.HasIndex(mr => mr.RequestedByUserId);
        builder.HasIndex(mr => mr.Status);
    }
}
