using System.Net.Http.Json;
using System.Text.Json;
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
    bool valid,
    DateOnly? promised_delivery_date = null,
    decimal transport_charge = 0m,
    string? payment_terms = null,
    SupplierHistoryInput? supplier_history = null
);

public record SupplierHistoryInput(
    int delivery_count = 0,
    int on_time_delivery_count = 0,
    int discrepancy_count = 0,
    int inspection_count = 0,
    decimal rejected_quantity = 0m,
    decimal inspected_quantity = 0m,
    int ncr_count = 0
)
{
    public decimal OnTimeRate => delivery_count == 0 ? 100m : (decimal)on_time_delivery_count / delivery_count * 100m;
    public decimal DiscrepancyRate => delivery_count == 0 ? 0m : (decimal)discrepancy_count / delivery_count * 100m;
    public decimal RejectionRate => inspected_quantity == 0 ? 0m : rejected_quantity / inspected_quantity * 100m;
    public decimal PerformanceScore => Math.Round(Math.Max(0m, Math.Min(100m, OnTimeRate * 0.5m + (100m - DiscrepancyRate) * 0.2m + (100m - RejectionRate) * 0.3m)), 2);
}

public record AgentAnalyzePayload(
    int material_request_id,
    List<AgentQuotationInput> quotations,
    Dictionary<string, decimal> requested_quantities,
    DateOnly? required_date = null
);

public class QuotationAgentClient
{
    private readonly HttpClient _http;
    private readonly ILogger<QuotationAgentClient> _logger;

    public QuotationAgentClient(HttpClient http, ILogger<QuotationAgentClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AgentRecommendationDto?> AnalyzeAsync(
        int materialRequestId,
        List<AgentQuotationInput> quotations,
        Dictionary<string, decimal> requestedQuantities,
        DateOnly? requiredDate = null,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var payload = new AgentAnalyzePayload(materialRequestId, quotations, requestedQuantities, requiredDate);

        try
        {
            var response = await _http.PostAsJsonAsync("/analyze", payload, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                // The Python agent emits snake_case (recommended_quotation_id, ranked_alternatives, ...)
                // while this DTO is PascalCase, so the response must be read with a snake_case naming
                // policy or every member binds to null. Scoped to this read only — the request payload
                // and the REST contract exposed to React remain unchanged.
                var agentJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                };
                var result = await response.Content.ReadFromJsonAsync<AgentRecommendationDto>(agentJsonOptions, cts.Token);
                if (result != null)
                {
                    _logger.LogInformation("Agent analysis received from external microservice for request #{RequestId}", materialRequestId);
                    return result;
                }
            }
            else
            {
                _logger.LogWarning("Agent microservice returned HTTP {StatusCode}. Using resilient fallback analysis.", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent microservice unreachable at {BaseAddress}. Executing resilient in-process agent fallback.", _http.BaseAddress);
        }

        // Resilient in-process rule-based evaluation fallback (mirrors exact logic)
        return ExecuteFallbackAnalysis(payload);
    }

    public AgentRecommendationDto ExecuteFallbackAnalysis(AgentAnalyzePayload payload)
    {
        var warnings = new List<string>();
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

            eligible.Add((q, coversAll));
        }

        var ranked = eligible
            .OrderBy(e => !e.CoversAll)
            .ThenBy(e => e.Quotation.total_amount + e.Quotation.transport_charge)
            .ThenByDescending(e => e.Quotation.supplier_history?.PerformanceScore ?? 0m)
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
                Justification: new List<string>(),
                RiskFlags: new List<string> { "NO_ELIGIBLE_QUOTATION" },
                Ranking: new List<RankedAlternativeDto>()
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

        var justification = new List<string>();
        var riskFlags = new List<string>();
        if (top.CoversAll)
            justification.Add($"Selected '{top.Quotation.supplier_name}' because it fully covers the requested quantity and has the lowest eligible landed cost.");
        else
            justification.Add($"'{top.Quotation.supplier_name}' is the best available quotation but has incomplete quantity coverage.");
        justification.Add($"Landed cost is {top.Quotation.total_amount + top.Quotation.transport_charge:N2} including transport charge.");
        if (top.Quotation.supplier_history is { } history)
        {
            justification.Add($"Supplier history: {history.OnTimeRate:N1}% on-time, {history.DiscrepancyRate:N1}% discrepancy, {history.RejectionRate:N1}% rejection.");
            if (history.DiscrepancyRate > 0) riskFlags.Add($"DELIVERY_DISCREPANCY_HISTORY:{top.Quotation.supplier_name}");
            if (history.RejectionRate > 0 || history.ncr_count > 0) riskFlags.Add($"QUALITY_HISTORY_REVIEW:{top.Quotation.supplier_name}");
        }
        if (top.Quotation.promised_delivery_date.HasValue) justification.Add($"Promised delivery date: {top.Quotation.promised_delivery_date:yyyy-MM-dd}.");
        if (!top.CoversAll) riskFlags.Add($"PARTIAL_QUANTITY:{top.Quotation.supplier_name}");

        return new AgentRecommendationDto(
            RecommendedQuotationId: top.Quotation.quotation_id,
            RecommendedSupplierId: top.Quotation.supplier_id,
            RecommendedSupplierName: top.Quotation.supplier_name,
            Rationale: rationale,
            RankedAlternatives: alternatives,
            Warnings: warnings,
            Justification: justification,
            RiskFlags: riskFlags.Distinct().ToList(),
            Ranking: alternatives
        );
    }
}
