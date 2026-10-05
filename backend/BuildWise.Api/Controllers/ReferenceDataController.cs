using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

/// <summary>
/// Reference-data endpoints: projects and materials.
/// Used by React and Flutter dropdowns so forms do not hard-code seed values.
/// <para>
/// Gated to internal staff. Supplier portal users must not enumerate the
/// project/material master data — they only ever see the lines on RFQs that
/// were addressed to them, via <c>SupplierPortalController</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Policy = Policies.InternalStaffOnly)]
public class ReferenceDataController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ReferenceDataController(ApplicationDbContext db) => _db = db;

    /// <summary>
    /// List projects for request-form dropdowns.
    /// <para>
    /// The materials budget is deliberately NOT returned here. This endpoint is
    /// readable by every internal role including site staff, and a budget is
    /// commercial information; it is exposed only through
    /// <c>GET /api/projects/{id}/budget</c>, which is procurement-side.
    /// </para>
    /// </summary>
    [HttpGet("projects")]
    public async Task<IActionResult> GetProjects()
    {
        var projects = await _db.Projects
            .Where(p => p.Status == Models.Enums.ProjectStatus.Active)
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Location, Status = p.Status.ToString() })
            .ToListAsync();
        return Ok(projects);
    }

    /// <summary>List active materials for request form dropdowns.</summary>
    [HttpGet("materials")]
    public async Task<IActionResult> GetMaterials()
    {
        var materials = await _db.Materials
            .Where(m => m.IsActive)
            .OrderBy(m => m.Name)
            .Select(m => new { m.Id, m.Name, m.Unit, m.Category })
            .ToListAsync();
        return Ok(materials);
    }
}

/// <summary>
/// Project materials budget — the input to the Step 5 budget check that the
/// procurement workflow raises against an over-budget recommendation.
/// <para>
/// <b>Access:</b> procurement desk and approvers only. A budget is commercial
/// information, so site, receiving and quality roles are refused, and so is the
/// supplier portal. Writing is restricted further to approvers, because
/// raising an allocation is a management decision, not a procurement desk one.
/// </para>
/// </summary>
[ApiController]
[Route("api/projects")]
[Authorize(Policy = Policies.ProcurementStaffAndAdmin)]
public class ProjectBudgetController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ProjectBudgetController(ApplicationDbContext db) => _db = db;

    /// <summary>Returns the materials budget allocated to a project.</summary>
    [HttpGet("{id:int}/budget")]
    public async Task<ActionResult<ProjectBudgetDto>> GetBudget(int id)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project is null)
            return NotFound(new { message = $"Project #{id} not found." });

        return Ok(new ProjectBudgetDto(project.Id, project.Name, project.MaterialBudgetAmount));
    }

    /// <summary>
    /// Sets or clears a project's materials budget. Approvers only —
    /// a Procurement Officer may read the budget but not change it.
    /// </summary>
    [HttpPut("{id:int}/budget")]
    [Authorize(Policy = Policies.ProcurementDecisionOnly)]
    public async Task<ActionResult<ProjectBudgetDto>> UpdateBudget(int id, [FromBody] UpdateProjectBudgetDto dto)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id);
        if (project is null)
            return NotFound(new { message = $"Project #{id} not found." });

        if (dto.MaterialBudgetAmount is < 0m)
            return BadRequest(new { message = "Material budget cannot be negative." });

        project.MaterialBudgetAmount = dto.MaterialBudgetAmount;
        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new ProjectBudgetDto(project.Id, project.Name, project.MaterialBudgetAmount));
    }
}
