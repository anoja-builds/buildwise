using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class ProcurementAdvisoryService(ApplicationDbContext db, IProcurementAdvisoryClient client,
    ProcurementValidationService validation)
{
    public async Task<AgentRecommendationDto> EnrichAsync(MaterialRequest request, List<Quotation> quotations,
        AgentRecommendationDto selection)
    {
        // Keep the complete deterministic result. Advisory failure never changes selection/ranking/rationale.
        var fallback = selection with { ExecutionMode = "DeterministicFallback", ToolsUsed = [], ToolTrace = [],
            ModelIdentifier = null, IterationCount = 0, Advisory = null, FallbackReason = "evidence_limit" };
        if (request.Items.Count is 0 or > 100 || quotations.Count is 0 or > 50
            || quotations.Any(q => q.Items.Count > 100) || selection.RecommendedQuotationId == null)
            return fallback;
        var now = DateTime.UtcNow;
        var requirements = request.Items.Select(i => new ProcurementRequirement($"request-item:{i.Id}", i.Id,
            Clip(i.Material.Name, 200)!, Clip(i.Material.Unit, 50)!, i.RequestedQuantity, Clip(i.Notes, 1000))).ToList();
        var comparison = new List<ProcurementComparison>();
        foreach (var q in quotations)
        {
            var check = await validation.ValidateRecommendationAsync(q.Id, request.Id);
            var ranked = selection.RankedAlternatives.SingleOrDefault(r => r.QuotationId == q.Id);
            comparison.Add(new($"quotation:{q.Id}", q.Id, q.SupplierId, q.TotalAmount,
                decimal.Round(q.Items.Sum(i => i.Quantity * i.UnitPrice), 2, MidpointRounding.AwayFromZero),
                ranked != null, ranked?.Rank, check.IsValid, check.Errors.Take(20).Select(e => Clip(e, 500)!).ToList(),
                q.Items.GroupBy(i => i.MaterialRequestItemId).ToDictionary(g => g.Key.ToString(), g => g.Sum(i => i.Quantity))));
        }
        var suppliers = new List<ProcurementSupplierEvidence>();
        foreach (var q in quotations.DistinctBy(q => q.SupplierId))
        {
            // Recorded order counts, not fabricated ratings or inferred quality performance.
            var counts = await db.PurchaseOrders.AsNoTracking().Where(p => p.CreatedAt < now
                && (p.QuotationId != null ? p.Quotation!.SupplierId : p.SupplierId) == q.SupplierId)
                .GroupBy(p => p.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
            suppliers.Add(new($"supplier:{q.SupplierId}", q.SupplierId, Clip(q.Supplier?.Name ?? "Unknown", 200)!,
                q.Supplier?.Status.ToString() ?? "Unknown", counts.Sum(c => c.Count),
                counts.Where(c => c.Status == PurchaseOrderStatus.Completed).Sum(c => c.Count),
                counts.Where(c => c.Status == PurchaseOrderStatus.Cancelled).Sum(c => c.Count)));
        }
        var evidence = new ProcurementAgentEvidence(request.Id, request.RequiredDate, Clip(request.Reason, 1000), now,
            selection.RecommendedQuotationId.Value, requirements, comparison, suppliers,
            new[] { $"request:{request.Id}" }.Concat(requirements.Select(r => r.EvidenceRef))
                .Concat(comparison.Select(q => q.EvidenceRef)).Concat(suppliers.Select(s => s.EvidenceRef)).ToList());
        fallback = fallback with { AdvisoryEvidence = evidence };
        try
        {
            var result = await client.AnalyseAsync(evidence);
            ProcurementAdvisoryValidator.Validate(result, evidence);
            if (!result.Success)
                return fallback with { ModelIdentifier = result.ModelIdentifier, IterationCount = result.IterationCount,
                    ToolTrace = result.Trace, ToolsUsed = result.Trace.Where(t => t.Success && ProcurementAdvisoryValidator.Tools.Contains(t.Action))
                        .Select(t => t.Action).Distinct().ToList(), FallbackReason = result.ErrorCode };
            return fallback with
            {
                ExecutionMode = "AgenticAI", ModelIdentifier = result.ModelIdentifier, IterationCount = result.IterationCount,
                ToolTrace = result.Trace, ToolsUsed = ProcurementAdvisoryValidator.Tools.ToList(), FallbackReason = null,
                Advisory = result.Recommendation,
                // Only the existing fallback notice is removed; all business-rule warnings are retained.
                Warnings = selection.Warnings.Where(w => w != "Agent service unavailable: C# deterministic fallback used; no external AI provider call.").ToList()
            };
        }
        catch (Exception ex)
        {
            // Never save provider exception text or rejected model output.
            return fallback with { FallbackReason = ex switch
            {
                OperationCanceledException => "timeout", JsonException => "invalid_output",
                InvalidOperationException => "not_configured", _ => "service_unavailable"
            }};
        }
    }

    private static string? Clip(string? value, int max) => value?.Length > max ? value[..max] : value;
}
