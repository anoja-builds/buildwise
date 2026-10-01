using BuildWise.Api.DTOs;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator")]
public class ProcurementWorkflowController : ControllerBase
{
    private readonly ProcurementWorkflowService _workflowService;
    private readonly ILogger<ProcurementWorkflowController> _logger;

    public ProcurementWorkflowController(
        ProcurementWorkflowService workflowService,
        ILogger<ProcurementWorkflowController> logger)
    {
        _workflowService = workflowService;
        _logger = logger;
    }

    [HttpGet("material-requests/{requestId:int}/procurement-workflow")]
    public async Task<IActionResult> GetLatestWorkflow(int requestId)
    {
        var workflow = await _workflowService.GetLatestWorkflowAsync(requestId);
        return Ok(workflow);
    }

    /// <summary>
    /// Starts the Quotation & Supplier Analysis Agent workflow for an approved material request (§4 / §7).
    /// </summary>
    [HttpPost("material-requests/{requestId:int}/procurement-workflow")]
    public async Task<ActionResult<StartProcurementWorkflowResponse>> StartWorkflow(
        int requestId,
        [FromBody] StartProcurementWorkflowRequest? request)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();

        try
        {
            var response = await _workflowService.StartWorkflowAsync(requestId, actorId, request?.Objective);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get current workflow status, AI recommendation, validation results, and warnings.
    /// </summary>
    [HttpGet("procurement-workflow/{workflowId:int}")]
    public async Task<ActionResult<ProcurementWorkflowDetailsDto>> GetWorkflow(int workflowId)
    {
        var details = await _workflowService.GetWorkflowDetailsAsync(workflowId);
        if (details is null)
            return NotFound($"Workflow #{workflowId} not found.");

        return Ok(details);
    }

    /// <summary>
    /// Get full auditable step-by-step execution history of the workflow (§7 / §10).
    /// </summary>
    [HttpGet("procurement-workflow/{workflowId:int}/history")]
    public async Task<ActionResult<IEnumerable<AgentWorkflowStepDto>>> GetWorkflowHistory(int workflowId)
    {
        var details = await _workflowService.GetWorkflowDetailsAsync(workflowId);
        if (details is null)
            return NotFound($"Workflow #{workflowId} not found.");

        return Ok(details.Steps);
    }

    /// <summary>
    /// Record Procurement Manager decision: Approve / Reject / RevisionRequested (§4.5 / §7).
    /// Human-in-the-loop gate: Only an 'Approve' decision unlocks purchase order creation.
    /// </summary>
    [HttpPost("procurement-workflow/{workflowId:int}/decision")]
    [Authorize(Roles = "ProcurementManager,Administrator")]
    public async Task<IActionResult> RecordDecision(int workflowId, [FromBody] WorkflowDecisionDto dto)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();

        try
        {
            var approval = await _workflowService.RecordDecisionAsync(workflowId, dto with { ReviewedByUserId = actorId });
            return Ok(new
            {
                message = $"Decision '{approval.Decision}' recorded successfully.",
                workflowId = approval.AgentWorkflowId,
                decision = approval.Decision.ToString(),
                comment = approval.Comment,
                decisionDate = approval.DecisionDate,
                purchaseOrderId = (await _workflowService.GetWorkflowDetailsAsync(workflowId))?.PurchaseOrderId
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
