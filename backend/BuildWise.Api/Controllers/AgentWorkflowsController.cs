using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

/// <summary>Read-only operational monitoring for persisted agent executions.</summary>
[ApiController]
[Route("api/agent-workflows")]
[Authorize(Roles = "ProcurementOfficer,ProcurementManager,SiteManager,Administrator")]
public class AgentWorkflowsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public AgentWorkflowsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<AgentWorkflowSummaryDto>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.AgentWorkflows
            .Include(w => w.Steps)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<Models.Enums.WorkflowStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(w => w.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(w => EF.Functions.ILike(w.Objective, $"%{term}%"));
        }

        var total = await query.CountAsync();
        var workflows = await query
            .OrderByDescending(w => w.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResultDto<AgentWorkflowSummaryDto>(
            workflows.Select(MapSummary).ToList(), total, page, pageSize));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AgentWorkflowDetailsDto>> GetOne(int id)
    {
        var workflow = await _db.AgentWorkflows
            .Include(w => w.Steps)
            .Include(w => w.Approvals)
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id);
        if (workflow is null) return NotFound(new { message = $"Workflow #{id} not found." });

        return Ok(new AgentWorkflowDetailsDto(
            MapSummary(workflow),
            workflow.Steps.OrderBy(s => s.StepOrder).Select(MapStep).ToList(),
            workflow.Approvals.OrderBy(a => a.DecisionDate).Select(a => new AgentWorkflowApprovalDto(
                a.Id, a.ReviewedByUserId, a.Decision.ToString(), a.Comment, a.DecisionDate)).ToList()));
    }

    private static AgentWorkflowSummaryDto MapSummary(Models.Entities.AgentWorkflow workflow) => new(
        workflow.Id,
        workflow.Objective,
        workflow.Status.ToString(),
        workflow.ApprovalStatus.ToString(),
        workflow.FinalOutcome,
        workflow.MaterialRequestId,
        workflow.PurchaseOrderId,
        workflow.DeliveryId,
        workflow.InitiatedByUserId,
        workflow.Steps.Count,
        workflow.Steps.Count(s => s.Status == Models.Enums.WorkflowStepStatus.Failed),
        workflow.StartedAt,
        workflow.CompletedAt,
        workflow.CreatedAt);

    private static AgentWorkflowStepDto MapStep(Models.Entities.AgentWorkflowStep step) => new(
        step.Id,
        step.AgentRole,
        step.StepName,
        step.StepOrder,
        step.Status.ToString(),
        step.StructuredResult,
        step.ValidationResult,
        step.ErrorMessage,
        step.StartedAt,
        step.CompletedAt);
}
