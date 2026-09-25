using System.Security.Claims;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/quality-inspections")]
[Authorize]
public class QualityInspectionsController : ControllerBase
{
    private readonly QualityInspectionService _service;

    public QualityInspectionsController(QualityInspectionService service)
    {
        _service = service;
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
    [Authorize(Roles = "SiteEngineer,SiteOfficer,ProcurementManager,SiteManager,QualityInspector,Administrator")]
    public async Task<IActionResult> GetInspections([FromQuery] int? deliveryId, [FromQuery] string? status)
    {
        if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse<BuildWise.Api.Models.Enums.InspectionStatus>(status, true, out _)) return BadRequest(new { message = "Invalid inspection status." });
        var parsed = string.IsNullOrWhiteSpace(status) ? (BuildWise.Api.Models.Enums.InspectionStatus?)null : Enum.Parse<BuildWise.Api.Models.Enums.InspectionStatus>(status, true);
        return Ok(await _service.GetInspectionsAsync(deliveryId, parsed));
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = "SiteEngineer,SiteOfficer,ProcurementManager,SiteManager,QualityInspector,Administrator")]
    public async Task<IActionResult> GetInspection(int id)
    {
        var inspection = await _service.GetInspectionByIdAsync(id);
        return inspection == null ? NotFound(new { message = $"Inspection {id} not found." }) : Ok(inspection);
    }

    /// <summary>
    /// Retrieves all open (non-closed) non-conformance reports.
    /// </summary>
    [HttpGet("non-conformances")]
    [Authorize(Roles = "SiteEngineer,SiteOfficer,ProcurementManager,SiteManager,QualityInspector,Administrator")]
    public async Task<IActionResult> GetNonConformances()
    {
        var ncrs = await _service.GetOpenNonConformancesAsync();
        return Ok(ncrs);
    }

    /// <summary>
    /// Retrieves a specific non-conformance report by ID.
    /// </summary>
    [HttpGet("non-conformances/{id}")]
    [Authorize(Roles = "SiteEngineer,SiteOfficer,ProcurementManager,SiteManager,QualityInspector,Administrator")]
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
    [Authorize(Roles = "ProcurementManager,SiteManager,Administrator")]
    public async Task<IActionResult> TransitionNonConformance(int id, [FromBody] NcrReviewRequest request)
    {
        try { return Ok(await _service.TransitionNonConformanceAsync(id, request, ParseUserId())); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("non-conformances/{id}/status")]
    [Authorize(Roles = "ProcurementManager,SiteManager,Administrator")]
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