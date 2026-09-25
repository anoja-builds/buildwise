using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildWise.Api.Data.Configurations;

public class RfqConfiguration : IEntityTypeConfiguration<Rfq>
{
    public void Configure(EntityTypeBuilder<Rfq> builder)
    {
        builder.ToTable("rfqs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(2000);
        builder.HasOne(r => r.MaterialRequest).WithMany(mr => mr.Rfqs).HasForeignKey(r => r.MaterialRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Suppliers).WithOne(s => s.Rfq).HasForeignKey(s => s.RfqId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.MaterialRequestId);
        builder.HasIndex(r => r.Status);
    }
}

public class RfqSupplierConfiguration : IEntityTypeConfiguration<RfqSupplier>
{
    public void Configure(EntityTypeBuilder<RfqSupplier> builder)
    {
        builder.ToTable("rfq_suppliers");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.HasOne(s => s.Supplier).WithMany().HasForeignKey(s => s.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.RfqId, s.SupplierId }).IsUnique();
    }
}
