using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class AgentWorkflowConfiguration : IEntityTypeConfiguration<AgentWorkflow>
{
    public void Configure(EntityTypeBuilder<AgentWorkflow> builder)
    {
        builder.ToTable("agent_workflows");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Objective)
            .IsRequired();

        builder.Property(w => w.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(w => w.ApprovalStatus)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.HasOne(w => w.MaterialRequest)
            .WithMany(mr => mr.Workflows)
            .HasForeignKey(w => w.MaterialRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(w => w.MaterialRequestId);
        builder.HasIndex(w => w.InitiatedByUserId);
        builder.HasIndex(w => w.Status);
        builder.HasIndex(w => w.ApprovalStatus);
    }
}
