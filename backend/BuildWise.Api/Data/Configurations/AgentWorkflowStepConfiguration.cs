using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class AgentWorkflowStepConfiguration : IEntityTypeConfiguration<AgentWorkflowStep>
{
    public void Configure(EntityTypeBuilder<AgentWorkflowStep> builder)
    {
        builder.ToTable("agent_workflow_steps");

        builder.HasKey(aws => aws.Id);

        builder.Property(aws => aws.AgentRole)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(aws => aws.StepName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(aws => aws.Status)
            .HasConversion<string>()
            .IsRequired();

        builder.HasOne(aws => aws.AgentWorkflow)
            .WithMany(aw => aw.Steps)
            .HasForeignKey(aws => aws.AgentWorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(aws => aws.Status);
        builder.HasIndex(aws => new { aws.AgentWorkflowId, aws.StepOrder }).IsUnique();
    }
}
