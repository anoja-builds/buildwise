using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildWise.Api.Services;

public record DeliveryAgentItem(string EvidenceRef, string MaterialName, string Unit,
    decimal Ordered, decimal PriorFulfilled, decimal Received, decimal Damaged,
    decimal OutstandingBefore, decimal OutstandingAfter, decimal PhysicalShortage, bool OverDelivery);
public record DeliveryAgentHistory(string EvidenceRef, int DeliveryId, DateTime ReceivedAt,
    string Status, int ItemCount, int DamagedItemCount);
public record DeliveryAgentEvidence(int DeliveryId, int? SupplierId, string SupplierName,
    DateTime AsOf, List<DeliveryAgentItem> Items, List<DeliveryAgentHistory> History,
    bool HistoryTruncated, List<DeliveryAgentHistory> PreviousDiscrepancies,
    bool DiscrepanciesTruncated, List<string> EvidenceRefs);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record DeliveryAgentAdvisory(
    [property: JsonRequired] string RiskLevel,
    [property: JsonRequired] string Summary,
    [property: JsonRequired] List<string> LikelyCauses,
    [property: JsonRequired] List<string> RecommendedActions,
    [property: JsonRequired] bool SupplierFollowUpRequired,
    [property: JsonRequired] List<string> EvidenceRefs);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record DeliveryAgentTrace(
    [property: JsonRequired] int Iteration,
    [property: JsonRequired] string Action,
    [property: JsonRequired] Dictionary<string, int> Arguments,
    [property: JsonRequired] bool Success);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record DeliveryAgentResponse(
    [property: JsonRequired] bool Success,
    [property: JsonRequired] DeliveryAgentAdvisory? Recommendation,
    [property: JsonRequired] List<DeliveryAgentTrace> Trace,
    [property: JsonRequired] int IterationCount,
    [property: JsonRequired] string ModelIdentifier,
    [property: JsonRequired] string? ErrorCode);

public record DeliveryAgentExecution(string Mode, string? ModelIdentifier, int IterationCount,
    List<DeliveryAgentTrace> Trace, string? FallbackReason);

public interface IDeliveryDiscrepancyAgentClient
{
    Task<DeliveryAgentResponse> AnalyseAsync(DeliveryAgentEvidence evidence, CancellationToken ct = default);
}

public class DeliveryDiscrepancyAgentClient(HttpClient http, IConfiguration configuration)
    : IDeliveryDiscrepancyAgentClient
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict
    };

    public async Task<DeliveryAgentResponse> AnalyseAsync(DeliveryAgentEvidence evidence, CancellationToken ct = default)
    {
        var baseUrl = configuration["AgentService:BaseUrl"];
        var key = (configuration["AgentService:ApiKey"] ?? Environment.GetEnvironmentVariable("QUALITY_AGENT_SERVICE_KEY"))?.Trim();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https") || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Internal delivery agent is not configured.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            uri.AbsoluteUri.TrimEnd('/') + "/delivery-discrepancy/analyse")
        {
            Content = JsonContent.Create(evidence, options: JsonOptions)
        };
        // Shared internal service authentication; clients never receive this key.
        request.Headers.Add("X-Quality-Agent-Key", key);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + read > 128_000) throw new JsonException("Oversized delivery agent response.");
            buffer.Write(chunk, 0, read);
        }
        return JsonSerializer.Deserialize<DeliveryAgentResponse>(buffer.ToArray(), JsonOptions)
            ?? throw new JsonException("Empty delivery agent response.");
    }
}

public static class DeliveryAgentValidator
{
    public static readonly string[] Tools = ["get_current_delivery_evidence", "get_supplier_delivery_history", "get_previous_discrepancy_summary"];
    public static readonly string[] ErrorCodes = ["missing_api_key", "timeout", "iteration_limit", "invalid_tool_call",
        "insufficient_tool_use", "invalid_output", "provider_or_agent_failure"];

    public static void Validate(DeliveryAgentResponse result, DeliveryAgentEvidence evidence)
    {
        Require(result != null && result.IterationCount is >= 0 and <= 6, "Invalid execution bounds.");
        Text(result!.ModelIdentifier, 100);
        Require(result.Trace != null && result.Trace.Count <= 7, "Invalid execution trace.");
        var previousIteration = 0;
        foreach (var entry in result.Trace!)
        {
            Require(entry != null && entry.Iteration >= 1 && entry.Iteration <= result.IterationCount
                && entry.Iteration >= previousIteration, "Invalid trace iteration.");
            previousIteration = entry!.Iteration;
            Require(Tools.Contains(entry.Action) || entry.Action == "final_output", "Invalid tool name.");
            Require(entry.Arguments != null, "Missing tool arguments.");
            if (entry.Action == "get_supplier_delivery_history")
                Require(entry.Arguments!.Count == 1 && entry.Arguments.TryGetValue("limit", out var limit)
                    && limit is >= 1 and <= 20, "Invalid history limit.");
            else Require(entry.Arguments!.Count == 0, "Unexpected tool arguments.");
        }
        Require(result.Trace.Count(t => Tools.Contains(t.Action)) <= 6, "Too many tool calls.");
        if (!result.Success)
        {
            Require(result.Recommendation == null && ErrorCodes.Contains(result.ErrorCode), "Invalid failure envelope.");
            return;
        }
        Require(result.ErrorCode == null && result.Recommendation != null && result.IterationCount > 0,
            "Invalid success envelope.");
        Require(result.Trace.All(t => t.Success) && Tools.All(t => result.Trace.Any(e => e.Action == t))
            && result.Trace.Count(t => t.Action == "final_output") == 1
            && result.Trace.Last().Action == "final_output", "Missing successful evidence collection.");
        var r = result.Recommendation!;
        Require(new[] { "Low", "Medium", "High", "Critical" }.Contains(r.RiskLevel), "Invalid risk level.");
        Text(r.Summary, 2000);
        Require(r.LikelyCauses != null && r.LikelyCauses.Count <= 10, "Invalid causes.");
        Require(r.RecommendedActions != null && r.RecommendedActions.Count is >= 1 and <= 10, "Invalid actions.");
        foreach (var value in r.LikelyCauses!.Concat(r.RecommendedActions!)) Text(value, 500);
        var historyLimit = result.Trace.Where(t => t.Action == "get_supplier_delivery_history")
            .Max(t => t.Arguments["limit"]);
        var observedRefs = new[] { $"delivery:{evidence.DeliveryId}" }.Concat(evidence.Items.Select(i => i.EvidenceRef))
            .Concat(evidence.History.Take(historyLimit).Select(h => h.EvidenceRef))
            .Concat(evidence.PreviousDiscrepancies.Select(h => h.EvidenceRef)).ToHashSet();
        Require(r.EvidenceRefs != null && r.EvidenceRefs.Count is >= 1 and <= 50
            && r.EvidenceRefs.All(e => evidence.EvidenceRefs.Contains(e) && observedRefs.Contains(e))
            && r.EvidenceRefs.Contains($"delivery:{evidence.DeliveryId}"), "Invalid evidence references.");
    }

    private static void Text(string? value, int max) => Require(!string.IsNullOrWhiteSpace(value) && value.Length <= max, "Invalid advisory text.");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new JsonException(message);
    }
}
