using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public sealed class DashboardService
{
    private readonly ApplicationDbContext _db;
    public DashboardService(ApplicationDbContext db) => _db = db;

    public async Task<DashboardResponseDto> GetAsync(int userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
    {
        var normalizedRoles = roles.Distinct(StringComparer.Ordinal).ToArray();
        var primaryRole = ChoosePrimaryRole(normalizedRoles);
        var siteScoped = primaryRole is "SiteEngineer" or "SiteOfficer";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var requestQuery = _db.MaterialRequests.Include(r => r.Project).Include(r => r.Items).AsNoTracking();
        if (siteScoped) requestQuery = requestQuery.Where(r => r.RequestedByUserId == userId);
        var requestList = await requestQuery.ToListAsync(cancellationToken);
        var projectIds = requestList.Select(r => r.ProjectId).Distinct().ToList();

        var poQuery = _db.PurchaseOrders.Include(po => po.Items).Include(po => po.Deliveries).AsNoTracking();
        if (siteScoped) poQuery = poQuery.Where(po => po.ProjectId != null && projectIds.Contains(po.ProjectId.Value));
        var poList = await poQuery.ToListAsync(cancellationToken);
        var poIds = poList.Select(po => po.Id).ToList();
        var deliveryList = await _db.Deliveries.Include(d => d.Items).Include(d => d.PurchaseOrder)
            .AsNoTracking().Where(d => poIds.Contains(d.PurchaseOrderId)).ToListAsync(cancellationToken);
        var deliveryIds = deliveryList.Select(d => d.Id).ToHashSet();
        var inspectionList = await _db.Inspections.Include(i => i.Items).AsNoTracking()
            .Where(i => deliveryIds.Contains(i.DeliveryId)).ToListAsync(cancellationToken);
        var inspectionItems = inspectionList.SelectMany(i => i.Items).ToList();
        var inspectedDeliveryIds = inspectionList.Select(i => i.DeliveryId).ToHashSet();
        var ncrCount = await _db.NonConformances.CountAsync(n => n.Status != NonConformanceStatus.Closed && n.Status != NonConformanceStatus.Resolved, cancellationToken);
        var awaitingApprovals = await _db.AgentWorkflows.CountAsync(w => w.Status == WorkflowStatus.AwaitingApproval && w.ApprovalStatus == AgentApprovalStatus.Pending, cancellationToken);
        var activeRfqs = await _db.Rfqs.CountAsync(r => r.Status == RfqStatus.Issued, cancellationToken);
        var quotationsReceivedToday = await _db.Quotations.CountAsync(q => q.QuotationDate == today, cancellationToken);
        var metrics = new List<DashboardMetricDto>
        {
            Metric("activeRequests", "Active requests submitted", requestList.Count(r => r.Status is not MaterialRequestStatus.Cancelled and not MaterialRequestStatus.Completed)),
            Metric("awaitingProcurement", "Awaiting procurement review", requestList.Count(r => r.Status is MaterialRequestStatus.PendingApproval or MaterialRequestStatus.RfqInProgress or MaterialRequestStatus.AwaitingProcurementApproval)),
            Metric("approvedPendingDelivery", "Approved pending delivery", requestList.Count(r => r.Status == MaterialRequestStatus.Approved)),
            Metric("activeRfqs", "Active RFQs", activeRfqs),
            Metric("quotationsToday", "Quotations received today", quotationsReceivedToday),
            Metric("proposalsAwaitingApproval", "Proposals awaiting approval", awaitingApprovals),
            Metric("confirmedPurchaseOrders", "Confirmed purchase orders", poList.Count(p => p.Status == PurchaseOrderStatus.Confirmed)),
            Metric("monthlySpend", "Monthly landed spend", poList.Where(p => p.OrderDate.Month == today.Month && p.OrderDate.Year == today.Year).Sum(p => p.TotalAmount), "LKR"),
            Metric("deliveriesExpectedToday", "Deliveries expected today", deliveryList.Count(d => d.Status is DeliveryStatus.Scheduled or DeliveryStatus.InTransit && d.PurchaseOrder?.ExpectedDeliveryDate == today)),
            Metric("deliveriesReconciled", "Deliveries received & reconciled", deliveryList.Count(d => d.Status == DeliveryStatus.Received)),
            Metric("discrepanciesLogged", "Discrepancies / shortages", deliveryList.Count(d => d.Status == DeliveryStatus.DiscrepancyReported)),
            Metric("deliveriesAwaitingQuality", "Deliveries awaiting quality check", deliveryList.Count(d => d.Status is DeliveryStatus.Received or DeliveryStatus.DiscrepancyReported && !inspectedDeliveryIds.Contains(d.Id))),
            Metric("qualityPassRate", "Overall quality pass rate", inspectionItems.Count == 0 ? 100 : Math.Round(inspectionItems.Sum(i => i.AcceptedQuantity) / Math.Max(0.0001m, inspectionItems.Sum(i => i.InspectedQuantity)) * 100, 1), "%"),
            Metric("activeNcrs", "Active open NCRs", ncrCount)
        };

        var tasks = BuildTasks(primaryRole, awaitingApprovals, ncrCount);
        var activity = await _db.AuditLogs.Where(a => a.UserId == userId).OrderByDescending(a => a.CreatedAt).Take(8)
            .Select(a => new DashboardActivityDto(a.Id, a.Action, a.RequestPath, a.CreatedAt)).ToListAsync(cancellationToken);
        var alerts = await BuildAlertsAsync(primaryRole, today, cancellationToken);
        return new DashboardResponseDto(primaryRole, normalizedRoles, metrics, tasks, activity, alerts, DateTime.UtcNow);
    }

    private static DashboardMetricDto Metric(string key, string label, decimal value, string? suffix = null) => new(key, label, value, suffix);

    private static string ChoosePrimaryRole(IReadOnlyCollection<string> roles)
    {
        foreach (var role in new[] { "ProcurementManager", "ProcurementOfficer", "QualityInspector", "SiteOfficer", "SiteEngineer", "SiteManager", "Administrator" })
            if (roles.Contains(role)) return role;
        return roles.FirstOrDefault() ?? "TeamMember";
    }


    private static List<DashboardTaskDto> BuildTasks(string role, int awaitingApprovals, int ncrCount) => role switch
    {
        "SiteEngineer" => new List<DashboardTaskDto>
        {
            new("create-request", "Create new material request", "Submit a site requirement with project, item and required date.", "/material-requests?create=1", "Normal"),
            new("track-request", "Track request status", "Review approval and procurement progress for your requests.", "/material-requests", "Normal")
        },
        "ProcurementOfficer" => new List<DashboardTaskDto>
        {
            new("create-rfq", "Create RFQ from approved request", "Invite active suppliers to quote for an approved request.", "/rfqs", "High"),
            new("evaluate", "Run AI supplier evaluation", "Start the persisted four-agent procurement workflow.", "/procurement", "High"),
            new("record-quotation", "Record supplier quotations", "Capture comparable quotation and delivery terms.", "/quotations", "Normal")
        },
        "ProcurementManager" => new List<DashboardTaskDto>
        {
            new("review", "Review pending recommendation", $"{awaitingApprovals} workflow(s) are waiting for your human approval decision.", "/procurement", "High"),
            new("agent-monitor", "Monitor agent workflow", "Inspect persisted planning, analysis, risk and validation steps.", "/agent-workflows", "Normal"),
            new("purchase-orders", "Review purchase orders", "Open confirmed orders and landed spend details.", "/purchase-orders", "Normal")
        },
        "SiteOfficer" => new List<DashboardTaskDto>
        {
            new("receive", "Record today's delivery receiving", "Reconcile a confirmed purchase order against received quantities.", "/deliveries", "High"),
            new("evidence", "Capture defect / damage evidence", "Attach photographic evidence while recording a discrepancy.", "/deliveries", "Normal")
        },
        "QualityInspector" => new List<DashboardTaskDto>
        {
            new("inspect", "Conduct site material inspection", "Record accepted and rejected quantities for a delivery.", "/quality-inspections", "High"),
            new("ncrs", $"Review {ncrCount} active NCR(s)", "Follow corrective actions and close resolved non-conformances.", "/quality-inspections", ncrCount > 0 ? "High" : "Normal"),
            new("supplier-quality", "View supplier quality history", "Trace inspection and NCR evidence by supplier.", "/suppliers", "Normal")
        },
        _ => new List<DashboardTaskDto>()
    };

    private async Task<List<DashboardAlertDto>> BuildAlertsAsync(string role, DateOnly today, CancellationToken cancellationToken)
    {
        var alerts = new List<DashboardAlertDto>();
        if (role is "ProcurementOfficer" or "ProcurementManager" or "SiteManager")
        {
            var expiring = await _db.Quotations.CountAsync(q => q.ValidUntil >= today && q.ValidUntil <= today.AddDays(7) && q.Status != QuotationStatus.Rejected, cancellationToken);
            if (expiring > 0) alerts.Add(new DashboardAlertDto("Warning", "Quotations expiring soon", $"{expiring} quotation(s) expire within seven days.", "/quotations"));
            var suspended = await _db.Suppliers.CountAsync(s => s.Status == SupplierStatus.Suspended, cancellationToken);
            if (suspended > 0) alerts.Add(new DashboardAlertDto("Danger", "Suspended suppliers", $"{suspended} supplier(s) require review before award.", "/suppliers"));
        }
        if (role is "SiteOfficer" or "SiteEngineer")
        {
            var discrepancies = await _db.Deliveries.CountAsync(d => d.Status == DeliveryStatus.DiscrepancyReported, cancellationToken);
            if (discrepancies > 0) alerts.Add(new DashboardAlertDto("Danger", "Delivery discrepancies", $"{discrepancies} receiving record(s) contain shortages or damage.", "/deliveries"));
        }
        if (role == "QualityInspector")
        {
            var pending = await _db.Deliveries.CountAsync(
                d => (d.Status == DeliveryStatus.Received || d.Status == DeliveryStatus.DiscrepancyReported)
                    && !_db.Inspections.Any(i => i.DeliveryId == d.Id),
                cancellationToken);
            if (pending > 0) alerts.Add(new DashboardAlertDto("Warning", "Quality checks waiting", $"{pending} received delivery(s) have no inspection record.", "/quality-inspections"));
        }
        return alerts;
    }
}
