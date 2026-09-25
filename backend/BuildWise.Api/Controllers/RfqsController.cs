using System.Security.Claims;
using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/rfqs")]
[Authorize(Roles = "ProcurementOfficer,ProcurementManager,SiteManager,Administrator")]
public class RfqsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<RfqsController> _logger;

    public RfqsController(ApplicationDbContext db, ILogger<RfqsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RfqDto>>> GetAll([FromQuery] string? status)
    {
        var query = _db.Rfqs
            .Include(r => r.MaterialRequest).ThenInclude(r => r.Project)
            .Include(r => r.Suppliers).ThenInclude(s => s.Supplier)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<RfqStatus>(status, true, out var parsed))
            query = query.Where(r => r.Status == parsed);
        var rows = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return Ok(rows.Select(Map));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RfqDto>> GetById(int id)
    {
        var rfq = await _db.Rfqs
            .Include(r => r.MaterialRequest).ThenInclude(r => r.Project)
            .Include(r => r.Suppliers).ThenInclude(s => s.Supplier)
            .FirstOrDefaultAsync(r => r.Id == id);
        return rfq is null ? NotFound(new { message = $"RFQ #{id} not found." }) : Ok(Map(rfq));
    }

    [HttpPost]
    [Authorize(Roles = "ProcurementOfficer,Administrator")]
    public async Task<ActionResult<RfqDto>> Create([FromBody] CreateRfqRequestDto dto)
    {
        var request = await _db.MaterialRequests.FirstOrDefaultAsync(r => r.Id == dto.MaterialRequestId);
        if (request is null) return NotFound(new { message = $"Material request #{dto.MaterialRequestId} not found." });
        if (request.Status != MaterialRequestStatus.Approved)
            return BadRequest(new { message = "RFQs can only be issued for Approved material requests." });
        if (dto.RequiredResponseDate < DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest(new { message = "Required response date cannot be in the past." });
        if (dto.SupplierIds is null || dto.SupplierIds.Count == 0)
            return BadRequest(new { message = "Select at least one supplier for the RFQ." });

        var supplierIds = dto.SupplierIds.Distinct().ToList();
        var suppliers = await _db.Suppliers.Where(s => supplierIds.Contains(s.Id)).ToListAsync();
        if (suppliers.Count != supplierIds.Count) return BadRequest(new { message = "One or more suppliers do not exist." });
        if (suppliers.Any(s => s.Status != SupplierStatus.Active)) return BadRequest(new { message = "Only Active suppliers can be invited to an RFQ." });

        var rfq = new Rfq
        {
            MaterialRequestId = dto.MaterialRequestId,
            IssuedByUserId = ParseUserId(),
            RequiredResponseDate = dto.RequiredResponseDate,
            Notes = dto.Notes?.Trim(),
            Status = RfqStatus.Issued,
            Suppliers = suppliers.Select(s => new RfqSupplier { Supplier = s, SupplierId = s.Id, Status = RfqSupplierStatus.Invited }).ToList()
        };
        _db.Rfqs.Add(rfq);
        await _db.SaveChangesAsync();
        _logger.LogInformation("RFQ #{RfqId} issued for material request #{RequestId} to {SupplierCount} suppliers", rfq.Id, request.Id, suppliers.Count);
        return CreatedAtAction(nameof(GetById), new { id = rfq.Id }, Map(await Load(rfq.Id)));
    }

    [HttpPost("{id:int}/suppliers")]
    [Authorize(Roles = "ProcurementOfficer,Administrator")]
    public async Task<ActionResult<RfqDto>> AddSuppliers(int id, [FromBody] AddRfqSuppliersRequestDto dto)
    {
        var rfq = await _db.Rfqs.Include(r => r.Suppliers).FirstOrDefaultAsync(r => r.Id == id);
        if (rfq is null) return NotFound(new { message = $"RFQ #{id} not found." });
        if (rfq.Status != RfqStatus.Issued) return BadRequest(new { message = "Suppliers can only be added while the RFQ is Issued." });
        var existing = rfq.Suppliers.Select(s => s.SupplierId).ToHashSet();
        var ids = dto.SupplierIds.Distinct().Where(id => !existing.Contains(id)).ToList();
        var suppliers = await _db.Suppliers.Where(s => ids.Contains(s.Id) && s.Status == SupplierStatus.Active).ToListAsync();
        if (suppliers.Count != ids.Count) return BadRequest(new { message = "All new suppliers must exist and be Active." });
        foreach (var supplier in suppliers)
            rfq.Suppliers.Add(new RfqSupplier { Rfq = rfq, SupplierId = supplier.Id, Status = RfqSupplierStatus.Invited });
        rfq.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(Map(await Load(id)));
    }

    [HttpPost("{id:int}/close")]
    [Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator")]
    public async Task<ActionResult<RfqDto>> Close(int id, [FromBody] CloseRfqRequestDto? request)
    {
        var rfq = await _db.Rfqs.FirstOrDefaultAsync(r => r.Id == id);
        if (rfq is null) return NotFound(new { message = $"RFQ #{id} not found." });
        if (rfq.Status is RfqStatus.Closed or RfqStatus.Cancelled) return BadRequest(new { message = "RFQ is already closed or cancelled." });
        rfq.Status = RfqStatus.Closed;
        rfq.Notes = string.IsNullOrWhiteSpace(request?.Reason) ? rfq.Notes : $"{rfq.Notes}\nClosed: {request!.Reason.Trim()}";
        rfq.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(Map(await Load(id)));
    }

    private async Task<Rfq> Load(int id) => await _db.Rfqs
        .Include(r => r.MaterialRequest).ThenInclude(r => r.Project)
        .Include(r => r.Suppliers).ThenInclude(s => s.Supplier)
        .FirstAsync(r => r.Id == id);

    private int ParseUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture);

    private static RfqDto Map(Rfq r) => new(
        r.Id,
        r.MaterialRequestId,
        r.MaterialRequest?.Project?.Name ?? $"Project #{r.MaterialRequest?.ProjectId ?? 0}",
        r.RequiredResponseDate,
        r.Notes,
        r.Status.ToString(),
        r.IssuedByUserId,
        r.CreatedAt,
        r.Suppliers.Select(s => new RfqSupplierDto(s.Id, s.SupplierId, s.Supplier.Name, s.Supplier.Status.ToString(), s.Status.ToString())).ToList());


}
