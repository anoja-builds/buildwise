using BuildWise.Api.Data;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[Authorize]
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

    [HttpGet("/api/material-requests/{id:int}/procurement-status")]
    [Authorize(Roles = "SiteEngineer,Administrator")]
    public async Task<IActionResult> GetProcurementStatus(int id)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();
        var request = await _dbContext.MaterialRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id);
        if (request == null) return NotFound();
        if (request.RequestedByUserId != actorId && !User.IsInRole("Administrator")) return Forbid();
        var order = await _dbContext.PurchaseOrders.AsNoTracking()
            .Where(p => p.Status != PurchaseOrderStatus.Cancelled && p.Quotation != null && p.Quotation.MaterialRequestId == id)
            .OrderByDescending(p => p.Id).FirstOrDefaultAsync();
        var workflow = await _dbContext.AgentWorkflows.AsNoTracking()
            .Where(w => w.MaterialRequestId == id && w.Steps.Any(s => s.AgentRole == "QuotationSupplierAnalysisAgent"))
            .OrderByDescending(w => w.CreatedAt).ThenByDescending(w => w.Id).FirstOrDefaultAsync();
        var status = order != null ? "PurchaseOrderCreated"
            : request.Status == MaterialRequestStatus.Rejected || workflow?.ApprovalStatus == AgentApprovalStatus.Rejected ? "Rejected"
            : workflow?.Status == WorkflowStatus.AwaitingApproval ? "AwaitingApproval"
            : workflow?.ApprovalStatus == AgentApprovalStatus.RevisionRequested ? "RevisionRequested"
            : workflow?.Status == WorkflowStatus.Failed ? "Failed"
            : await _dbContext.Quotations.AnyAsync(q => q.MaterialRequestId == id) ? "QuotationsInProgress" : "NotStarted";
        return Ok(new BuildWise.Api.DTOs.ProcurementStatusDto(id, status, order?.Id, order?.Status.ToString()));
    }

    [HttpGet("options")]
    [HttpGet("/api/material-requests/options")]
    [Authorize(Roles = "SiteEngineer,Administrator")]
    public async Task<IActionResult> GetOptions()
    {
        var projects = await _dbContext.Projects.AsNoTracking()
            .Where(p => p.Status == ProjectStatus.Active).OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name }).ToListAsync();
        var materials = await _dbContext.Materials.AsNoTracking()
            .Where(m => m.IsActive).OrderBy(m => m.Name)
            .Select(m => new { m.Id, m.Name, m.Unit }).ToListAsync();
        return Ok(new { projects, materials });
    }

    [HttpGet]
    [Authorize(Roles = "SiteEngineer,ProcurementOfficer,ProcurementManager,Administrator")]
    public async Task<IActionResult> GetRequests([FromQuery] string? status, [FromQuery] int? projectId)
    {
        var query = _dbContext.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Quotations)
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
            QuotationCount = r.Quotations.Count,
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
    [Authorize(Roles = "SiteEngineer,ProcurementOfficer,ProcurementManager,Administrator")]
    public async Task<IActionResult> GetRequestById(int id)
    {
        var r = await _dbContext.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Quotations)
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
    [Authorize(Roles = "SiteEngineer,Administrator")]
    public async Task<IActionResult> CreateRequest([FromBody] CreateMaterialRequestDto dto)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();

        if (dto.Items == null || dto.Items.Count == 0)
            return BadRequest("Material request must contain at least one item.");

        var requiredDate = DateOnly.FromDateTime(dto.RequiredDate);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (requiredDate < today)
            return BadRequest("Required date cannot be before today.");

        if (dto.Items.Any(i => i.Quantity <= 0))
            return BadRequest("Quantity must be greater than zero.");

        if (dto.Items.Select(i => i.MaterialId).Distinct().Count() != dto.Items.Count)
            return BadRequest("Duplicate material items are not allowed in the same request.");

        var project = await _dbContext.Projects.FindAsync(dto.ProjectId);
        if (project == null || project.Status != ProjectStatus.Active) return BadRequest("Select an active project.");
        var materialIds = dto.Items.Select(i => i.MaterialId).Distinct().ToList();
        if (materialIds.Count == 0 || await _dbContext.Materials.CountAsync(m => materialIds.Contains(m.Id) && m.IsActive) != materialIds.Count)
            return BadRequest("Select active materials from the catalogue.");

        var request = new MaterialRequest
        {
            ProjectId = dto.ProjectId,
            RequestedByUserId = actorId,
            RequiredDate = requiredDate,
            Reason = dto.Reason,
            Status = dto.SubmitImmediately ? MaterialRequestStatus.PendingApproval : MaterialRequestStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.MaterialRequests.Add(request);

        foreach (var itemDto in dto.Items)
        {
            var item = new MaterialRequestItem
            {
                MaterialRequest = request,
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
    [Authorize(Roles = "SiteEngineer,Administrator")]
    public async Task<IActionResult> SubmitRequest(int id)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();

        var request = await _dbContext.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items)
                .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (request == null) return NotFound("Material Request not found.");

        if (request.RequestedByUserId != actorId && !User.IsInRole("Administrator")) return Forbid();

        if (request.Status != MaterialRequestStatus.Draft)
        {
            return BadRequest("Only draft requests can be submitted.");
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            return BadRequest("Material request must contain at least one item.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.RequiredDate < today)
        {
            return BadRequest("Required date cannot be before today.");
        }

        if (request.Project == null || request.Project.Status != ProjectStatus.Active)
        {
            return BadRequest("Associated project must exist and be active.");
        }

        if (request.Items.Any(i => i.Material == null || !i.Material.IsActive))
        {
            return BadRequest("All materials in the request must exist and be active.");
        }

        if (request.Items.Any(i => i.RequestedQuantity <= 0))
        {
            return BadRequest("Quantity must be greater than zero.");
        }

        if (request.Items.Select(i => i.MaterialId).Distinct().Count() != request.Items.Count)
        {
            return BadRequest("Duplicate material items are not allowed in the same request.");
        }

        request.Status = MaterialRequestStatus.PendingApproval;
        request.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Ok(new { Message = "Material Request submitted successfully.", request.Id, Status = request.Status.ToString() });
    }

    [HttpPost("{id}/approve")]
    [Authorize(Roles = "ProcurementManager,Administrator")]
    public async Task<IActionResult> ApproveRequest(int id, [FromBody] ApproveMaterialRequestDto dto)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();

        var request = await _dbContext.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items)
                .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (request == null) return NotFound("Material Request not found.");

        if (request.Status != MaterialRequestStatus.PendingApproval)
            return BadRequest("Only pending requests can be reviewed.");
        if (!Enum.IsDefined(dto.Decision)) return BadRequest("Invalid approval decision.");

        if (dto.Decision == ApprovalDecision.Approved)
        {
            if (request.Project != null && request.Project.Status != ProjectStatus.Active)
            {
                return BadRequest("Associated project must exist and be active.");
            }

            if (request.Items != null && request.Items.Any(i => i.Material != null && !i.Material.IsActive))
            {
                return BadRequest("All materials in the request must exist and be active.");
            }

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

        var now = DateTime.UtcNow;
        request.UpdatedAt = now;
        _dbContext.Approvals.Add(new Approval
        {
            MaterialRequestId = request.Id,
            ApprovedByUserId = actorId,
            Decision = dto.Decision,
            Comment = dto.Comment,
            DecisionDate = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        // Persist the status and its history row atomically.
        await _dbContext.SaveChangesAsync();

        return Ok(new { Message = $"Request decision recorded: {dto.Decision}", Status = request.Status.ToString() });
    }

    [HttpPost("{id}/plan")]
    [Authorize(Roles = "SiteEngineer,ProcurementManager,Administrator")]
    public async Task<IActionResult> RunProcurementPlanningAgent(int id, [FromQuery] int? userId)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();

        try
        {
            var result = await _planningAgentService.EvaluateMaterialRequestPlanAsync(id, actorId);
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
