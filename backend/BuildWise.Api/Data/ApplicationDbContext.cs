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

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<DeliveryItem> DeliveryItems => Set<DeliveryItem>();

    public DbSet<DeliverySchedule> DeliverySchedules => Set<DeliverySchedule>();

    public DbSet<DeliveryIssue> DeliveryIssues => Set<DeliveryIssue>();

    public DbSet<DeliveryEvidence> DeliveryEvidences => Set<DeliveryEvidence>();

    public DbSet<Inspection> Inspections => Set<Inspection>();

    public DbSet<InspectionItem> InspectionItems => Set<InspectionItem>();

    public DbSet<NonConformance> NonConformances => Set<NonConformance>();

    public DbSet<AgentWorkflow> AgentWorkflows => Set<AgentWorkflow>();

    public DbSet<AgentWorkflowStep> AgentWorkflowSteps => Set<AgentWorkflowStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ApplicationDbContext).Assembly);
    }
}
