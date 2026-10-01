using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildWise.Api.Services;

public record PlanningAgentItem(
    string EvidenceRef,
    int ItemId,
    int MaterialId,
    string MaterialName,
    string Unit,
    decimal RequestedQuantity,
    string? Notes
);

public record PlanningAgentHistory(
    string EvidenceRef,
    int RequestId,
    DateOnly RequiredDate,
    string Status,
    int ItemCount
);

public record PlanningAgentEvidence(
    int RequestId,
    int ProjectId,
    string ProjectName,
    string ProjectStatus,
    string? SiteLocation,
    string RequiredDate,
    int DaysUntilRequired,
    string? Reason,
    string RequestStatus,
    DateTime AsOf,
    List<PlanningAgentItem> Items,
    List<PlanningAgentHistory> History,
    bool HistoryTruncated,
    List<string> EvidenceRefs
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record PlanningAgentAdvisory(
    [property: JsonRequired] string RiskLevel,
    [property: JsonRequired] string Summary,
    [property: JsonRequired] List<string> PlanningFlags,
    [property: JsonRequired] List<string> RequiredChecks,
    [property: JsonRequired] string RecommendedApproach,
    [property: JsonRequired] List<string> EvidenceRefs
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record PlanningAgentTrace(
    [property: JsonRequired] int Iteration,
    [property: JsonRequired] string Action,
    [property: JsonRequired] Dictionary<string, int> Arguments,
    [property: JsonRequired] bool Success
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record PlanningAgentResponse(
    [property: JsonRequired] bool Success,
    [property: JsonRequired] PlanningAgentAdvisory? Recommendation,
    [property: JsonRequired] List<PlanningAgentTrace> Trace,
    [property: JsonRequired] int IterationCount,
    [property: JsonRequired] string ModelIdentifier,
    [property: JsonRequired] string? ErrorCode
);

public record PlanningAgentExecution(
    string Mode,
    string? ModelIdentifier,
    int IterationCount,
    List<PlanningAgentTrace> Trace,
    string? FallbackReason
);

public interface IPlanningAgentClient
{
    Task<PlanningAgentResponse> AnalyseAsync(PlanningAgentEvidence evidence, CancellationToken ct = default);
}

public class PlanningAgentClient(HttpClient http, IConfiguration configuration) : IPlanningAgentClient
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict
    };

    public async Task<PlanningAgentResponse> AnalyseAsync(PlanningAgentEvidence evidence, CancellationToken ct = default)
    {
        var baseUrl = configuration["AgentService:BaseUrl"] ?? configuration["AgentService:Url"];
        var key = (configuration["AgentService:ApiKey"] ?? Environment.GetEnvironmentVariable("QUALITY_AGENT_SERVICE_KEY"))?.Trim();

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https")
            || string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Internal planning agent is not configured.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            uri.AbsoluteUri.TrimEnd('/') + "/planning/analyse")
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
            if (buffer.Length + read > 128_000)
                throw new JsonException("Oversized planning agent response.");
            buffer.Write(chunk, 0, read);
        }

        return JsonSerializer.Deserialize<PlanningAgentResponse>(buffer.ToArray(), JsonOptions)
            ?? throw new JsonException("Empty planning agent response.");
    }
}

public static class PlanningAgentValidator
{
    public static readonly string[] Tools =
    [
        "get_current_material_request_evidence",
        "get_project_context",
        "get_recent_material_request_history"
    ];

    public static readonly string[] ErrorCodes =
    [
        "missing_api_key",
        "timeout",
        "iteration_limit",
        "invalid_tool_call",
        "insufficient_tool_use",
        "invalid_output",
        "provider_or_agent_failure"
    ];

    public static void Validate(PlanningAgentResponse result, PlanningAgentEvidence evidence)
    {
        Require(result != null && result.IterationCount is >= 0 and <= 6, "Invalid execution bounds.");
        Text(result!.ModelIdentifier, 100);
        Require(result.Trace != null && result.Trace.Count <= 7, "Invalid execution trace.");

        var previousIteration = 0;
        foreach (var entry in result.Trace!)
        {
            Require(
                entry != null && entry.Iteration >= 1 && entry.Iteration <= result.IterationCount
                && entry.Iteration >= previousIteration,
                "Invalid trace iteration."
            );
            previousIteration = entry!.Iteration;
            Require(Tools.Contains(entry.Action) || entry.Action == "final_output", "Invalid tool name.");
            Require(entry.Arguments != null, "Missing tool arguments.");

            if (entry.Action == "get_recent_material_request_history")
            {
                Require(
                    entry.Arguments!.Count == 1 && entry.Arguments.TryGetValue("limit", out var limit)
                    && limit is >= 1 and <= 20,
                    "Invalid history limit."
                );
            }
            else
            {
                Require(entry.Arguments!.Count == 0, "Unexpected tool arguments.");
            }
        }

        Require(result.Trace.Count(t => Tools.Contains(t.Action)) <= 6, "Too many tool calls.");

        if (!result.Success)
        {
            Require(result.Recommendation == null && ErrorCodes.Contains(result.ErrorCode), "Invalid failure envelope.");
            return;
        }

        Require(
            result.ErrorCode == null && result.Recommendation != null && result.IterationCount > 0,
            "Invalid success envelope."
        );
        Require(
            result.Trace.All(t => t.Success) && Tools.All(t => result.Trace.Any(e => e.Action == t))
            && result.Trace.Count(t => t.Action == "final_output") == 1
            && result.Trace.Last().Action == "final_output",
            "Missing successful evidence collection."
        );

        var r = result.Recommendation!;
        Require(new[] { "Low", "Medium", "High", "Critical" }.Contains(r.RiskLevel), "Invalid risk level.");
        Text(r.Summary, 2000);
        Require(r.PlanningFlags != null && r.PlanningFlags.Count <= 15, "Invalid planning flags count.");
        Require(r.RequiredChecks != null && r.RequiredChecks.Count is >= 1 and <= 15, "Invalid required checks count.");
        Text(r.RecommendedApproach, 500);

        foreach (var value in r.PlanningFlags!.Concat(r.RequiredChecks!))
        {
            Text(value, 500);
        }

        var historyCalls = result.Trace.Where(t => t.Action == "get_recent_material_request_history").ToList();
        var historyLimit = historyCalls.Count > 0 ? historyCalls.Max(t => t.Arguments["limit"]) : 0;

        var observedRefs = new[] { $"request:{evidence.RequestId}", $"project:{evidence.ProjectId}" }
            .Concat(evidence.Items.Select(i => i.EvidenceRef))
            .Concat(evidence.History.Take(historyLimit).Select(h => h.EvidenceRef))
            .ToHashSet();

        Require(
            r.EvidenceRefs != null && r.EvidenceRefs.Count is >= 1 and <= 50
            && r.EvidenceRefs.All(e => evidence.EvidenceRefs.Contains(e) && observedRefs.Contains(e))
            && r.EvidenceRefs.Contains($"request:{evidence.RequestId}"),
            "Invalid evidence references."
        );
    }

    private static void Text(string? value, int max) =>
        Require(!string.IsNullOrWhiteSpace(value) && value.Length <= max, "Invalid advisory text.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new JsonException(message);
    }
}

