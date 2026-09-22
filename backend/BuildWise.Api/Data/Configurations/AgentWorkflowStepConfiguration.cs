using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class AgentWorkflowStepConfiguration : IEntityTypeConfiguration<AgentWorkflowStep>
{
    public void Configure(EntityTypeBuilder<AgentWorkflowStep> builder)
    {
        builder.ToTable("agent_workflow_steps");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.AgentRole)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.StepName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.HasOne(s => s.AgentWorkflow)
            .WithMany(w => w.Steps)
            .HasForeignKey(s => s.AgentWorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.AgentWorkflowId);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => new { s.AgentWorkflowId, s.StepOrder })
            .IsUnique();

        // C3-facing aliases share the same columns - not mapped separately.
        builder.Ignore(s => s.StructuredResultJson);
        builder.Ignore(s => s.ValidationResultJson);
    }
}
