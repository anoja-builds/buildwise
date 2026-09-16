using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Enums;

namespace BuildWise.Api.Services;

public class QualityRiskRecommendationValidator
{
    private static readonly string[] Levels = ["Low", "Medium", "High", "Critical"];

    public void Validate(QualityRiskRecommendation r, QualityRiskEvidence evidence)
    {
        Require(r.InspectionId == evidence.InspectionId, "Inspection identity mismatch.");
        Require(Levels.Contains(r.RiskLevel), "Invalid risk level.");
        Text(r.EvidenceSummary, 2000);
        Text(r.RationaleSummary, 2000);
        Require(r.RiskFlags != null && r.RiskFlags.Count <= 20, "Invalid risk flags.");
        Require(r.ItemRecommendations != null && r.ItemRecommendations.Count <= evidence.Items.Count, "Invalid item recommendations.");
        var references = evidence.EvidenceReferences.ToHashSet();
        foreach (var flag in r.RiskFlags!)
        {
            Require(flag != null, "Null risk flag.");
            Text(flag!.Flag, 300);
            References(flag.EvidenceReferences, references);
        }
        var ids = new HashSet<int>();
        foreach (var item in r.ItemRecommendations!)
        {
            Require(item != null, "Null item recommendation.");
            Require(ids.Add(item!.InspectionItemId), "Duplicate inspection item.");
            var source = evidence.Items.SingleOrDefault(i => i.InspectionItemId == item.InspectionItemId);
            Require(source != null, "Unrelated inspection item.");
            Text(item.Rationale, 2000);
            References(item.EvidenceReferences, references);
            Require(item.EvidenceReferences.Contains($"inspection-item:{item.InspectionItemId}"), "Item recommendation must cite its inspection item.");
            if (item.NcrRecommended)
            {
                Require(source!.RejectedQuantity > 0, "NCR recommendation requires rejected material.");
                Require(item.SuggestedSeverity != null && Enum.GetNames<NonConformanceSeverity>().Contains(item.SuggestedSeverity), "Invalid NCR severity.");
                Text(item.SuggestedIssueDescription, 2000);
                Text(item.SuggestedCorrectiveAction, 2000);
            }
            else
                Require(item.SuggestedSeverity == null && item.SuggestedIssueDescription == null
                    && item.SuggestedCorrectiveAction == null, "Non-NCR suggestions must be null.");
        }
        Require(r.NcrRecommended == r.ItemRecommendations.Any(i => i.NcrRecommended), "Overall NCR recommendation is inconsistent.");
        Require(!r.NcrRecommended || evidence.Items.Any(i => i.RejectedQuantity > 0), "No rejected quantity supports an NCR.");
    }

    public void ValidateEnvelope(QualityAgentResult result)
    {
        Require(result.IterationCount is >= 0 and <= 6 && result.Trace != null && result.Trace.Count <= 13,
            "Invalid agent execution bounds.");
        Text(result.ModelIdentifier, 100);
        var tools = new[] { "get_current_inspection_evidence", "get_supplier_quality_history", "get_prior_non_conformance_summary" };
        foreach (var trace in result.Trace!)
        {
            Require(trace != null && trace.Iteration >= 1 && trace.Iteration <= result.IterationCount,
                "Invalid trace iteration.");
            Require(double.IsFinite(trace!.DurationMs) && trace.DurationMs >= 0 && trace.DurationMs <= 90000, "Invalid trace duration.");
            Require(trace.Arguments != null, "Invalid tool arguments.");
            Require(tools.Contains(trace.Action) || trace.Action == "final_output", "Invalid trace action.");
            if (trace.Action == "get_supplier_quality_history")
                Require(trace.Arguments!.Count == 1 && trace.Arguments.TryGetValue("limit", out var limit) && limit is >= 1 and <= 20, "Invalid history limit.");
            else Require(trace.Arguments!.Count == 0, "Unexpected tool arguments.");
        }
        Require(result.Trace.Count(t => tools.Contains(t.Action)) <= 6, "Tool-call limit exceeded.");
        if (result.Success)
        {
            Require(result.ErrorCode == null && result.Recommendation != null && result.IterationCount > 0, "Invalid success response.");
            Require(result.Trace.All(t => t.Success), "Failed trace cannot be reported as success.");
            Require(tools.All(tool => result.Trace.Any(t => t.Action == tool)), "Required evidence tools were not executed.");
            Require(result.Trace.LastOrDefault()?.Action == "final_output", "Missing final output trace.");
        }
        else Require(result.Recommendation == null, "Failed execution cannot return a recommendation.");
    }

    private static void Text(string? text, int max) => Require(!string.IsNullOrWhiteSpace(text) && text.Length <= max, "Missing or oversized recommendation text.");
    private static void References(List<string>? refs, HashSet<string> available) => Require(refs != null && refs.Count is >= 1 and <= 20
        && refs.All(r => r != null && available.Contains(r)), "Invalid evidence reference.");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new QualityRiskException(502, message);
    }
}
