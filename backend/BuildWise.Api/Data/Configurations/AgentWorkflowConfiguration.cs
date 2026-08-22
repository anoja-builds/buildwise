using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class AgentWorkflowConfiguration : IEntityTypeConfiguration<AgentWorkflow>
{
    public void Configure(EntityTypeBuilder<AgentWorkflow> builder)
    {
        builder.ToTable("agent_workflows");

        builder.HasKey(aw => aw.Id);

        builder.Property(aw => aw.Objective)
            .IsRequired();

        builder.Property(aw => aw.Status)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(aw => aw.ApprovalStatus)
            .HasConversion<string>()
            .IsRequired();

        builder.HasOne(aw => aw.InitiatedByUser)
            .WithMany()
            .HasForeignKey(aw => aw.InitiatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(aw => aw.Status);
        builder.HasIndex(aw => aw.ApprovalStatus);
    }
}
