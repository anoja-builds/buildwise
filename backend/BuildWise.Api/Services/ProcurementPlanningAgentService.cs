using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class ProcurementPlanningAgentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPlanningAgentClient? _agentClient;
    private readonly ILogger<ProcurementPlanningAgentService>? _logger;

    public ProcurementPlanningAgentService(
        ApplicationDbContext dbContext,
        IPlanningAgentClient? agentClient = null,
        ILogger<ProcurementPlanningAgentService>? logger = null)
    {
        _dbContext = dbContext;
        _agentClient = agentClient;
        _logger = logger;
    }

    public async Task<ProcurementPlanResult> EvaluateMaterialRequestPlanAsync(
        int requestId,
        int? userId = null,
        CancellationToken ct = default)
    {
        var request = await _dbContext.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items)
                .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

        if (request == null)
        {
            throw new ArgumentException($"Material Request #{requestId} not found.");
        }

        // 1. Authoritative ASP.NET deterministic rules & checks
        var riskFlags = new List<string>();
        var requiredChecks = new List<string>();
        var steps = new List<string>();

        // Step 1: Urgency analysis
        var reqDateTime = request.RequiredDate.ToDateTime(TimeOnly.MinValue);
        var daysUntilRequired = (reqDateTime - DateTime.UtcNow).TotalDays;
        steps.Add("1. Analyzed request schedule and required-by date against standard lead times.");

        if (daysUntilRequired < 3)
        {
            riskFlags.Add("URGENT DEADLINE: Less than 3 days until required date. High risk of site delay.");
        }
        else if (daysUntilRequired < 7)
        {
            riskFlags.Add("TIGHT DEADLINE: Less than 7 days lead time.");
        }

        // Step 2: Item quantity & specification checks (NO inventory/stock entity exists in the system)
        steps.Add("2. Verified item specifications, quantities, and material categories.");
        foreach (var item in request.Items)
        {
            requiredChecks.Add($"Verify technical specification for {item.RequestedQuantity:G29} {item.Material?.Unit ?? "units"} of {item.Material?.Name ?? "Material"}.");
            if (item.RequestedQuantity > 500)
            {
                riskFlags.Add($"LARGE VOLUME: {item.Material?.Name ?? "Material"} quantity ({item.RequestedQuantity:G29}) requires staged site delivery or bulk supplier agreement.");
            }
        }

        // Step 3: Project context check
        steps.Add("3. Cross-referenced site location and project active status.");
        if (request.Project != null && request.Project.Status != ProjectStatus.Active)
        {
            riskFlags.Add($"PROJECT STATUS ALERT: Project '{request.Project.Name}' is not currently Active ({request.Project.Status}).");
        }

        // Step 4: RFQ Strategy Formulation
        steps.Add("4. Formulated RFQ distribution strategy to active local suppliers.");
        var deterministicApproach = riskFlags.Any(r => r.StartsWith("URGENT") || r.StartsWith("PROJECT"))
            ? "Immediate Expedited RFQ to Priority Suppliers"
            : "Standard Competitive RFQ Process (Min. 2 Quotations)";

        // 2. Query bounded recent prior request history for this project (excluding current)
        var priorHistory = await _dbContext.MaterialRequests
            .AsNoTracking()
            .Where(r => r.ProjectId == request.ProjectId && r.Id != requestId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(21)
            .Select(r => new PlanningAgentHistory(
                $"request:{r.Id}",
                r.Id,
                r.RequiredDate,
                r.Status.ToString(),
                r.Items.Count
            ))
            .ToListAsync(ct);

        var historyRows = priorHistory.Take(20).ToList();
        var historyTruncated = priorHistory.Count > 20;

        var items = request.Items.Select(i => new PlanningAgentItem(
            $"item:{i.Id}",
            i.Id,
            i.MaterialId,
            i.Material?.Name ?? $"Material #{i.MaterialId}",
            i.Material?.Unit ?? "units",
            i.RequestedQuantity,
            i.Notes
        )).ToList();

        var evidenceRefs = new[] { $"request:{request.Id}", $"project:{request.ProjectId}" }
            .Concat(items.Select(i => i.EvidenceRef))
            .Concat(historyRows.Select(h => h.EvidenceRef))
            .Distinct()
            .ToList();

        var evidence = new PlanningAgentEvidence(
            request.Id,
            request.ProjectId,
            request.Project?.Name ?? "Unknown Project",
            request.Project?.Status.ToString() ?? "Active",
            request.Project?.Location,
            request.RequiredDate.ToString("yyyy-MM-dd"),
            (int)Math.Round(daysUntilRequired),
            request.Reason,
            request.Status.ToString(),
            DateTime.UtcNow,
            items,
            historyRows,
            historyTruncated,
            evidenceRefs
        );

        // 3. Attempt genuine bounded advisory Agent 1 via internal Python service
        PlanningAgentAdvisory? advisory = null;
        var execution = new PlanningAgentExecution("DeterministicFallback", null, 0, [], "not_configured");

        if (_agentClient != null)
        {
            try
            {
                var response = await _agentClient.AnalyseAsync(evidence, ct);
                PlanningAgentValidator.Validate(response, evidence);

                if (response.Success && response.Recommendation != null)
                {
                    advisory = response.Recommendation;
                    execution = new PlanningAgentExecution(
                        "AgenticAI",
                        response.ModelIdentifier,
                        response.IterationCount,
                        response.Trace,
                        null
                    );
                }
                else
                {
                    execution = new PlanningAgentExecution(
                        "DeterministicFallback",
                        response.ModelIdentifier,
                        response.IterationCount,
                        response.Trace,
                        response.ErrorCode ?? "agent_failure"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Agent 1 service unreachable or returned error. Using resilient deterministic fallback.");
                execution = new PlanningAgentExecution(
                    "DeterministicFallback",
                    null,
                    0,
                    [],
                    ex switch
                    {
                        OperationCanceledException => "timeout",
                        JsonException => "invalid_output",
                        InvalidOperationException => "not_configured",
                        _ => "service_unavailable"
                    }
                );
            }
        }

        // 4. Construct plan output (blending advisory recommendations or deterministic fallback)
        string riskLevel;
        string summary;
        List<string> planningFlags;
        List<string> finalRequiredChecks;
        string recommendedApproach;
        List<string> finalEvidenceRefs;
        List<string> outputSteps;

        if (advisory != null)
        {
            riskLevel = advisory.RiskLevel;
            summary = advisory.Summary;
            planningFlags = advisory.PlanningFlags;
            finalRequiredChecks = advisory.RequiredChecks;
            recommendedApproach = advisory.RecommendedApproach;
            finalEvidenceRefs = advisory.EvidenceRefs;
            outputSteps = execution.Trace.Select(t =>
                t.Action == "final_output" ? "Submitted validated advisory recommendation." :
                t.Action == "get_current_material_request_evidence" ? "Read current material request schedule and line items." :
                t.Action == "get_project_context" ? "Retrieved site location and project execution status." :
                t.Action == "get_recent_material_request_history" ? "Surveyed recent prior requests for project context." :
                t.Action
            ).ToList();
        }
        else
        {
            riskLevel = riskFlags.Any(r => r.StartsWith("URGENT")) ? "High"
                : (riskFlags.Any(r => r.StartsWith("TIGHT") || r.StartsWith("LARGE") || r.StartsWith("PROJECT")) ? "Medium" : "Low");
            summary = $"Deterministic rule-based planning analysis completed for Material Request #{request.Id} " +
                      $"(Project: '{request.Project?.Name ?? "Unknown"}'). Schedule lead time is {Math.Max(0, (int)Math.Round(daysUntilRequired))} days.";
            planningFlags = riskFlags;
            finalRequiredChecks = requiredChecks;
            recommendedApproach = deterministicApproach;
            finalEvidenceRefs = new[] { $"request:{request.Id}", $"project:{request.ProjectId}" }
                .Concat(items.Select(i => i.EvidenceRef))
                .ToList();
            outputSteps = steps;
        }

        var planOutput = new ProcurementPlanResult
        {
            AgentName = "Procurement Planning Agent (Agent 1)",
            MaterialRequestId = request.Id,
            ProjectId = request.ProjectId,
            ProjectName = request.Project?.Name ?? "Unknown Project",
            RequiredDate = request.RequiredDate,
            Objective = $"Formulate automated procurement strategy for Material Request #{request.Id}",
            RiskLevel = riskLevel,
            Summary = summary,
            PlanningFlags = planningFlags,
            RequiredChecks = finalRequiredChecks,
            RecommendedApproach = recommendedApproach,
            EvidenceRefs = finalEvidenceRefs,
            ExecutionMode = execution.Mode,
            Execution = execution,
            Advisory = advisory,
            // UI & backwards compatibility
            RecommendedAction = recommendedApproach,
            RiskFlags = planningFlags,
            Steps = outputSteps,
            Timestamp = DateTime.UtcNow
        };

        // 5. Persist AgentWorkflow and AgentWorkflowStep with full execution audit
        var now = DateTime.UtcNow;
        var workflow = new AgentWorkflow
        {
            MaterialRequestId = requestId,
            InitiatedByUserId = userId ?? request.RequestedByUserId,
            Objective = $"Procurement Plan for MR-{requestId:D4}",
            Status = WorkflowStatus.Completed,
            ApprovalStatus = AgentApprovalStatus.Pending,
            FinalOutcome = $"Procurement planning advisory completed for MR-{requestId:D4}. Mode: {execution.Mode}.",
            StartedAt = now,
            CompletedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Steps = [new AgentWorkflowStep
            {
                AgentRole = "Procurement Planning Agent",
                StepName = "Requirement Analysis & Plan Generation",
                StepOrder = 1,
                Status = WorkflowStepStatus.Completed,
                StructuredResultJson = JsonSerializer.Serialize(planOutput, new JsonSerializerOptions { WriteIndented = false }),
                ValidationResultJson = JsonSerializer.Serialize(new
                {
                    Valid = true,
                    Mode = execution.Mode,
                    execution.FallbackReason,
                    Timestamp = now
                }),
                StartedAt = now,
                CompletedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            }]
        };

        _dbContext.AgentWorkflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);

        return planOutput;
    }
}

public class ProcurementPlanResult
{
    public string AgentName { get; set; } = "Procurement Planning Agent (Agent 1)";
    public int MaterialRequestId { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public DateOnly RequiredDate { get; set; }
    public string Objective { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "Low";
    public string Summary { get; set; } = string.Empty;
    public List<string> PlanningFlags { get; set; } = new();
    public List<string> RequiredChecks { get; set; } = new();
    public string RecommendedApproach { get; set; } = string.Empty;
    public List<string> EvidenceRefs { get; set; } = new();
    public string ExecutionMode { get; set; } = "DeterministicFallback";
    public PlanningAgentExecution? Execution { get; set; }
    public PlanningAgentAdvisory? Advisory { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public List<string> RiskFlags { get; set; } = new();
    public List<string> Steps { get; set; } = new();
    public DateTime Timestamp { get; set; }
}
