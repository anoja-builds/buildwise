using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator")]
public class SuppliersController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SuppliersController> _logger;

    public SuppliersController(ApplicationDbContext db, ILogger<SuppliersController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// List/search/filter suppliers by name, status, with pagination.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResultDto<SupplierDto>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.Suppliers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<SupplierStatus>(status, true, out var supplierStatus))
        {
            query = query.Where(s => s.Status == supplierStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var cleanSearch = search.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(cleanSearch) ||
                                     (s.ContactPerson != null && s.ContactPerson.ToLower().Contains(cleanSearch)) ||
                                     (s.Email != null && s.Email.ToLower().Contains(cleanSearch)));
        }

        var total = await query.CountAsync();

        var suppliers = await query
            .OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SupplierDto(
                s.Id,
                s.Name,
                s.ContactPerson,
                s.Email,
                s.Phone,
                s.Address,
                s.Status.ToString(),
                s.CreatedAt,
                s.UpdatedAt
            ))
            .ToListAsync();

        return Ok(new PagedResultDto<SupplierDto>(suppliers, total, page, pageSize));
    }

    /// <summary>
    /// Get supplier detail including full quotation history summary (§2 / §7).
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<SupplierDetailDto>> GetById(int id)
    {
        var supplier = await _db.Suppliers
            .Include(s => s.Quotations)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (supplier is null)
            return NotFound($"Supplier #{id} not found.");

        var history = supplier.Quotations
            .OrderByDescending(q => q.QuotationDate)
            .Select(q => new SupplierQuotationHistoryItemDto(
                q.Id,
                q.MaterialRequestId,
                q.QuotationDate,
                q.ValidUntil,
                q.Status.ToString(),
                q.TotalAmount
            ))
            .ToList();

        var detail = new SupplierDetailDto(
            supplier.Id,
            supplier.Name,
            supplier.ContactPerson,
            supplier.Email,
            supplier.Phone,
            supplier.Address,
            supplier.Status.ToString(),
            supplier.CreatedAt,
            supplier.UpdatedAt,
            history
        );

        return Ok(detail);
    }

    /// <summary>
    /// Create a new supplier (defaults to Active).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create(CreateSupplierDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Supplier name is required.");

        var supplier = new Supplier
        {
            Name = dto.Name.Trim(),
            ContactPerson = dto.ContactPerson?.Trim(),
            Email = dto.Email?.Trim(),
            Phone = dto.Phone?.Trim(),
            Address = dto.Address?.Trim(),
            Status = SupplierStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Supplier created: #{Id} {Name}", supplier.Id, supplier.Name);

        var result = new SupplierDto(
            supplier.Id,
            supplier.Name,
            supplier.ContactPerson,
            supplier.Email,
            supplier.Phone,
            supplier.Address,
            supplier.Status.ToString(),
            supplier.CreatedAt,
            supplier.UpdatedAt
        );

        return CreatedAtAction(nameof(GetById), new { id = supplier.Id }, result);
    }

    /// <summary>
    /// Edit supplier profile details.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<SupplierDto>> Update(int id, UpdateSupplierDto dto)
    {
        var supplier = await _db.Suppliers.FindAsync(id);
        if (supplier is null)
            return NotFound($"Supplier #{id} not found.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Supplier name cannot be empty.");

        supplier.Name = dto.Name.Trim();
        supplier.ContactPerson = dto.ContactPerson?.Trim();
        supplier.Email = dto.Email?.Trim();
        supplier.Phone = dto.Phone?.Trim();
        supplier.Address = dto.Address?.Trim();
        supplier.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(new SupplierDto(
            supplier.Id,
            supplier.Name,
            supplier.ContactPerson,
            supplier.Email,
            supplier.Phone,
            supplier.Address,
            supplier.Status.ToString(),
            supplier.CreatedAt,
            supplier.UpdatedAt
        ));
    }

    /// <summary>
    /// Change supplier status (Active, Inactive, Suspended).
    /// Status changes are audited and affect procurement AI eligibility (§2 / §5).
    /// </summary>
    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateSupplierStatusDto dto)
    {
        var supplier = await _db.Suppliers.FindAsync(id);
        if (supplier is null)
            return NotFound($"Supplier #{id} not found.");

        if (!Enum.TryParse<SupplierStatus>(dto.Status, true, out var newStatus))
            return BadRequest($"Invalid status '{dto.Status}'. Allowed values: Active, Inactive, Suspended.");

        var oldStatus = supplier.Status;
        supplier.Status = newStatus;
        supplier.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Supplier #{Id} status changed from {OldStatus} to {NewStatus}", id, oldStatus, newStatus);

        return NoContent();
    }
}
