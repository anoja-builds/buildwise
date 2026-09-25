using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.ToTable("quotations");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.TotalAmount)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.HasIndex(q => new { q.MaterialRequestId, q.PromisedDeliveryDate });

        builder.Property(q => q.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.HasOne(q => q.Supplier)
            .WithMany(s => s.Quotations)
            .HasForeignKey(q => q.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(q => q.MaterialRequest)
            .WithMany(mr => mr.Quotations)
            .HasForeignKey(q => q.MaterialRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(q => q.Rfq)
            .WithMany()
            .HasForeignKey(q => q.RfqId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(q => q.MaterialRequestId);
        builder.HasIndex(q => q.SupplierId);
        builder.HasIndex(q => q.Status);
    }
}
