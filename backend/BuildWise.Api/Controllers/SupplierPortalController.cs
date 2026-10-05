using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

/// <summary>
/// External supplier portal. This is the actor for the "Supplier quotation"
/// step of the end-to-end scenario, which previously had no implementation —
/// quotations could only ever be keyed in by procurement staff.
/// <para>
/// <b>Scoping rule:</b> every query filters on the supplier id carried in the
/// caller's signed JWT. No action accepts a supplier id as input, so one
/// supplier can never observe or modify another supplier's RFQs, quotations or
/// purchase orders, and cannot reach any internal procurement data.
/// </para>
/// <para>
/// Suppliers see only the request lines on RFQs addressed to them — never the
/// other invited suppliers, and never internal notes, agent output or approval
/// history.
/// </para>
/// </summary>
[ApiController]
[Route("api/supplier-portal")]
[Authorize(Policy = Policies.SupplierPortalOnly)]
public class SupplierPortalController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SupplierPortalController> _logger;

    public SupplierPortalController(ApplicationDbContext db, ILogger<SupplierPortalController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Returns the supplier record bound to the caller's login.</summary>
    [HttpGet("profile")]
    public async Task<ActionResult<SupplierPortalProfileDto>> GetProfile()
    {
        if (!TryGetSupplierId(out var supplierId))
            return Forbid();

        var supplier = await _db.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == supplierId);

        if (supplier is null)
            return NotFound(new { message = "The supplier linked to this account no longer exists." });

        return Ok(new SupplierPortalProfileDto(
            supplier.Id, supplier.Name, supplier.ContactPerson,
            supplier.Email, supplier.Phone, supplier.Address, supplier.Status.ToString()));
    }

    /// <summary>RFQ invitations addressed to the caller's own supplier.</summary>
    [HttpGet("rfqs")]
    public async Task<ActionResult<IEnumerable<SupplierPortalRfqDto>>> GetRfqs()
    {
        if (!TryGetSupplierId(out var supplierId))
            return Forbid();

        var rows = await _db.RfqSuppliers
            .AsNoTracking()
            .Where(rs => rs.SupplierId == supplierId)
            .OrderByDescending(rs => rs.Rfq!.CreatedAt)
            .Select(rs => new SupplierPortalRfqDto(
                rs.RfqId,
                rs.Rfq!.MaterialRequestId,
                rs.Rfq.MaterialRequest!.Project!.Name,
                rs.Rfq.RequiredResponseDate,
                rs.Rfq.Notes,
                rs.Rfq.Status.ToString(),
                rs.Status.ToString(),
                rs.Rfq.CreatedAt,
                rs.Rfq.MaterialRequest!.Quotations!
                    .Any(q => q.SupplierId == supplierId)))
            .ToListAsync();

        return Ok(rows);
    }

    // MARK: - Submitting quotations

    /// <summary>The caller's own quotations.</summary>
    [HttpGet("quotations")]
    public async Task<ActionResult<IEnumerable<SupplierPortalQuotationDto>>> GetQuotations()
    {
        if (!TryGetSupplierId(out var supplierId))
            return Forbid();

        var rows = await _db.Quotations
            .AsNoTracking()
            .Include(q => q.Items)
                .ThenInclude(i => i.MaterialRequestItem)
                    .ThenInclude(mri => mri.Material)
            .Where(q => q.SupplierId == supplierId)
            .OrderByDescending(q => q.QuotationDate)
            .ToListAsync();

        return Ok(rows.Select(MapQuotation));
    }
    /// <summary>Request lines for one of the caller's own RFQs.</summary>
    [HttpGet("rfqs/{rfqId:int}/items")]
    public async Task<ActionResult> GetRfqItems(int rfqId)
    {
        if (!TryGetSupplierId(out var supplierId))
            return Forbid();

        // The RfqSupplier join row is the authorisation check: a supplier may only
        // read an RFQ that actually invited it.
        var invited = await _db.RfqSuppliers
            .AsNoTracking()
            .AnyAsync(rs => rs.RfqId == rfqId && rs.SupplierId == supplierId);
        if (!invited)
            return NotFound(new { message = $"RFQ #{rfqId} was not issued to your organisation." });

        var requestId = await _db.Rfqs
            .AsNoTracking()
            .Where(r => r.Id == rfqId)
            .Select(r => (int?)r.MaterialRequestId)
            .FirstOrDefaultAsync();

        if (requestId is null)
            return NotFound(new { message = $"RFQ #{rfqId} was not issued to your organisation." });

        var items = await _db.MaterialRequestItems
            .AsNoTracking()
            .Where(i => i.MaterialRequestId == requestId.Value)
            .Select(i => new
            {
                i.Id,
                i.MaterialId,
                MaterialName = i.Material!.Name,
                MaterialUnit = i.Material.Unit,
                i.RequestedQuantity,
                i.Notes
            })
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>
    /// Submits (or replaces) the caller's own quotation against an RFQ it was
    /// invited to. At most one live quotation per supplier per RFQ is retained;
    /// a resubmission supersedes the supplier's own previous draft.
    /// </summary>
    [HttpPost("rfqs/{rfqId:int}/quotations")]
    public async Task<ActionResult<SupplierPortalQuotationDto>> SubmitQuotation(int rfqId, [FromBody] SupplierQuotationSubmissionDto dto)
    {
        if (!TryGetSupplierId(out var supplierId))
            return Forbid();

        var invitation = await _db.RfqSuppliers
            .Include(rs => rs.Rfq)
            .FirstOrDefaultAsync(rs => rs.RfqId == rfqId && rs.SupplierId == supplierId);

        if (invitation?.Rfq is null)
            return NotFound(new { message = $"RFQ #{rfqId} was not issued to your organisation." });

        if (invitation.Rfq.Status != RfqStatus.Issued)
            return BadRequest(new { message = "This RFQ is no longer open for responses." });

        var supplier = await _db.Suppliers.FindAsync(supplierId);
        if (supplier is null)
            return NotFound(new { message = "The supplier linked to this account no longer exists." });
        if (supplier.Status != SupplierStatus.Active)
            return BadRequest(new { message = "Only Active suppliers can submit quotations. Please contact the procurement desk." });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dto.QuotationDate > today)
            return BadRequest(new { message = "Quotation date cannot be in the future." });
        if (dto.ValidUntil < dto.QuotationDate)
            return BadRequest(new { message = "Quotation ValidUntil cannot be earlier than QuotationDate." });
        if (dto.ValidUntil < today)
            return BadRequest(new { message = "Quotation validity date has expired." });
        if (dto.TransportCharge < 0)
            return BadRequest(new { message = "Transport charge cannot be negative." });
        if (dto.Items is null || dto.Items.Count == 0)
            return BadRequest(new { message = "At least one quotation line is required." });

        var request = await _db.MaterialRequests
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == invitation.Rfq.MaterialRequestId);

        if (request is null)
            return NotFound(new { message = "The material request behind this RFQ no longer exists." });

        var validLineIds = request.Items.Select(i => i.Id).ToHashSet();
        foreach (var line in dto.Items)
        {
            if (!validLineIds.Contains(line.MaterialRequestItemId))
                return BadRequest(new { message = $"Request line #{line.MaterialRequestItemId} does not belong to this RFQ." });
            if (line.Quantity <= 0)
                return BadRequest(new { message = $"Quantity for line #{line.MaterialRequestItemId} must be greater than zero." });
            if (line.UnitPrice < 0)
                return BadRequest(new { message = $"Unit price for line #{line.MaterialRequestItemId} cannot be negative." });
        }
        // Resubmission supersedes the supplier's own previous quote for this RFQ.
        var superseded = await _db.Quotations
            .Include(q => q.Items)
            .Where(q => q.RfqId == rfqId && q.SupplierId == supplierId
                && q.Status != QuotationStatus.Selected
                && q.Status != QuotationStatus.Rejected)
            .ToListAsync();

        var quotation = superseded.FirstOrDefault() ?? new Quotation
        {
            MaterialRequestId = invitation.Rfq.MaterialRequestId,
            RfqId = rfqId,
            SupplierId = supplierId,
            Status = QuotationStatus.Submitted,
            Items = new List<QuotationItem>()
        };

        quotation.QuotationDate = dto.QuotationDate;
        quotation.ValidUntil = dto.ValidUntil;
        quotation.PromisedDeliveryDate = dto.PromisedDeliveryDate;
        quotation.PaymentTerms = dto.PaymentTerms?.Trim();
        quotation.TransportCharge = dto.TransportCharge;
        quotation.UpdatedAt = DateTime.UtcNow;

        quotation.Items.Clear();
        foreach (var line in dto.Items)
        {
            quotation.Items.Add(new QuotationItem
            {
                MaterialRequestItemId = line.MaterialRequestItemId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice
            });
        }

        // The API derives the total from quantity * unit price. A supplier can
        // never declare its own total, and the analysis agent reads this value.
        quotation.TotalAmount = quotation.Items.Sum(i => i.Quantity * i.UnitPrice);

        if (superseded.Count > 1)
        {
            _db.Quotations.RemoveRange(superseded.Skip(1));
        }
        if (quotation.Id == 0)
        {
            _db.Quotations.Add(quotation);
        }
        else
        {
            _db.Quotations.Update(quotation);
        }

        await _db.SaveChangesAsync();

        invitation.Status = RfqSupplierStatus.Quoted;
        invitation.RespondedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Supplier #{SupplierId} submitted quotation #{QuotationId} against RFQ #{RfqId} (total {Total})",
            supplierId, quotation.Id, rfqId, quotation.TotalAmount);

        var reloaded = await _db.Quotations
            .AsNoTracking()
            .Include(q => q.Items)
                .ThenInclude(i => i.MaterialRequestItem)
                    .ThenInclude(mri => mri.Material)
            .FirstAsync(q => q.Id == quotation.Id);

        return Ok(MapQuotation(reloaded));
    }

    /// <summary>Purchase orders awarded to the caller's own supplier.</summary>
    [HttpGet("purchase-orders")]
    public async Task<ActionResult<IEnumerable<SupplierPortalPurchaseOrderDto>>> GetPurchaseOrders()
    {
        if (!TryGetSupplierId(out var supplierId))
            return Forbid();

        var orders = await _db.PurchaseOrders
            .AsNoTracking()
            .Include(po => po.Items)
                .ThenInclude(i => i.Material)
            .Include(po => po.Quotation)
            .Where(po => po.SupplierId == supplierId
                || (po.SupplierId == null && po.Quotation!.SupplierId == supplierId))
            .OrderByDescending(po => po.CreatedAt)
            .ToListAsync();

        return Ok(orders.Select(po => new SupplierPortalPurchaseOrderDto(
            po.Id,
            po.Quotation?.MaterialRequestId,
            po.OrderDate,
            po.ExpectedDeliveryDate,
            po.Status.ToString(),
            po.TotalAmount,
            po.Items.Select(i => new SupplierPortalPurchaseOrderItemDto(
                i.Id,
                i.Material?.Name ?? $"Item #{i.MaterialId ?? i.Id}",
                i.Material?.Unit ?? "Units",
                i.OrderedQuantity,
                i.UnitPrice,
                i.OrderedQuantity * i.UnitPrice)).ToList())));
    }

    private bool TryGetSupplierId(out int supplierId)
    {
        var bound = User.GetBoundSupplierId();
        if (bound is not > 0)
        {
            _logger.LogWarning(
                "Supplier account {Email} has no supplier binding; denying portal access.",
                User.Identity?.Name);
            supplierId = 0;
            return false;
        }
        supplierId = bound.Value;
        return true;
    }

    private static SupplierPortalQuotationDto MapQuotation(Quotation q) => new(
        q.Id,
        q.RfqId,
        q.MaterialRequestId,
        q.QuotationDate,
        q.ValidUntil,
        q.PromisedDeliveryDate,
        q.Status.ToString(),
        q.TotalAmount,
        q.PaymentTerms,
        q.TransportCharge,
        q.CreatedAt,
        q.Items.Select(i => new SupplierPortalQuotationItemDto(
            i.Id,
            i.MaterialRequestItemId,
            i.MaterialRequestItem?.Material?.Name ?? $"Line #{i.MaterialRequestItemId}",
            i.MaterialRequestItem?.Material?.Unit ?? "Units",
            i.Quantity,
            i.UnitPrice,
            i.Quantity * i.UnitPrice)).ToList());
}