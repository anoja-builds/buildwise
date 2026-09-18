using BuildWise.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<MaterialRequest> MaterialRequests => Set<MaterialRequest>();

    public DbSet<MaterialRequestItem> MaterialRequestItems => Set<MaterialRequestItem>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Quotation> Quotations => Set<Quotation>();

    public DbSet<QuotationItem> QuotationItems => Set<QuotationItem>();

    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();

    public DbSet<AgentWorkflow> AgentWorkflows => Set<AgentWorkflow>();

    public DbSet<AgentWorkflowStep> AgentWorkflowSteps => Set<AgentWorkflowStep>();

    public DbSet<AgentApproval> AgentApprovals => Set<AgentApproval>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ApplicationDbContext).Assembly);
    }
}