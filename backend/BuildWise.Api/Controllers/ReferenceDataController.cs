using BuildWise.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

/// <summary>
/// Reference-data endpoints: projects and materials.
/// Used by React and Flutter dropdowns so forms do not hard-code seed values.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class ReferenceDataController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ReferenceDataController(ApplicationDbContext db) => _db = db;

    /// <summary>List active projects/sites for request form dropdowns.</summary>
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
