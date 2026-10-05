using System.Security.Claims;
using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authorization;
using BuildWise.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/quality-inspections")]
[Authorize(Policy = Policies.QualityReaders)]
public class QualityInspectionsController : ControllerBase
{
    private readonly QualityInspectionService _service;
    private readonly OperationalAgentClient _agentClient;
    private readonly ApplicationDbContext _db;

    public QualityInspectionsController(
        QualityInspectionService service,
        OperationalAgentClient agentClient,
        ApplicationDbContext db)
    {
        _service = service;
        _agentClient = agentClient;
        _db = db;
    }

    /// <summary>
    /// Runs the QualityRiskAnalysisAgent (:8004) over one completed inspection and
    /// returns its structured assessment, so Component 4 visibly demonstrates the
    /// fourth domain agent rather than only showing the NCRs it produced.
    /// <para>
    /// Advisory only: nothing here changes the inspection, creates an NCR, or
    /// alters any status. The NCRs generated when the inspection was completed
    /// remain the authoritative business record.
    /// </para>
    /// </summary>
    [HttpPost("{id:int}/risk-analysis")]
    public async Task<IActionResult> AnalyzeQualityRisk(int id)
    {
        // Items and materials must be loaded: the agent's input is the per-line
        // inspected/accepted/rejected quantities plus the material name.
        var inspection = await _db.Inspections
            .AsNoTracking()
            .Include(i => i.Items)
                .ThenInclude(item => item.Material)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (inspection is null) return NotFound(new { message = $"Inspection {id} not found." });

        var qualityItems = inspection.Items
            .Select(item => new QualityAgentItem(
                item.Material?.Name ?? $"Material #{item.MaterialId}",
                item.InspectedQuantity,
                item.RejectedQuantity,
                item.AcceptedQuantity,
                item.RejectionReason ?? string.Empty))
            .ToList();

        var analysis = await _agentClient.AnalyzeQualityRiskAsync(inspection.DeliveryId, qualityItems);

        return Ok(new
        {
            InspectionId = inspection.Id,
            DeliveryId = inspection.DeliveryId,
            Agent = "QualityRiskAnalysisAgent",
            Tool = "analyze_quality_risk",
            ExecutionSource = analysis.ExecutionSource,
            RiskLevel = analysis.Output.RiskLevel,
            RequiresNcr = analysis.Output.RequiresNcr,
            SuggestedCorrectiveAction = analysis.Output.SuggestedCorrectiveAction,
            RiskFlags = analysis.Output.RiskFlags,
            TotalInspected = analysis.Output.TotalInspected,
            TotalRejected = analysis.Output.TotalRejected,
            RejectionRatePct = analysis.Output.RejectionRatePct,
            InspectionStatus = inspection.Status.ToString(),
            OverallDecision = inspection.OverallDecision.ToString(),
            AnalyzedAtUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Completes an inspection for a delivery. Validates quantities and
    /// automatically generates Non-Conformance Reports for rejected materials.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "QualityControlOnly")]
    public async Task<IActionResult> CompleteInspection([FromBody] CompleteInspectionDto dto)
    {
        try
        {
            var created = await _service.CompleteInspectionAsync(dto, ParseUserId());
            return Ok(created);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet]
    [Authorize(Policy = Policies.QualityReaders)]
    public async Task<IActionResult> GetInspections([FromQuery] int? deliveryId, [FromQuery] string? status)
    {
        if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse<BuildWise.Api.Models.Enums.InspectionStatus>(status, true, out _)) return BadRequest(new { message = "Invalid inspection status." });
        var parsed = string.IsNullOrWhiteSpace(status) ? (BuildWise.Api.Models.Enums.InspectionStatus?)null : Enum.Parse<BuildWise.Api.Models.Enums.InspectionStatus>(status, true);
        return Ok(await _service.GetInspectionsAsync(deliveryId, parsed));
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = Policies.QualityReaders)]
    public async Task<IActionResult> GetInspection(int id)
    {
        var inspection = await _service.GetInspectionByIdAsync(id);
        return inspection == null ? NotFound(new { message = $"Inspection {id} not found." }) : Ok(inspection);
    }

    /// <summary>
    /// Retrieves all open (non-closed) non-conformance reports.
    /// </summary>
    [HttpGet("non-conformances")]
    [Authorize(Policy = Policies.QualityReaders)]
    public async Task<IActionResult> GetNonConformances()
    {
        var ncrs = await _service.GetOpenNonConformancesAsync();
        return Ok(ncrs);
    }

    /// <summary>
    /// Retrieves a specific non-conformance report by ID.
    /// </summary>
    [HttpGet("non-conformances/{id}")]
    [Authorize(Policy = Policies.QualityReaders)]
    public async Task<IActionResult> GetNonConformance(int id)
    {
        var ncr = await _service.GetNonConformanceByIdAsync(id);
        if (ncr == null)
            return NotFound(new { message = $"Non-conformance {id} not found." });

        return Ok(ncr);
    }

    /// <summary>
    /// Updates the status of a non-conformance (e.g., to Resolved or Closed).
    /// </summary>
    [HttpPost("non-conformances/{id:int}/transition")]
    [Authorize(Policy = Policies.ProcurementDecisionOnly)]
    public async Task<IActionResult> TransitionNonConformance(int id, [FromBody] NcrReviewRequest request)
    {
        try { return Ok(await _service.TransitionNonConformanceAsync(id, request, ParseUserId())); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("non-conformances/{id}/status")]
    [Authorize(Policy = Policies.ProcurementDecisionOnly)]
    public async Task<IActionResult> UpdateNonConformanceStatus(
        int id,
        [FromBody] UpdateNcrStatusRequest request)
    {
        try
        {
            var ncr = await _service.UpdateNonConformanceStatusAsync(id, request.NewStatus);
            return Ok(ncr);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
    private int ParseUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(value, out var userId) || userId <= 0)
            throw new InvalidOperationException("Authenticated user identifier is missing or invalid.");
        return userId;
    }
}

public class UpdateNcrStatusRequest
{
    public BuildWise.Api.Models.Enums.NonConformanceStatus NewStatus { get; set; }
}