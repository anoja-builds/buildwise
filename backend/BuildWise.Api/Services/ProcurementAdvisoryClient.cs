using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildWise.Api.Services;

public record ProcurementRequirement(string EvidenceRef, int ItemId, string MaterialName, string Unit, decimal Quantity, string? Notes);
public record ProcurementComparison(string EvidenceRef, int QuotationId, int SupplierId, decimal TotalAmount,
    decimal CalculatedTotal, bool Eligible, int? Rank, bool ValidationPassed, List<string> ValidationErrors,
    Dictionary<string, decimal> OfferedQuantities);
public record ProcurementSupplierEvidence(string EvidenceRef, int SupplierId, string Name, string Status,
    int PreviousOrderCount, int CompletedOrderCount, int CancelledOrderCount);
public record ProcurementAgentEvidence(int RequestId, DateOnly RequiredDate, string? Reason, DateTime CollectedAt,
    int SelectedQuotationId, List<ProcurementRequirement> Requirements, List<ProcurementComparison> Quotations,
    List<ProcurementSupplierEvidence> Suppliers, List<string> EvidenceRefs);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record ProcurementAdvisory(
    [property: JsonRequired] string Summary,
    [property: JsonRequired] string RiskLevel,
    [property: JsonRequired] List<string> Risks,
    [property: JsonRequired] List<string> ClarificationQuestions,
    [property: JsonRequired] List<string> RecommendedFollowUps,
    [property: JsonRequired] List<string> EvidenceRefs);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record ProcurementAgentTrace(
    [property: JsonRequired] int Iteration,
    [property: JsonRequired] string Action,
    [property: JsonRequired] Dictionary<string, int> Arguments,
    [property: JsonRequired] bool Success);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record ProcurementAgentResponse(
    [property: JsonRequired] bool Success,
    [property: JsonRequired] ProcurementAdvisory? Recommendation,
    [property: JsonRequired] List<ProcurementAgentTrace> Trace,
    [property: JsonRequired] int IterationCount,
    [property: JsonRequired] string ModelIdentifier,
    [property: JsonRequired] string? ErrorCode);

public interface IProcurementAdvisoryClient
{
    Task<ProcurementAgentResponse> AnalyseAsync(ProcurementAgentEvidence evidence, CancellationToken ct = default);
}

public class ProcurementAdvisoryClient(HttpClient http, IConfiguration configuration) : IProcurementAdvisoryClient
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict
    };

    public async Task<ProcurementAgentResponse> AnalyseAsync(ProcurementAgentEvidence evidence, CancellationToken ct = default)
    {
        var key = (configuration["AgentService:ApiKey"] ?? Environment.GetEnvironmentVariable("QUALITY_AGENT_SERVICE_KEY"))?.Trim();
        if (!Uri.TryCreate(configuration["AgentService:BaseUrl"], UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https") || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Internal procurement agent is not configured.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));
        using var request = new HttpRequestMessage(HttpMethod.Post, uri.AbsoluteUri.TrimEnd('/') + "/procurement/analyse")
        {
            Content = JsonContent.Create(evidence, options: JsonOptions)
        };
        request.Headers.Add("X-Quality-Agent-Key", key);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + read > 128_000) throw new JsonException("Oversized procurement agent response.");
            buffer.Write(chunk, 0, read);
        }
        return JsonSerializer.Deserialize<ProcurementAgentResponse>(buffer.ToArray(), JsonOptions)
            ?? throw new JsonException("Empty procurement agent response.");
    }
}

public static class ProcurementAdvisoryValidator
{
    public static readonly string[] Tools = ["get_material_request_requirements", "get_validated_quotation_comparison", "get_supplier_procurement_evidence"];
    private static readonly string[] Errors = ["missing_api_key", "timeout", "iteration_limit", "invalid_tool_call",
        "insufficient_tool_use", "invalid_output", "provider_or_agent_failure"];

    public static void Validate(ProcurementAgentResponse result, ProcurementAgentEvidence evidence)
    {
        Require(result != null && result.IterationCount is >= 0 and <= 6, "Invalid execution bounds.");
        Text(result!.ModelIdentifier, 100);
        Require(result.Trace != null && result.Trace.Count <= 7, "Invalid execution trace.");
        var previous = 0;
        foreach (var t in result.Trace!)
        {
            Require(t != null && t.Iteration >= 1 && t.Iteration >= previous && t.Iteration <= result.IterationCount, "Invalid iteration.");
            previous = t!.Iteration;
            Require(Tools.Contains(t.Action) || t.Action == "final_output", "Invalid tool name.");
            Require(t.Arguments != null && t.Arguments.Count == 0, "Tools cannot accept scope overrides.");
        }
        Require(result.Trace.Count(t => Tools.Contains(t.Action)) <= 6, "Too many tool calls.");
        if (!result.Success)
        {
            Require(result.Recommendation == null && Errors.Contains(result.ErrorCode), "Invalid failure envelope.");
            return;
        }
        Require(result.ErrorCode == null && result.Recommendation != null && result.IterationCount > 0, "Invalid success envelope.");
        Require(result.Trace.All(t => t.Success) && Tools.All(t => result.Trace.Any(e => e.Action == t))
            && result.Trace.Count(t => t.Action == "final_output") == 1 && result.Trace.Last().Action == "final_output",
            "Missing successful evidence collection.");
        var r = result.Recommendation!;
        Text(r.Summary, 2000);
        Require(new[] { "Low", "Medium", "High", "Critical" }.Contains(r.RiskLevel), "Invalid risk level.");
        foreach (var list in new[] { r.Risks, r.ClarificationQuestions, r.RecommendedFollowUps })
        {
            Require(list != null && list.Count <= 10, "Invalid advisory list.");
            foreach (var value in list!) Text(value, 500);
        }
        Require(r.EvidenceRefs != null && r.EvidenceRefs.Count is >= 2 and <= 50
            && r.EvidenceRefs.All(evidence.EvidenceRefs.Contains)
            && r.EvidenceRefs.Contains($"request:{evidence.RequestId}")
            && r.EvidenceRefs.Contains($"quotation:{evidence.SelectedQuotationId}"), "Invalid evidence references.");
    }

    private static void Text(string? value, int max) => Require(!string.IsNullOrWhiteSpace(value) && value.Length <= max, "Invalid advisory text.");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new JsonException(message);
    }
}
