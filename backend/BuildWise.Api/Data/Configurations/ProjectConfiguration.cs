using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.Location)
            .HasMaxLength(300);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .IsRequired();

        // Materials budget for the project. Nullable: null = no allocation set,
        // which the validation step treats as "budget check not applicable"
        // rather than as a zero budget.
        builder.Property(p => p.MaterialBudgetAmount)
            .HasPrecision(14, 2);

        builder.HasIndex(p => p.Status);
    }
}