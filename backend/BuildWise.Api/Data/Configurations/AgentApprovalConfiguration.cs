using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class AgentApprovalConfiguration : IEntityTypeConfiguration<AgentApproval>
{
    public void Configure(EntityTypeBuilder<AgentApproval> builder)
    {
        builder.ToTable("agent_approvals");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Decision)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.HasOne(a => a.AgentWorkflow)
            .WithMany(w => w.Approvals)
            .HasForeignKey(a => a.AgentWorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.AgentWorkflowId);
        builder.HasIndex(a => a.ReviewedByUserId);
        builder.HasIndex(a => a.Decision);
    }
}
