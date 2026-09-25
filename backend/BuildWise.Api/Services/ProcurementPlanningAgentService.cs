using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

/// <summary>Agent 1: builds a controlled plan using only allow-listed read tools.</summary>
public class ProcurementPlanningAgentService
{
    public const string AgentRole = "ProcurementPlanningAgent";
    public static readonly IReadOnlySet<string> AllowedToolNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "GetMaterialRequest", "GetMaterialDetails", "GetProjectDetails", "GetAvailableQuotations"
    };
    public static readonly IReadOnlyList<string> ProhibitedCapabilities = new[]
    {
        "Approve procurement", "Issue purchase order", "Modify supplier records"
    };

    private readonly ApplicationDbContext _db;
    public ProcurementPlanningAgentService(ApplicationDbContext db) => _db = db;

    public async Task<ProcurementPlanningOutput> CreatePlanAsync(ProcurementPlanningInput input, CancellationToken ct = default)
    {
        if (input.MaterialRequestId <= 0) throw new ArgumentOutOfRangeException(nameof(input.MaterialRequestId));
        var request = await _db.MaterialRequests.AsNoTracking()
            .Include(r => r.Project).Include(r => r.Items).ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(r => r.Id == input.MaterialRequestId, ct)
            ?? throw new KeyNotFoundException($"Material request #{input.MaterialRequestId} not found.");
        var materialIds = request.Items.Select(i => i.MaterialId).Distinct().ToList();
        var materials = await _db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);
        var quotations = await _db.Quotations.AsNoTracking()
            .Where(q => q.MaterialRequestId == request.Id && q.Status != QuotationStatus.Rejected)
            .Include(q => q.Supplier).Include(q => q.Items).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = request.Items.Select(i => new ProcurementPlanningItemFact(i.Id, i.MaterialId,
            materials.GetValueOrDefault(i.MaterialId)?.Name ?? $"Material #{i.MaterialId}",
            i.Unit ?? materials.GetValueOrDefault(i.MaterialId)?.Unit ?? "Units", i.RequestedQuantity)).ToList();
        var facts = new ProcurementPlanningInputFacts(request.Id, request.ProjectId,
            request.Project?.Name ?? $"Project #{request.ProjectId}", request.Project?.Status.ToString() ?? "Unknown",
            request.RequestDate, request.RequiredDate, request.Priority.ToString(), request.Status.ToString(), items, quotations.Count);
        var objective = string.IsNullOrWhiteSpace(input.Objective)
            ? $"Create a controlled procurement plan for material request #{request.Id}."
            : input.Objective.Trim();
        var steps = new List<ProcurementPlanningStep>
        {
            new(1, "Validate requirement facts", "Confirm approved request, project, positive quantities and required dates.", AgentRole),
            new(2, "Evaluate quotations", "Filter eligible quotations and rank compliant supplier options.", "QuotationSupplierAnalysisAgent"),
            new(3, "Assess delivery risk", "Compare promised delivery information and supplier history with the required date.", "DeliveryRiskAgent"),
            new(4, "Validate recommendation", "Re-check supplier status, validity, coverage, totals and schema.", "ProcurementValidationAgent"),
            new(5, "Pause for human approval", "Present recommendation and warnings to an authorized Procurement Manager.", "ProcurementManager", true)
        };
        var requiredChecks = new List<string>
        {
            "Quantity must be greater than zero for every item.", "Project and request must exist and be eligible.",
            "Supplier must be Active.", "Quotation must be unexpired and belong to the request.",
            "Quotation must cover requested materials and totals must reconcile.",
            "Recommendation must reference a real supported quotation.",
            "Purchase-order authorization requires an approved authorized-manager decision."
        };
        var toolResults = new List<ProcurementPlanningToolResult>
        {
            ToolResult("GetMaterialRequest", request.Items.Count > 0, $"Read request #{request.Id} with {request.Items.Count} item(s)."),
            ToolResult("GetProjectDetails", request.ProjectId > 0, $"Read project #{request.ProjectId}: {facts.ProjectName} [{facts.ProjectStatus}]."),
            ToolResult("GetMaterialDetails", items.Count > 0 && items.All(i => i.MaterialId > 0), $"Resolved {items.Count} material line(s)."),
            ToolResult("GetAvailableQuotations", quotations.Count > 0, $"Found {quotations.Count} non-rejected quotation(s).")
        };
        return new ProcurementPlanningOutput(AgentRole, objective, steps, requiredChecks, BuildRiskFlags(request, quotations, today), facts,
            AllowedToolNames.ToList(), ProhibitedCapabilities.ToList(), toolResults);
    }

    private static List<string> BuildRiskFlags(MaterialRequest request, List<Quotation> quotations, DateOnly today)
    {
        var flags = new List<string>();
        if (request.Status != MaterialRequestStatus.Approved) flags.Add("REQUEST_NOT_APPROVED");
        if (request.Project?.Status != ProjectStatus.Active) flags.Add("PROJECT_NOT_ACTIVE");
        if (request.Items.Count == 0) flags.Add("NO_REQUEST_ITEMS");
        if (request.Items.Any(i => i.RequestedQuantity <= 0)) flags.Add("NON_POSITIVE_QUANTITY");
        if (request.RequiredDate < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3)) flags.Add("INSUFFICIENT_LEAD_TIME");
        if (request.Priority is MaterialRequestPriority.High or MaterialRequestPriority.Urgent) flags.Add("HIGH_PRIORITY_REQUIREMENT");
        if (quotations.Count == 0) flags.Add("NO_AVAILABLE_QUOTATIONS");
        if (quotations.Count == 1) flags.Add("SINGLE_QUOTATION_COMPARISON");
        if (quotations.Any(q => q.Supplier?.Status != SupplierStatus.Active)) flags.Add("INELIGIBLE_SUPPLIER_PRESENT");
        if (quotations.Any(q => q.ValidUntil < today)) flags.Add("EXPIRED_QUOTATION_PRESENT");
        if (quotations.Any(q => q.Status != QuotationStatus.Submitted)) flags.Add("NON_SUBMITTED_QUOTATION_PRESENT");
        return flags.Distinct().ToList();
    }

    private static ProcurementPlanningToolResult ToolResult(string tool, bool succeeded, string summary)
    {
        if (!AllowedToolNames.Contains(tool)) throw new InvalidOperationException($"Tool '{tool}' is not in the planning-agent allow-list.");
        return new ProcurementPlanningToolResult(tool, succeeded, summary);
    }
}
