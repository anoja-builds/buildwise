using BuildWise.Api.Data;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MaterialRequestsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ProcurementPlanningAgentService _planningAgentService;

    public MaterialRequestsController(
        ApplicationDbContext dbContext,
        ProcurementPlanningAgentService planningAgentService)
    {
        _dbContext = dbContext;
        _planningAgentService = planningAgentService;
    }

    [HttpGet]
    public async Task<IActionResult> GetRequests([FromQuery] string? status, [FromQuery] int? projectId)
    {
        var query = _dbContext.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items)
                .ThenInclude(i => i.Material)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<MaterialRequestStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(r => r.Status == parsedStatus);
        }

        if (projectId.HasValue)
        {
            query = query.Where(r => r.ProjectId == projectId.Value);
        }

        var requests = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

        var result = requests.Select(r => new
        {
            r.Id,
            r.ProjectId,
            ProjectName = r.Project?.Name,
            r.RequestedByUserId,
            r.RequiredDate,
            r.Reason,
            Status = r.Status.ToString(),
            r.CreatedAt,
            ItemsCount = r.Items.Count,
            Items = r.Items.Select(i => new
            {
                i.Id,
                i.MaterialId,
                MaterialName = i.Material?.Name,
                Quantity = i.RequestedQuantity,
                MaterialUnit = i.Material?.Unit,
                i.Notes
            })
        });

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetRequestById(int id)
    {
        var r = await _dbContext.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items)
                .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(req => req.Id == id);

        if (r == null) return NotFound("Material Request not found.");

        var result = new
        {
            r.Id,
            r.ProjectId,
            ProjectName = r.Project?.Name,
            r.RequestedByUserId,
            r.RequiredDate,
            r.Reason,
            Status = r.Status.ToString(),
            r.CreatedAt,
            Items = r.Items.Select(i => new
            {
                i.Id,
                i.MaterialId,
                MaterialName = i.Material?.Name,
                Quantity = i.RequestedQuantity,
                MaterialUnit = i.Material?.Unit,
                i.Notes
            })
        };

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateRequest([FromBody] CreateMaterialRequestDto dto)
    {
        var project = await _dbContext.Projects.FindAsync(dto.ProjectId);
        if (project == null) return BadRequest("Invalid Project ID.");

        var request = new MaterialRequest
        {
            ProjectId = dto.ProjectId,
            RequestedByUserId = dto.RequestedByUserId,
            RequiredDate = DateOnly.FromDateTime(dto.RequiredDate),
            Reason = dto.Reason,
            Status = dto.SubmitImmediately ? MaterialRequestStatus.PendingApproval : MaterialRequestStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.MaterialRequests.Add(request);
        await _dbContext.SaveChangesAsync();

        foreach (var itemDto in dto.Items)
        {
            var item = new MaterialRequestItem
            {
                MaterialRequestId = request.Id,
                MaterialId = itemDto.MaterialId,
                RequestedQuantity = itemDto.Quantity,
                Notes = itemDto.Notes
            };
            _dbContext.MaterialRequestItems.Add(item);
        }

        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRequestById), new { id = request.Id }, new { request.Id, Status = request.Status.ToString() });
    }

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> SubmitRequest(int id)
    {
        var request = await _dbContext.MaterialRequests.FindAsync(id);
        if (request == null) return NotFound("Material Request not found.");

        if (request.Status != MaterialRequestStatus.Draft)
        {
            return BadRequest("Only draft requests can be submitted.");
        }

        request.Status = MaterialRequestStatus.PendingApproval;
        request.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Ok(new { Message = "Material Request submitted successfully.", request.Id, Status = request.Status.ToString() });
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> ApproveRequest(int id, [FromBody] ApproveMaterialRequestDto dto)
    {
        var request = await _dbContext.MaterialRequests.FindAsync(id);
        if (request == null) return NotFound("Material Request not found.");

        if (dto.Decision == ApprovalDecision.Approved)
        {
            request.Status = MaterialRequestStatus.Approved;
        }
        else if (dto.Decision == ApprovalDecision.Rejected)
        {
            request.Status = MaterialRequestStatus.Rejected;
        }
        else
        {
            request.Status = MaterialRequestStatus.PendingApproval;
        }

        request.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Ok(new { Message = $"Request decision recorded: {dto.Decision}", Status = request.Status.ToString() });
    }

    [HttpPost("{id}/plan")]
    public async Task<IActionResult> RunProcurementPlanningAgent(int id, [FromQuery] int? userId)
    {
        try
        {
            var result = await _planningAgentService.EvaluateMaterialRequestPlanAsync(id, userId);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}
