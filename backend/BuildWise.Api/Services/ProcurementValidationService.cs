using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class ProcurementValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class ProcurementValidationService
{
    private readonly ApplicationDbContext _db;

    public ProcurementValidationService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Validates an AI recommendation against deterministic business rules (§5.1 - §5.6).
    /// Hard gate: if this returns invalid, the recommendation cannot proceed to AwaitingApproval.
    /// </summary>
    public async Task<ProcurementValidationResult> ValidateRecommendationAsync(
        int recommendedQuotationId,
        int materialRequestId)
    {
        var result = new ProcurementValidationResult();

        // Rule 1: Material Request exists and status = Approved
        var request = await _db.MaterialRequests
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == materialRequestId);

        if (request is null)
        {
            result.Errors.Add($"Material request #{materialRequestId} does not exist.");
            return result;
        }

        if (request.Status != MaterialRequestStatus.Approved)
        {
            result.Errors.Add($"Material request #{materialRequestId} has status '{request.Status}', but must be 'Approved'.");
        }

        // Rule 2 & 3: Quotation and Supplier existence, Active status, and validity period
        var quotation = await _db.Quotations
            .Include(q => q.Supplier)
            .Include(q => q.Items)
            .FirstOrDefaultAsync(q => q.Id == recommendedQuotationId);

        if (quotation is null)
        {
            result.Errors.Add($"Recommended quotation #{recommendedQuotationId} does not exist.");
            return result;
        }

        if (quotation.MaterialRequestId != materialRequestId)
        {
            result.Errors.Add($"Quotation #{recommendedQuotationId} belongs to request #{quotation.MaterialRequestId}, not #{materialRequestId}.");
        }

        if (quotation.Supplier is null)
        {
            result.Errors.Add($"Supplier for quotation #{recommendedQuotationId} not found.");
        }
        else if (quotation.Supplier.Status != SupplierStatus.Active)
        {
            result.Errors.Add($"Supplier '{quotation.Supplier.Name}' is {quotation.Supplier.Status}. Only Active suppliers are eligible.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (quotation.ValidUntil < today)
        {
            result.Errors.Add($"Quotation #{recommendedQuotationId} expired on {quotation.ValidUntil:yyyy-MM-dd}.");
        }

        // Rule 4: Every quotation_item references a material_request_item that belongs to this request
        var validRequestItemIds = request.Items.Select(i => i.Id).ToHashSet();
        foreach (var qItem in quotation.Items)
        {
            if (!validRequestItemIds.Contains(qItem.MaterialRequestItemId))
            {
                result.Errors.Add($"Quotation item #{qItem.Id} references request item #{qItem.MaterialRequestItemId}, which does not belong to request #{materialRequestId}.");
            }
        }

        // Rule 5: Full quantity coverage per item (>= requested_quantity)
        foreach (var reqItem in request.Items)
        {
            var offeredQuantity = quotation.Items
                .Where(qi => qi.MaterialRequestItemId == reqItem.Id)
                .Sum(qi => qi.Quantity);

            if (offeredQuantity < reqItem.RequestedQuantity)
            {
                result.Errors.Add($"Quotation covers only {offeredQuantity} of {reqItem.RequestedQuantity} units for request item #{reqItem.Id}. Partial coverage cannot be recommended as sole winner.");
            }
        }

        // Rule 6: Recalculate quotation total amount (must match sum of quantity * unit_price)
        var calculatedTotal = quotation.Items.Sum(i => i.Quantity * i.UnitPrice);
        if (Math.Abs(calculatedTotal - quotation.TotalAmount) > 0.01m)
        {
            result.Errors.Add($"Quotation total mismatch: recorded total is {quotation.TotalAmount}, but calculated sum is {calculatedTotal}.");
        }

        return result;
    }

    /// <summary>
    /// Rule 7: Validates structured JSON schema conformance of AI agent output.
    /// </summary>
    public ProcurementValidationResult ValidateRecommendationSchema(AgentRecommendationDto? recommendation)
    {
        var result = new ProcurementValidationResult();

        if (recommendation is null)
        {
            result.Errors.Add("Recommendation payload is null.");
            return result;
        }

        if (recommendation.RecommendedQuotationId == null || recommendation.RecommendedQuotationId <= 0)
        {
            result.Errors.Add("Schema violation: recommended_quotation_id is missing or non-positive.");
        }

        if (recommendation.RecommendedSupplierId == null || recommendation.RecommendedSupplierId <= 0)
        {
            result.Errors.Add("Schema violation: recommended_supplier_id is missing or non-positive.");
        }

        if (string.IsNullOrWhiteSpace(recommendation.Rationale))
        {
            result.Errors.Add("Schema violation: rationale is missing or empty.");
        }

        if (recommendation.RankedAlternatives == null)
        {
            result.Errors.Add("Schema violation: ranked_alternatives array is null.");
        }

        return result;
    }

    /// <summary>
    /// Validates conditions to create a Purchase Order (§5.8 - §5.10).
    /// </summary>
    public async Task<ProcurementValidationResult> ValidatePurchaseOrderCreationAsync(int workflowId)
    {
        var result = new ProcurementValidationResult();

        var workflow = await _db.AgentWorkflows
            .Include(w => w.Approvals)
            .Include(w => w.MaterialRequest)
            .FirstOrDefaultAsync(w => w.Id == workflowId);

        if (workflow is null)
        {
            result.Errors.Add($"Workflow #{workflowId} does not exist.");
            return result;
        }

        // Rule 8: Blocked until an agent_approvals row exists with decision = Approved
        var hasApprovedDecision = workflow.Approvals
            .Any(a => a.Decision == AgentApprovalStatus.Approved);

        if (!hasApprovedDecision)
        {
            result.Errors.Add($"Purchase order creation blocked: workflow #{workflowId} does not have an Approved manager decision.");
        }

        // Rule 1: Request must still be Approved
        if (workflow.MaterialRequest.Status != MaterialRequestStatus.Approved)
        {
            result.Errors.Add($"Material request #{workflow.MaterialRequestId} is not in Approved state.");
        }

        // Rule 10: Idempotency check: Cannot create duplicate non-cancelled PO for the same material request
        var existingPo = await _db.PurchaseOrders
            .Include(po => po.Quotation)
            .FirstOrDefaultAsync(po => po.Quotation.MaterialRequestId == workflow.MaterialRequestId
                                       && po.Status != PurchaseOrderStatus.Cancelled);

        if (existingPo != null)
        {
            result.Errors.Add($"Purchase Order #{existingPo.Id} already exists for material request #{workflow.MaterialRequestId}. Duplicate POs are prohibited.");
        }

        return result;
    }
}
