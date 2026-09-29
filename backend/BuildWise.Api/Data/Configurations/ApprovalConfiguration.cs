using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class ApprovalConfiguration : IEntityTypeConfiguration<Approval>
{
    public void Configure(EntityTypeBuilder<Approval> builder)
    {
        builder.ToTable("approvals");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Decision)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.Comment)
            .HasColumnType("text");

        builder.Property(a => a.DecisionDate)
            .IsRequired();

        builder.HasOne(a => a.MaterialRequest)
            .WithMany()
            .HasForeignKey(a => a.MaterialRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.ApprovedByUser)
            .WithMany()
            .HasForeignKey(a => a.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.MaterialRequestId);
        builder.HasIndex(a => a.ApprovedByUserId);
    }
}
