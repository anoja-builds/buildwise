using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class ProcurementWorkflowService
{
    /// <summary>agent_workflow_steps.agent_role for the tool-using analysis step (spec §6).</summary>
    private const string AnalysisAgentRole = "QuotationSupplierAnalysisAgent";

    private readonly ApplicationDbContext _db;
    private readonly QuotationAgentClient _agentClient;
    private readonly ProcurementValidationService _validationService;
    private readonly IEmailService _emailService;
    private readonly ILogger<ProcurementWorkflowService> _logger;

    public ProcurementWorkflowService(
        ApplicationDbContext db,
        QuotationAgentClient agentClient,
        ProcurementValidationService validationService,
        IEmailService emailService,
        ILogger<ProcurementWorkflowService> logger)
    {
        _db = db;
        _agentClient = agentClient;
        _validationService = validationService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<StartProcurementWorkflowResponse> StartWorkflowAsync(
        int materialRequestId,
        int initiatedByUserId = 1,
        string? objective = null)
    {
        var request = await _db.MaterialRequests
            .Include(r => r.Items)
            .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(r => r.Id == materialRequestId);

        if (request is null)
            throw new ArgumentException($"Material request #{materialRequestId} not found.");

        if (request.Status != MaterialRequestStatus.Approved)
            throw new InvalidOperationException($"Cannot start procurement workflow: request #{materialRequestId} is '{request.Status}', not 'Approved'.");

        var quotations = await _db.Quotations
            .Where(q => q.MaterialRequestId == materialRequestId && q.Status != QuotationStatus.Rejected)
            .Include(q => q.Supplier)
            .Include(q => q.Items)
            .ToListAsync();

        if (quotations.Count == 0)
            throw new InvalidOperationException($"No active or submitted quotations recorded for request #{materialRequestId}.");

        // 1. Create Workflow in Running state
        var workflow = new AgentWorkflow
        {
            MaterialRequestId = materialRequestId,
            InitiatedByUserId = initiatedByUserId,
            Objective = objective ?? $"Analyze supplier quotations and recommend procurement award for material request #{materialRequestId}.",
            Status = WorkflowStatus.Running,
            ApprovalStatus = AgentApprovalStatus.Pending,
            StartedAt = DateTime.UtcNow
        };

        _db.AgentWorkflows.Add(workflow);
        await _db.SaveChangesAsync();

        // 2. Planning step (ProcurementPlanningAgent): a distinct, lightweight
        // coordinator that turns the objective into a structured, ordered plan
        // before any tool call happens — this is what a downstream agent
        // "delegates" against, satisfying spec §9.1's planning/delegation step.
        var plan = new[]
        {
            "Filter eligible quotations (Active supplier, not expired) and rank by total amount.",
            "Independently re-validate the recommendation against all deterministic business rules.",
            "Pause for Procurement Manager approval before any purchase order can be created."
        };
        var planningStep = new AgentWorkflowStep
        {
            AgentWorkflowId = workflow.Id,
            AgentRole = "ProcurementPlanningAgent",
            StepName = "Plan quotation analysis",
            StepOrder = 1,
            Status = WorkflowStepStatus.Completed,
            StructuredResult = JsonSerializer.Serialize(new { objective = workflow.Objective, plan }, new JsonSerializerOptions { WriteIndented = true }),
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        _db.AgentWorkflowSteps.Add(planningStep);
        await _db.SaveChangesAsync();

        // 3. Prepare Agent payload
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var requestedQuantities = request.Items.ToDictionary(
            i => i.Id.ToString(),
            i => i.RequestedQuantity
        );

        var agentQuotations = quotations.Select(q => new AgentQuotationInput(
            quotation_id: q.Id,
            supplier_id: q.SupplierId,
            supplier_name: q.Supplier?.Name ?? $"Supplier #{q.SupplierId}",
            supplier_status: q.Supplier?.Status.ToString() ?? "Unknown",
            quantity_offered: q.Items.ToDictionary(i => i.MaterialRequestItemId.ToString(), i => i.Quantity),
            unit_prices: q.Items.ToDictionary(i => i.MaterialRequestItemId.ToString(), i => i.UnitPrice),
            total_amount: q.TotalAmount,
            valid: q.ValidUntil >= today
        )).ToList();

        // 4. Analysis step (QuotationSupplierAnalysisAgent): the tool-using agent —
        // calls the allow-listed filter_eligible/rank_by_total tools via the
        // agent microservice (or the rule-based fallback) and produces the
        // structured recommendation.
        var analysisStep = new AgentWorkflowStep
        {
            AgentWorkflowId = workflow.Id,
            AgentRole = AnalysisAgentRole,
            StepName = "Filter & rank eligible quotations (tool use)",
            StepOrder = 2,
            Status = WorkflowStepStatus.Running,
            StartedAt = DateTime.UtcNow
        };
        _db.AgentWorkflowSteps.Add(analysisStep);
        await _db.SaveChangesAsync();

        try
        {
            // Call Agent Microservice
            var recommendation = await _agentClient.AnalyzeAsync(materialRequestId, agentQuotations, requestedQuantities);

            // Validate schema conformance (§5.7)
            var schemaResult = _validationService.ValidateRecommendationSchema(recommendation);
            if (!schemaResult.IsValid)
            {
                analysisStep.Status = WorkflowStepStatus.Failed;
                analysisStep.ErrorMessage = $"Agent schema validation failed: {string.Join("; ", schemaResult.Errors)}";
                analysisStep.CompletedAt = DateTime.UtcNow;
                workflow.Status = WorkflowStatus.Failed;
                workflow.FinalOutcome = "Workflow failed due to agent output schema violation.";
                await _db.SaveChangesAsync();

                return new StartProcurementWorkflowResponse(workflow.Id, workflow.Status.ToString(), analysisStep.ErrorMessage);
            }

            // Record Structured Result
            var recommendationJson = JsonSerializer.Serialize(recommendation, new JsonSerializerOptions { WriteIndented = true });
            analysisStep.StructuredResult = recommendationJson;
            analysisStep.Status = WorkflowStepStatus.Completed;
            analysisStep.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // 5. Validation step (ProcurementValidationAgent): a distinct safety/
            // compliance agent that independently re-derives eligibility against
            // spec §5's business rules — it does not trust the analysis agent's
            // output, it re-checks it.
            var validationStep = new AgentWorkflowStep
            {
                AgentWorkflowId = workflow.Id,
                AgentRole = "ProcurementValidationAgent",
                StepName = "Deterministic business-rule validation",
                StepOrder = 3,
                Status = WorkflowStepStatus.Running,
                StartedAt = DateTime.UtcNow
            };
            _db.AgentWorkflowSteps.Add(validationStep);
            await _db.SaveChangesAsync();

            // Deterministic Validation Gate (§5 rules)
            var validationResult = await _validationService.ValidateRecommendationAsync(
                recommendation!.RecommendedQuotationId!.Value,
                materialRequestId
            );

            var validationResultJson = JsonSerializer.Serialize(validationResult, new JsonSerializerOptions { WriteIndented = true });
            validationStep.ValidationResult = validationResultJson;

            if (!validationResult.IsValid)
            {
                validationStep.Status = WorkflowStepStatus.Failed;
                validationStep.ErrorMessage = $"Deterministic validation failed: {string.Join("; ", validationResult.Errors)}";
                validationStep.CompletedAt = DateTime.UtcNow;

                workflow.Status = WorkflowStatus.Failed;
                workflow.FinalOutcome = $"Recommendation rejected by deterministic validation: {string.Join("; ", validationResult.Errors)}";
                await _db.SaveChangesAsync();

                return new StartProcurementWorkflowResponse(workflow.Id, workflow.Status.ToString(), validationStep.ErrorMessage);
            }

            // Validation passed & ready for human approval
            validationStep.Status = WorkflowStepStatus.Completed;
            validationStep.CompletedAt = DateTime.UtcNow;

            workflow.Status = WorkflowStatus.AwaitingApproval;
            workflow.ApprovalStatus = AgentApprovalStatus.Pending;
            workflow.FinalOutcome = $"Quotation #{recommendation.RecommendedQuotationId} recommended. Awaiting Procurement Manager authorization.";
            await _db.SaveChangesAsync();

            await NotifyProcurementManagersAsync(workflow, recommendation);

            return new StartProcurementWorkflowResponse(
                workflow.Id,
                workflow.Status.ToString(),
                "AI analysis passed deterministic validation and is now awaiting manager approval."
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run procurement workflow for request #{RequestId}", materialRequestId);
            analysisStep.Status = WorkflowStepStatus.Failed;
            analysisStep.ErrorMessage = ex.Message;
            analysisStep.CompletedAt = DateTime.UtcNow;

            workflow.Status = WorkflowStatus.Failed;
            workflow.FinalOutcome = $"Workflow execution error: {ex.Message}";
            await _db.SaveChangesAsync();

            return new StartProcurementWorkflowResponse(workflow.Id, workflow.Status.ToString(), ex.Message);
        }
    }

    private async Task NotifyProcurementManagersAsync(AgentWorkflow workflow, AgentRecommendationDto recommendation)
    {
        try
        {
            var managerEmails = await _db.Users
                .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Name == "ProcurementManager"))
                .Select(u => u.Email)
                .ToListAsync();

            var subject = $"[BuildWise] Procurement recommendation awaiting approval — Request #{workflow.MaterialRequestId}";
            var body =
                $"A procurement recommendation is ready for your review.\n\n" +
                $"Material request: #{workflow.MaterialRequestId}\n" +
                $"Recommended supplier: {recommendation.RecommendedSupplierName}\n" +
                $"Rationale: {recommendation.Rationale}\n\n" +
                $"Review it in BuildWise under Procurement > Approved Requests > Request #{workflow.MaterialRequestId}.";

            foreach (var email in managerEmails)
            {
                await _emailService.SendAsync(email, subject, body);
            }
        }
        catch (Exception ex)
        {
            // A notification failure must never fail the workflow it's reporting on.
            _logger.LogWarning(ex, "Failed to notify Procurement Managers for workflow #{WorkflowId}", workflow.Id);
        }
    }

    public async Task<ProcurementWorkflowDetailsDto?> GetWorkflowDetailsAsync(int workflowId)
    {
        var workflow = await _db.AgentWorkflows
            .Include(w => w.Steps)
            .Include(w => w.Approvals)
            .FirstOrDefaultAsync(w => w.Id == workflowId);

        if (workflow is null) return null;

        var stepDtos = workflow.Steps
            .OrderBy(s => s.StepOrder)
            .Select(s => new AgentWorkflowStepDto(
                s.Id,
                s.AgentRole,
                s.StepName,
                s.StepOrder,
                s.Status.ToString(),
                s.StructuredResult,
                s.ValidationResult,
                s.ErrorMessage,
                s.StartedAt,
                s.CompletedAt
            )).ToList();

        AgentRecommendationDto? recommendation = null;
        ProcurementValidationResultDto? validation = null;

        // Recommendation and validation live on distinct agent steps (analysis vs.
        // validation), so each is read from behind its own guard: the recommendation only
        // from the analysis agent's step (never the planning step's plan JSON), and null
        // when that step failed schema validation.
        var recommendationStep = workflow.Steps
            .Where(s => s.AgentRole == AnalysisAgentRole && s.StructuredResult != null)
            .OrderByDescending(s => s.StepOrder)
            .FirstOrDefault();
        if (recommendationStep?.StructuredResult != null)
        {
            try
            {
                recommendation = JsonSerializer.Deserialize<AgentRecommendationDto>(recommendationStep.StructuredResult);
            }
            catch { }
        }

        var validationStep = workflow.Steps
            .Where(s => s.ValidationResult != null)
            .OrderByDescending(s => s.StepOrder)
            .FirstOrDefault();
        if (validationStep?.ValidationResult != null)
        {
            try
            {
                var valObj = JsonSerializer.Deserialize<ProcurementValidationResult>(validationStep.ValidationResult);
                if (valObj != null)
                    validation = new ProcurementValidationResultDto(valObj.IsValid, valObj.Errors);
            }
            catch { }
        }

        return new ProcurementWorkflowDetailsDto(
            workflow.Id,
            workflow.MaterialRequestId ?? 0,
            workflow.Objective,
            workflow.Status.ToString(),
            workflow.ApprovalStatus.ToString(),
            workflow.FinalOutcome,
            recommendation,
            validation,
            stepDtos,
            workflow.CreatedAt,
            workflow.UpdatedAt
        );
    }

    public async Task<AgentApproval> RecordDecisionAsync(int workflowId, WorkflowDecisionDto dto)
    {
        var workflow = await _db.AgentWorkflows
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == workflowId);

        if (workflow is null)
            throw new ArgumentException($"Workflow #{workflowId} not found.");

        if (workflow.Status != WorkflowStatus.AwaitingApproval)
            throw new InvalidOperationException($"Workflow #{workflowId} is in state '{workflow.Status}', not 'AwaitingApproval'.");

        var normalizedDecision = dto.Decision?.Trim().ToLowerInvariant() switch
        {
            "approve" or "approved" => AgentApprovalStatus.Approved,
            "reject" or "rejected" => AgentApprovalStatus.Rejected,
            "revisionrequested" or "revision_requested" or "requestrevision" or "request_revision" => AgentApprovalStatus.RevisionRequested,
            _ => (AgentApprovalStatus?)null
        };

        if (normalizedDecision is null)
            throw new ArgumentException("Invalid decision. Must be Approve, Reject, or RevisionRequested.");

        var decisionStatus = normalizedDecision.Value;

        var approval = new AgentApproval
        {
            AgentWorkflowId = workflowId,
            ReviewedByUserId = dto.ReviewedByUserId,
            Decision = decisionStatus,
            Comment = dto.Comment,
            DecisionDate = DateTime.UtcNow
        };

        _db.AgentApprovals.Add(approval);
        workflow.ApprovalStatus = decisionStatus;
        workflow.UpdatedAt = DateTime.UtcNow;

        if (decisionStatus == AgentApprovalStatus.Approved)
        {
            workflow.Status = WorkflowStatus.Completed;
            workflow.CompletedAt = DateTime.UtcNow;
            workflow.FinalOutcome = $"Manager Approved recommendation: {dto.Comment}";
            await _db.SaveChangesAsync();

            // Auto-create purchase order on Manager approval (§4.6)
            await CreatePurchaseOrderInternalAsync(workflow);
        }
        else if (decisionStatus == AgentApprovalStatus.Rejected)
        {
            workflow.Status = WorkflowStatus.Failed;
            workflow.CompletedAt = DateTime.UtcNow;
            workflow.FinalOutcome = $"Manager Rejected recommendation: {dto.Comment}";
            await _db.SaveChangesAsync();
        }
        else if (decisionStatus == AgentApprovalStatus.RevisionRequested)
        {
            workflow.Status = WorkflowStatus.Pending;
            workflow.FinalOutcome = $"Manager Requested Revision: {dto.Comment}";
            await _db.SaveChangesAsync();
        }

        return approval;
    }

    public async Task<PurchaseOrder> CreatePurchaseOrderFromWorkflowAsync(int workflowId)
    {
        var workflow = await _db.AgentWorkflows
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == workflowId);

        if (workflow is null)
            throw new ArgumentException($"Workflow #{workflowId} not found.");

        return await CreatePurchaseOrderInternalAsync(workflow);
    }

    private async Task<PurchaseOrder> CreatePurchaseOrderInternalAsync(AgentWorkflow workflow)
    {
        // 1. Validate PO creation preconditions (§5.8 - §5.10)
        var valResult = await _validationService.ValidatePurchaseOrderCreationAsync(workflow.Id);
        if (!valResult.IsValid)
            throw new InvalidOperationException($"Cannot create Purchase Order: {string.Join("; ", valResult.Errors)}");

        // 2. Retrieve winning recommendation from structured_result
        var lastStep = workflow.Steps.LastOrDefault(s => !string.IsNullOrEmpty(s.StructuredResult));
        if (lastStep is null)
            throw new InvalidOperationException("No recommendation payload found in workflow steps.");

        var recommendation = JsonSerializer.Deserialize<AgentRecommendationDto>(lastStep.StructuredResult!);
        if (recommendation?.RecommendedQuotationId is null)
            throw new InvalidOperationException("Recommended quotation ID not present in workflow result.");

        var winnerQuotation = await _db.Quotations
            .Include(q => q.Items)
            .Include(q => q.Supplier)
            .FirstOrDefaultAsync(q => q.Id == recommendation.RecommendedQuotationId.Value);

        if (winnerQuotation is null)
            throw new InvalidOperationException($"Winner quotation #{recommendation.RecommendedQuotationId} not found.");

        // Purchase order creation touches several tables that must succeed or
        // fail together (§5.9: the source quotation and request must never end
        // up partially locked), so it's wrapped in one explicit transaction
        // rather than relying only on SaveChangesAsync's own per-call atomicity.
        // (The in-memory provider used by unit tests doesn't support
        // transactions at all, so only start one against a real relational
        // database — Postgres in every real environment.)
        var useTransaction = _db.Database.IsRelational();
        var transaction = useTransaction ? await _db.Database.BeginTransactionAsync() : null;
        PurchaseOrder po;
        try
        {
            // 3. Create Purchase Order & Items.
            // The supplier is carried both ways: via the winning quotation (C2
            // read path) and directly (C3 read path, non-nullable downstream,
            // incl. DeliveryRiskAgentService) — set both here. Project stays
            // null (C3 backfills it on delivery flows if needed).
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            po = new PurchaseOrder
            {
                QuotationId = winnerQuotation.Id,
                SupplierId = winnerQuotation.SupplierId,
                OrderDate = today,
                ExpectedDeliveryDate = today.AddDays(7),
                Status = PurchaseOrderStatus.Created,
                TotalAmount = winnerQuotation.TotalAmount,
                Items = winnerQuotation.Items.Select(qi => new PurchaseOrderItem
                {
                    QuotationItemId = qi.Id,
                    OrderedQuantity = qi.Quantity,
                    UnitPrice = qi.UnitPrice
                }).ToList()
            };

            _db.PurchaseOrders.Add(po);

            // 4. Update quotation statuses: Winner -> Selected, Losers -> Rejected (§4.6)
            winnerQuotation.Status = QuotationStatus.Selected;

            var losingQuotations = await _db.Quotations
                .Where(q => q.MaterialRequestId == workflow.MaterialRequestId && q.Id != winnerQuotation.Id)
                .ToListAsync();

            foreach (var loser in losingQuotations)
            {
                loser.Status = QuotationStatus.Rejected;
            }

            await _db.SaveChangesAsync();
            if (transaction is not null)
                await transaction.CommitAsync();
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }

        _logger.LogInformation("Purchase Order #{PoId} created for quotation #{QuotationId} (Request #{RequestId})",
            po.Id, winnerQuotation.Id, workflow.MaterialRequestId);

        await NotifyRequesterOfPurchaseOrderAsync(workflow.MaterialRequestId ?? 0, po, winnerQuotation);

        return po;
    }

    private async Task NotifyRequesterOfPurchaseOrderAsync(int materialRequestId, PurchaseOrder po, Quotation winnerQuotation)
    {
        try
        {
            var request = await _db.MaterialRequests.FindAsync(materialRequestId);
            if (request is null) return;

            var requester = await _db.Users.FindAsync(request.RequestedByUserId);
            if (requester is null || string.IsNullOrWhiteSpace(requester.Email)) return;

            var subject = $"[BuildWise] Purchase Order #{po.Id} created for your material request";
            var body =
                $"Good news — procurement is complete for your material request #{materialRequestId}.\n\n" +
                $"Purchase Order: #{po.Id}\n" +
                $"Supplier: {winnerQuotation.Supplier?.Name}\n" +
                $"Total: {po.TotalAmount:N2}\n" +
                $"Expected delivery: {po.ExpectedDeliveryDate}\n\n" +
                $"You can track delivery status in BuildWise.";

            await _emailService.SendAsync(requester.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify requester for PO #{PoId}", po.Id);
        }
    }
}
