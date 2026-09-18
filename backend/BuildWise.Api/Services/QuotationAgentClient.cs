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
    bool valid
);

public record AgentAnalyzePayload(
    int material_request_id,
    List<AgentQuotationInput> quotations,
    Dictionary<string, decimal> requested_quantities
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
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var payload = new AgentAnalyzePayload(materialRequestId, quotations, requestedQuantities);

        try
        {
            var response = await _http.PostAsJsonAsync("/analyze", payload, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AgentRecommendationDto>(cancellationToken: cts.Token);
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
                Warnings: warnings
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
            Warnings: warnings
        );
    }
}
