using BuildWise.Api.DTOs;

namespace BuildWise.Api.Services;

public record AgentQuotationInput(
    int quotation_id,
    int supplier_id,
    string supplier_name,
    string supplier_status,
    Dictionary<string, decimal> quantity_offered,
    Dictionary<string, decimal> unit_prices,
    decimal total_amount,
    bool valid
);

public record AgentAnalyzePayload(
    int material_request_id,
    List<AgentQuotationInput> quotations,
    Dictionary<string, decimal> requested_quantities
);

public class QuotationAgentClient
{
    // Kept constructor-compatible with existing workflow composition. Ranking no longer
    // crosses a provider boundary; the separate advisory client cannot choose a winner.
    public QuotationAgentClient(HttpClient http, ILogger<QuotationAgentClient> logger) { }

    public Task<AgentRecommendationDto?> AnalyzeAsync(int materialRequestId,
        List<AgentQuotationInput> quotations, Dictionary<string, decimal> requestedQuantities,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = ExecuteFallbackAnalysis(new(materialRequestId, quotations, requestedQuantities));
        return Task.FromResult<AgentRecommendationDto?>(result);
    }

    public AgentRecommendationDto ExecuteFallbackAnalysis(AgentAnalyzePayload payload)
    {
        var warnings = new List<string> { "Agent service unavailable: C# deterministic fallback used; no external AI provider call." };
        var eligible = new List<(AgentQuotationInput Quotation, bool CoversAll)>();

        foreach (var q in payload.quotations)
        {
            if (q.supplier_status != "Active")
            {
                warnings.Add($"Supplier '{q.supplier_name}' excluded: status is {q.supplier_status} (not Active).");
                continue;
            }

            if (!q.valid)
            {
                warnings.Add($"Quotation #{q.quotation_id} from '{q.supplier_name}' excluded: quotation has expired.");
                continue;
            }

            var coversAll = true;
            foreach (var (itemId, reqQty) in payload.requested_quantities)
            {
                var offered = q.quantity_offered.GetValueOrDefault(itemId, 0);
                if (offered < reqQty)
                {
                    coversAll = false;
                    warnings.Add($"Quotation #{q.quotation_id} from '{q.supplier_name}' covers only {offered:g}/{reqQty:g} units for request line #{itemId}.");
                }
            }

            if (coversAll) eligible.Add((q, coversAll));
        }

        var ranked = eligible
            .OrderBy(e => !e.CoversAll)
            .ThenBy(e => e.Quotation.total_amount)
            .ToList();

        if (ranked.Count == 0)
        {
            return new AgentRecommendationDto(
                RecommendedQuotationId: null,
                RecommendedSupplierId: null,
                RecommendedSupplierName: null,
                Rationale: "No eligible quotations met the procurement criteria.",
                RankedAlternatives: new List<RankedAlternativeDto>(),
                Warnings: warnings,
                ExecutionMode: "DeterministicFallback",
                ToolsUsed: [],
                ToolTrace: [],
                FallbackReason: "not_configured"
            );
        }

        var fullCoverageCandidates = ranked.Where(r => r.CoversAll).ToList();
        var top = fullCoverageCandidates.Count > 0 ? fullCoverageCandidates[0] : ranked[0];

        var alternatives = ranked.Select((r, index) => new RankedAlternativeDto(
            r.Quotation.quotation_id,
            r.Quotation.supplier_id,
            r.Quotation.supplier_name,
            index + 1,
            r.Quotation.total_amount,
            r.CoversAll ? "Full coverage, lowest price" : "Partial coverage — flagged as incomplete"
        )).ToList();

        var rationale = top.CoversAll
            ? $"Selected '{top.Quotation.supplier_name}' (Quotation #{top.Quotation.quotation_id}) as the lowest-cost compliant supplier ({top.Quotation.total_amount:N2}) with 100% quantity fulfillment and Active standing."
            : $"Flagged '{top.Quotation.supplier_name}' as best available ({top.Quotation.total_amount:N2}), but NOTE: does not fully cover requested quantities.";

        return new AgentRecommendationDto(
            RecommendedQuotationId: top.Quotation.quotation_id,
            RecommendedSupplierId: top.Quotation.supplier_id,
            RecommendedSupplierName: top.Quotation.supplier_name,
            Rationale: rationale,
            RankedAlternatives: alternatives,
            Warnings: warnings,
                ExecutionMode: "DeterministicFallback",
                ToolsUsed: [],
                ToolTrace: [],
                FallbackReason: "not_configured"
        );
    }
}
