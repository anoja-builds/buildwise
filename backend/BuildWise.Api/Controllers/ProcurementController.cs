using BuildWise.Api.Data;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Controllers;

[Authorize(Roles = "ProcurementOfficer,ProcurementManager,Administrator")]
[ApiController]
[Route("api/[controller]")]
public class ProcurementController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly SupplierEvaluationAgentService _supplierEvaluationAgent;
    private readonly ProcurementValidationService _validationService;

    public ProcurementController(
        ApplicationDbContext dbContext,
        SupplierEvaluationAgentService supplierEvaluationAgent,
        ProcurementValidationService validationService)
    {
        _dbContext = dbContext;
        _supplierEvaluationAgent = supplierEvaluationAgent;
        _validationService = validationService;
    }

    [HttpGet("rfqs")]
    public async Task<IActionResult> GetRfqs()
    {
        var rfqs = await _dbContext.Rfqs
            .Include(r => r.MaterialRequest)
                .ThenInclude(mr => mr!.Project)
            .Include(r => r.Suppliers)
                .ThenInclude(rs => rs.Supplier)
            .Include(r => r.Quotations)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var result = rfqs.Select(r => new
        {
            r.Id,
            r.MaterialRequestId,
            ProjectName = r.MaterialRequest?.Project?.Name,
            r.Title,
            r.Notes,
            Status = r.Status.ToString(),
            r.Deadline,
            r.CreatedAt,
            SuppliersCount = r.Suppliers.Count,
            QuotationsCount = r.Quotations.Count
        });

        return Ok(result);
    }

    [HttpPost("rfqs")]
    public async Task<IActionResult> CreateRfq([FromBody] CreateRfqDto dto)
    {
        var materialRequest = await _dbContext.MaterialRequests.FindAsync(dto.MaterialRequestId);
        if (materialRequest == null) return BadRequest("Material Request not found.");

        var rfq = new Rfq
        {
            MaterialRequestId = dto.MaterialRequestId,
            Title = dto.Title,
            Notes = dto.Notes,
            Deadline = dto.Deadline ?? DateTime.UtcNow.AddDays(3),
            Status = RfqStatus.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Rfqs.Add(rfq);
        await _dbContext.SaveChangesAsync();

        foreach (var supplierId in dto.SupplierIds)
        {
            var rfqSupplier = new RfqSupplier
            {
                RfqId = rfq.Id,
                SupplierId = supplierId,
                Status = "Invited",
                SentAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.RfqSuppliers.Add(rfqSupplier);
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new { rfq.Id, Message = "RFQ created and sent to suppliers.", Status = rfq.Status.ToString() });
    }

    [HttpPost("quotations")]
    public async Task<IActionResult> SubmitQuotation([FromBody] CreateQuotationDto dto)
    {
        var supplier = await _dbContext.Suppliers.FindAsync(dto.SupplierId);
        if (supplier == null) return BadRequest("Invalid Supplier ID.");

        var materialRequest = await _dbContext.MaterialRequests
            .Include(mr => mr.Items)
            .FirstOrDefaultAsync(mr => mr.Id == dto.MaterialRequestId);

        if (materialRequest == null) return BadRequest("Invalid Material Request ID.");

        var totalItemsAmount = dto.Items.Sum(i => i.Quantity * i.UnitPrice);
        var totalAmount = totalItemsAmount + dto.TransportCharge + dto.TaxAmount;

        var quotation = new Quotation
        {
            MaterialRequestId = dto.MaterialRequestId,
            SupplierId = dto.SupplierId,
            QuotationDate = DateOnly.FromDateTime(dto.QuotationDate),
            ValidUntil = DateOnly.FromDateTime(dto.ValidityDate),
            Status = QuotationStatus.Submitted,
            TotalAmount = totalAmount,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Quotations.Add(quotation);
        await _dbContext.SaveChangesAsync();

        foreach (var itemDto in dto.Items)
        {
            var mrItem = materialRequest.Items.FirstOrDefault(i => i.MaterialId == itemDto.MaterialId);
            if (mrItem != null)
            {
                var item = new QuotationItem
                {
                    QuotationId = quotation.Id,
                    MaterialRequestItemId = mrItem.Id,
                    Quantity = itemDto.Quantity,
                    UnitPrice = itemDto.UnitPrice
                };
                _dbContext.QuotationItems.Add(item);
            }
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new { quotation.Id, Message = "Supplier quotation recorded successfully." });
    }

    [HttpGet("comparison/{materialRequestId}")]
    public async Task<IActionResult> GetQuotationComparison(int materialRequestId)
    {
        var quotations = await _dbContext.Quotations
            .Include(q => q.Supplier)
            .Include(q => q.Items)
                .ThenInclude(i => i.MaterialRequestItem)
                    .ThenInclude(mri => mri.Material)
            .Where(q => q.MaterialRequestId == materialRequestId)
            .ToListAsync();

        var materialRequest = await _dbContext.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.Items)
                .ThenInclude(i => i.Material)
            .FirstOrDefaultAsync(r => r.Id == materialRequestId);

        if (materialRequest == null) return NotFound("Material Request not found.");

        var recommendations = await _dbContext.ProcurementRecommendations
            .Include(p => p.RecommendedSupplier)
            .Where(p => p.MaterialRequestId == materialRequestId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        var comparison = new
        {
            MaterialRequestId = materialRequestId,
            ProjectName = materialRequest.Project?.Name,
            RequiredDate = materialRequest.RequiredDate,
            RequestedItems = materialRequest.Items.Select(i => new { i.MaterialId, MaterialName = i.Material?.Name, Quantity = i.RequestedQuantity, MaterialUnit = i.Material?.Unit }),
            Quotations = quotations.Select(q => new
            {
                q.Id,
                SupplierId = q.SupplierId,
                SupplierName = q.Supplier?.Name,
                SupplierStatus = q.Supplier?.Status.ToString(),
                TotalUnitPrice = q.TotalAmount,
                TotalLandedCost = q.TotalAmount,
                PromisedDeliveryDate = q.QuotationDate.AddDays(3),
                CanMeetRequiredDate = q.QuotationDate.AddDays(3) <= materialRequest.RequiredDate,
                ValidityDate = q.ValidUntil
            }),
            LatestAIRecommendation = recommendations == null ? null : new
            {
                recommendations.Id,
                RecommendedSupplierId = recommendations.RecommendedSupplierId,
                RecommendedSupplierName = recommendations.RecommendedSupplier?.Name,
                recommendations.Summary,
                recommendations.Justification,
                Status = recommendations.Status.ToString()
            }
        };

        return Ok(comparison);
    }

    [HttpPost("{materialRequestId}/evaluate")]
    public async Task<IActionResult> EvaluateQuotationsWithAI(int materialRequestId)
    {
        try
        {
            var result = await _supplierEvaluationAgent.EvaluateQuotationsAsync(materialRequestId, false);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpGet("recommendations")]
    public async Task<IActionResult> GetRecommendations()
    {
        var recs = await _dbContext.ProcurementRecommendations
            .Include(r => r.MaterialRequest)
                .ThenInclude(mr => mr!.Project)
            .Include(r => r.RecommendedSupplier)
            .Include(r => r.ApprovedByUser)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var result = recs.Select(r => new
        {
            r.Id,
            r.MaterialRequestId,
            ProjectName = r.MaterialRequest?.Project?.Name,
            RecommendedSupplierId = r.RecommendedSupplierId,
            RecommendedSupplierName = r.RecommendedSupplier?.Name,
            r.Summary,
            r.Justification,
            Status = r.Status.ToString(),
            ApprovedBy = r.ApprovedByUser?.FullName,
            r.DecisionComment,
            r.DecisionDate,
            r.GeneratedPurchaseOrderId
        });

        return Ok(result);
    }

    [HttpPost("recommendations/{id}/approve")]
    [Authorize(Roles = "ProcurementManager,Administrator")]
    public async Task<IActionResult> ApproveRecommendation(int id, [FromBody] ProcurementApprovalDto dto)
    {
        if (!User.TryGetUserId(out var actorId)) return Unauthorized();

        var rec = await _dbContext.ProcurementRecommendations
            .Include(r => r.MaterialRequest)
                .ThenInclude(mr => mr!.Items)
            .Include(r => r.RecommendedSupplier)
            .Include(r => r.RecommendedQuotation)
                .ThenInclude(q => q!.Items)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (rec == null) return NotFound("Procurement recommendation not found.");

        if (rec.Status != RecommendationStatus.AwaitingApproval)
        {
            return BadRequest("Recommendation has already been decided.");
        }

        if (dto.Decision is not (RecommendationStatus.Approved or RecommendationStatus.Rejected or RecommendationStatus.RevisionRequested))
            return BadRequest("Invalid approval decision.");

        PurchaseOrder? po = null;
        Delivery? delivery = null;
        if (dto.Decision == RecommendationStatus.Approved)
        {
            if (rec.RecommendedQuotationId is not int quotationId)
                return BadRequest("A valid quotation is required before approval.");
            var validation = await _validationService.ValidateRecommendationAsync(quotationId, rec.MaterialRequestId);
            if (!validation.IsValid) return BadRequest(new { errors = validation.Errors });
            if (rec.RecommendedQuotation!.SupplierId != rec.RecommendedSupplierId)
                return BadRequest("Recommended supplier does not match the quotation.");
            if (await _dbContext.PurchaseOrders.AnyAsync(p => p.Status != PurchaseOrderStatus.Cancelled &&
                (p.Quotation != null && p.Quotation.MaterialRequestId == rec.MaterialRequestId ||
                 _dbContext.ProcurementRecommendations.Any(r => r.MaterialRequestId == rec.MaterialRequestId && r.GeneratedPurchaseOrderId == p.Id))))
                return BadRequest("A purchase order already exists for this material request.");

            po = new PurchaseOrder
            {
                QuotationId = quotationId,
                SupplierId = rec.RecommendedQuotation.SupplierId,
                ProjectId = rec.MaterialRequest!.ProjectId,
                OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
                ExpectedDeliveryDate = rec.MaterialRequest.RequiredDate,
                Status = PurchaseOrderStatus.Created,
                TotalAmount = rec.RecommendedQuotation.TotalAmount,
                Items = rec.RecommendedQuotation.Items.Select(q => new PurchaseOrderItem
                {
                    QuotationItemId = q.Id,
                    MaterialId = rec.MaterialRequest.Items.Single(i => i.Id == q.MaterialRequestItemId).MaterialId,
                    OrderedQuantity = q.Quantity,
                    UnitPrice = q.UnitPrice
                }).ToList()
            };
            delivery = new Delivery
            {
                PurchaseOrder = po,
                DeliveryReference = $"DN-REC-{rec.Id}",
                Status = DeliveryStatus.Scheduled,
                Items = po.Items.Select(i => new DeliveryItem { PurchaseOrderItem = i }).ToList()
            };
            _dbContext.Deliveries.Add(delivery);
            rec.GeneratedPurchaseOrder = po;
            rec.RecommendedQuotation.Status = QuotationStatus.Selected;
        }

        rec.ApprovedByUserId = actorId;
        rec.Status = dto.Decision;
        rec.DecisionComment = dto.Comment;
        rec.DecisionDate = DateTime.UtcNow;
        rec.UpdatedAt = DateTime.UtcNow;
        // One SaveChanges makes the decision, order, items and delivery atomic.
        await _dbContext.SaveChangesAsync();
        return Ok(new { Message = $"Recommendation {dto.Decision}.", Status = dto.Decision.ToString(),
            PurchaseOrderId = po?.Id, DeliveryId = delivery?.Id });
    }

    [HttpGet("purchase-orders")]
    public async Task<IActionResult> GetPurchaseOrders()
    {
        var pos = await _dbContext.PurchaseOrders
            .Include(p => p.Supplier)
            .Include(p => p.Project)
            .Include(p => p.Items)
                .ThenInclude(i => i.QuotationItem)
                    .ThenInclude(qi => qi.MaterialRequestItem)
                        .ThenInclude(mri => mri.Material)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var result = pos.Select(p => new
        {
            p.Id,
            p.SupplierId,
            SupplierName = p.Supplier?.Name,
            p.ProjectId,
            ProjectName = p.Project?.Name,
            p.OrderDate,
            p.ExpectedDeliveryDate,
            Status = p.Status.ToString(),
            p.TotalAmount,
            Items = p.Items.Select(i => new
            {
                i.Id,
                MaterialName = i.QuotationItem?.MaterialRequestItem?.Material?.Name,
                i.OrderedQuantity,
                i.UnitPrice
            })
        });

        return Ok(result);
    }
}
