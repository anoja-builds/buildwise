using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Roles = "ProcurementOfficer,ProcurementManager,SiteManager,Administrator")]
public class QuotationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<QuotationsController> _logger;

    public QuotationsController(ApplicationDbContext db, ILogger<QuotationsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get all quotations recorded against an approved material request.
    /// </summary>
    [HttpGet("material-requests/{requestId:int}/quotations")]
    [Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator,SiteManager")]
    public async Task<ActionResult<IEnumerable<QuotationDto>>> GetQuotationsForRequest(int requestId)
    {
        var quotations = await _db.Quotations
            .Where(q => q.MaterialRequestId == requestId)
            .Include(q => q.Supplier)
            .Include(q => q.Items)
            .ThenInclude(qi => qi.MaterialRequestItem)
            .ThenInclude(mri => mri.Material)
            .OrderBy(q => q.TotalAmount)
            .ToListAsync();

        var dtos = quotations.Select(MapToQuotationDto).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// Get single quotation by id with line items.
    /// </summary>
    [HttpGet("quotations/{id:int}")]
    [Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator,SiteManager")]
    public async Task<ActionResult<QuotationDto>> GetById(int id)
    {
        var quotation = await _db.Quotations
            .Include(q => q.Supplier)
            .Include(q => q.Items)
            .ThenInclude(qi => qi.MaterialRequestItem)
            .ThenInclude(mri => mri.Material)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (quotation is null)
            return NotFound($"Quotation #{id} not found.");

        return Ok(MapToQuotationDto(quotation));
    }

    /// <summary>
    /// Record a quotation for an approved material request (§3.1 / §5.1 / §5.6).
    /// Enforces: request must be Approved, total is auto-calculated by API from quantity * unit_price.
    /// </summary>
    [HttpPost("material-requests/{requestId:int}/quotations")]
    [Authorize(Roles = "ProcurementOfficer,Administrator")]
    public async Task<ActionResult<QuotationDto>> Create(int requestId, CreateQuotationDto dto)
    {
        var request = await _db.MaterialRequests
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request is null)
            return NotFound($"Material request #{requestId} not found.");

        if (request.Status != MaterialRequestStatus.Approved)
            return BadRequest($"Cannot record quotations: Material Request #{requestId} is '{request.Status}', but must be 'Approved'.");

        if (dto.ValidUntil < DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest($"Quotation validity date '{dto.ValidUntil:yyyy-MM-dd}' has expired.");

        if (dto.ValidUntil < dto.QuotationDate)
            return BadRequest("Quotation ValidUntil cannot be earlier than QuotationDate.");

        if (dto.RfqId.HasValue)
        {
            var rfq = await _db.Rfqs.FirstOrDefaultAsync(r => r.Id == dto.RfqId.Value);
            if (rfq is null || rfq.MaterialRequestId != requestId)
                return BadRequest("RFQ is missing or belongs to a different material request.");
            if (rfq.Status != RfqStatus.Issued)
                return BadRequest("Quotations can only be recorded against an Issued RFQ.");
        }

        var supplier = await _db.Suppliers.FindAsync(dto.SupplierId);
        if (supplier is null)
            return NotFound($"Supplier #{dto.SupplierId} not found.");

        if (dto.Items == null || dto.Items.Count == 0)
            return BadRequest("At least one quotation item is required.");

        var validItemIds = request.Items.Select(i => i.Id).ToHashSet();
        foreach (var item in dto.Items)
        {
            if (!validItemIds.Contains(item.MaterialRequestItemId))
                return BadRequest($"Request line item #{item.MaterialRequestItemId} does not belong to request #{requestId}.");
            if (item.Quantity <= 0)
                return BadRequest($"Quantity for item #{item.MaterialRequestItemId} must be greater than zero.");
            if (item.UnitPrice < 0)
                return BadRequest($"Unit price for item #{item.MaterialRequestItemId} cannot be negative.");
        }

        // Calculate total deterministically on the server (§3.1 #4)
        var totalAmount = dto.Items.Sum(i => i.Quantity * i.UnitPrice);

        var quotation = new Quotation
        {
            MaterialRequestId = requestId,
            RfqId = dto.RfqId,
            SupplierId = dto.SupplierId,
            QuotationDate = dto.QuotationDate,
            ValidUntil = dto.ValidUntil,
            PromisedDeliveryDate = dto.PromisedDeliveryDate,
            Status = QuotationStatus.Submitted,
            TotalAmount = totalAmount,
            TransportCharge = dto.TransportCharge,
            PaymentTerms = dto.PaymentTerms?.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Items = dto.Items.Select(i => new QuotationItem
            {
                MaterialRequestItemId = i.MaterialRequestItemId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        _db.Quotations.Add(quotation);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Quotation #{QuotationId} recorded for request #{RequestId} from supplier #{SupplierId}. Total: {TotalAmount}",
            quotation.Id, requestId, supplier.Id, totalAmount);

        // Reload with navigations
        return await GetById(quotation.Id);
    }

    /// <summary>
    /// Delete a quotation (only while in Submitted or UnderReview status).
    /// </summary>
    [HttpDelete("quotations/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var quotation = await _db.Quotations.FindAsync(id);
        if (quotation is null)
            return NotFound($"Quotation #{id} not found.");

        if (quotation.Status != QuotationStatus.Submitted && quotation.Status != QuotationStatus.UnderReview)
            return BadRequest($"Cannot delete quotation #{id} in state '{quotation.Status}'. Only Submitted or UnderReview quotations can be removed.");

        _db.Quotations.Remove(quotation);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Structured side-by-side comparison payload (§3.2 / §7).
    /// One row per requested item, columns per supplier quotation with coverage and price indicators.
    /// </summary>
    [HttpGet("material-requests/{requestId:int}/quotations/compare")]
    [Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator,SiteManager")]
    public async Task<ActionResult<QuotationComparisonResponseDto>> Compare(int requestId)
    {
        var request = await _db.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items)
            .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request is null)
            return NotFound($"Material request #{requestId} not found.");

        var quotations = await _db.Quotations
            .Where(q => q.MaterialRequestId == requestId && q.Status != QuotationStatus.Rejected)
            .Include(q => q.Supplier)
            .Include(q => q.Items)
            .ToListAsync();

        var rows = request.Items.Select(item =>
        {
            var offers = quotations.SelectMany(q => q.Items
                .Where(qi => qi.MaterialRequestItemId == item.Id)
                .Select(qi => new QuotationOfferDto(
                    q.Id,
                    q.SupplierId,
                    q.Supplier?.Name ?? $"Supplier #{q.SupplierId}",
                    q.Supplier?.Status.ToString() ?? "Unknown",
                    qi.Quantity,
                    qi.UnitPrice,
                    qi.Quantity * qi.UnitPrice,
                    qi.Quantity >= item.RequestedQuantity
                ))).ToList();

            return new QuotationComparisonRowDto(
                item.Id,
                item.Material?.Name ?? $"Material #{item.MaterialId}",
                item.Material?.Unit ?? "Units",
                item.RequestedQuantity,
                offers
            );
        }).ToList();

        var quotationDtos = quotations.Select(MapToQuotationDto).ToList();

        var response = new QuotationComparisonResponseDto(
            request.Id,
            request.Project?.Name ?? $"Project #{request.ProjectId}",
            rows,
            quotationDtos
        );

        return Ok(response);
    }

    private static QuotationDto MapToQuotationDto(Quotation q)
    {
        return new QuotationDto(
            q.Id,
            q.MaterialRequestId,
            q.SupplierId,
            q.Supplier?.Name ?? $"Supplier #{q.SupplierId}",
            q.Supplier?.Status.ToString() ?? "Unknown",
            q.QuotationDate,
            q.ValidUntil,
            q.PromisedDeliveryDate,
            q.Status.ToString(),
            q.TotalAmount,
            q.CreatedAt,
            q.Items.Select(i => new QuotationItemDto(
                i.Id,
                i.MaterialRequestItemId,
                i.MaterialRequestItem?.Material?.Name ?? $"Item #{i.MaterialRequestItemId}",
                i.MaterialRequestItem?.Material?.Unit ?? "Units",
                i.Quantity,
                i.UnitPrice,
                i.Quantity * i.UnitPrice
            )).ToList()
        );
    }
}
