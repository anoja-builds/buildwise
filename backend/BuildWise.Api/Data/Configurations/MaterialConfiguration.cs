using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("materials");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(m => m.Description);

        builder.Property(m => m.Unit)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.Category)
            .HasMaxLength(100);

        builder.HasIndex(m => m.Name);

        builder.HasIndex(m => m.Category);
    }
}