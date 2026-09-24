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

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<DeliveryItem> DeliveryItems => Set<DeliveryItem>();

    public DbSet<DeliverySchedule> DeliverySchedules => Set<DeliverySchedule>();

    public DbSet<DeliveryIssue> DeliveryIssues => Set<DeliveryIssue>();

    public DbSet<DeliveryEvidence> DeliveryEvidences => Set<DeliveryEvidence>();

    public DbSet<Inspection> Inspections => Set<Inspection>();

    public DbSet<InspectionItem> InspectionItems => Set<InspectionItem>();

    public DbSet<NonConformance> NonConformances => Set<NonConformance>();

    public DbSet<Approval> Approvals => Set<Approval>();

    public DbSet<Rfq> Rfqs => Set<Rfq>();

    public DbSet<RfqSupplier> RfqSuppliers => Set<RfqSupplier>();

    public DbSet<ProcurementRecommendation> ProcurementRecommendations => Set<ProcurementRecommendation>();

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
