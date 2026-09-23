using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class QuotationItemConfiguration : IEntityTypeConfiguration<QuotationItem>
{
    public void Configure(EntityTypeBuilder<QuotationItem> builder)
    {
        builder.ToTable("quotation_items");

        builder.HasKey(qi => qi.Id);

        builder.Property(qi => qi.Quantity)
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(qi => qi.UnitPrice)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.HasOne(qi => qi.Quotation)
            .WithMany(q => q.Items)
            .HasForeignKey(qi => qi.QuotationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(qi => qi.MaterialRequestItem)
            .WithMany(mri => mri.QuotationItems)
            .HasForeignKey(qi => qi.MaterialRequestItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(qi => new { qi.QuotationId, qi.MaterialRequestItemId })
            .IsUnique();
    }
}
