using System.Net.Http.Json;
using System.Text.Json;

namespace BuildWise.Api.Services;

/// <summary>
/// Typed gateway for the three operational Python agents. Agent output is
/// advisory: callers must independently re-validate all business invariants.
/// </summary>
public class OperationalAgentClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OperationalAgentClient> _logger;

    public OperationalAgentClient(
        IHttpClientFactory httpClientFactory,
        ILogger<OperationalAgentClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private HttpClient AgentClient(string name) => _httpClientFactory.CreateClient(name);

    public async Task<AgentClientResult<RequestAgentResult>> AnalyzeRequestAsync(
        int requestId,
        string projectName,
        string reason,
        int itemCount,
        decimal totalQuantity)
    {
        var payload = new
        {
            request_id = requestId,
            project_name = projectName,
            reason,
            items_count = itemCount,
            total_quantity = totalQuantity
        };

        try
        {
            var result = await AgentClient("RequestAgent").PostAsJsonAsync(
                "api/agent/analyze-request", payload, JsonOptions);
            result.EnsureSuccessStatusCode();
            return new AgentClientResult<RequestAgentResult>(
                await result.Content.ReadFromJsonAsync<RequestAgentResult>(JsonOptions)
                    ?? throw new InvalidOperationException("Request agent returned an empty response."),
                "PythonRequestAgent");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Request agent unavailable for request {RequestId}; using deterministic fallback.", requestId);
            return new AgentClientResult<RequestAgentResult>(
                RequestAgentResult.Fallback(requestId, itemCount, totalQuantity, reason),
                "DeterministicFallback");
        }
    }

    public async Task<AgentClientResult<DeliveryAgentResult>> AnalyzeDiscrepancyAsync(
        decimal orderedQuantity,
        decimal receivedQuantity,
        decimal damagedQuantity)
    {
        var payload = new
        {
            ordered_qty = orderedQuantity,
            received_qty = receivedQuantity,
            damaged_qty = damagedQuantity
        };

        try
        {
            var result = await AgentClient("DeliveryAgent").PostAsJsonAsync(
                "api/agent/analyze-discrepancy", payload, JsonOptions);
            result.EnsureSuccessStatusCode();
            return new AgentClientResult<DeliveryAgentResult>(
                await result.Content.ReadFromJsonAsync<DeliveryAgentResult>(JsonOptions)
                    ?? throw new InvalidOperationException("Delivery agent returned an empty response."),
                "PythonDeliveryAgent");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Delivery agent unavailable; using deterministic discrepancy fallback.");
            return new AgentClientResult<DeliveryAgentResult>(
                DeliveryAgentResult.Deterministic(orderedQuantity, receivedQuantity, damagedQuantity),
                "DeterministicFallback");
        }
    }

    public async Task<AgentClientResult<QualityAgentResult>> AnalyzeQualityRiskAsync(
        int deliveryId,
        IEnumerable<QualityAgentItem> items)
    {
        var itemList = items.ToList();
        var payload = new { delivery_id = deliveryId, items = itemList };

        try
        {
            var result = await AgentClient("QualityAgent").PostAsJsonAsync(
                "api/agent/analyze-quality-risk", payload, JsonOptions);
            result.EnsureSuccessStatusCode();
            return new AgentClientResult<QualityAgentResult>(
                await result.Content.ReadFromJsonAsync<QualityAgentResult>(JsonOptions)
                    ?? throw new InvalidOperationException("Quality agent returned an empty response."),
                "PythonQualityAgent");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Quality agent unavailable for delivery {DeliveryId}; using deterministic fallback.", deliveryId);
            return new AgentClientResult<QualityAgentResult>(
                QualityAgentResult.Deterministic(itemList),
                "DeterministicFallback");
        }
    }
}

public record AgentClientResult<T>(T Output, string ExecutionSource);


public record RequestAgentResult(
    int RequestId,
    List<string> Flags,
    string Status)
{
    public static RequestAgentResult Fallback(int requestId, int itemCount, decimal totalQuantity, string reason)
    {
        var flags = new List<string>();
        if (reason.Contains("urgent", StringComparison.OrdinalIgnoreCase)) flags.Add("HIGH_URGENCY");
        if (itemCount > 5) flags.Add("BULK_ORDER");
        if (totalQuantity >= 200m) flags.Add("LARGE_QUANTITY_ORDER");
        return new RequestAgentResult(requestId, flags, "Analyzed");
    }
}

public record DeliveryAgentResult(
    bool ShortageDetected,
    bool DamageDetected,
    string Summary)
{
    public static DeliveryAgentResult Deterministic(decimal ordered, decimal received, decimal damaged) =>
        new(received < ordered, damaged > 0,
            received < ordered || damaged > 0 ? "Discrepancy flagged." : "Fully verified.");
}

public record QualityAgentItem(
    string MaterialName,
    decimal InspectedQty,
    decimal RejectedQty,
    decimal AcceptedQty,
    string RejectionReason);

public record QualityAgentResult(
    string RiskLevel,
    bool RequiresNcr,
    string SuggestedCorrectiveAction,
    List<string> RiskFlags,
    decimal TotalInspected,
    decimal TotalRejected,
    decimal RejectionRatePct)
{
    public static QualityAgentResult Deterministic(List<QualityAgentItem> items)
    {
        var inspected = items.Sum(i => i.InspectedQty);
        var rejected = items.Sum(i => i.RejectedQty);
        var rate = inspected == 0 ? 0 : Math.Round(rejected / inspected * 100m, 2);
        var level = rejected > 50m ? "High" : rejected > 0m ? "Medium" : "Low";
        var flags = rejected > 0m ? new List<string> { "QUALITY_DEFECT" } : new List<string>();
        return new QualityAgentResult(
            level,
            rejected > 0m,
            rejected > 0m ? "Issue NCR and require supplier corrective action." : "No corrective action required.",
            flags,
            inspected,
            rejected,
            rate);
    }
}
