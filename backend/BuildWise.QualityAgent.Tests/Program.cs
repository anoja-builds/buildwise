using System.Text.Json;
using System.Text.Json.Nodes;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Services;

var evidenceJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "evidence.json"));
var recommendationJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "recommendation.json"));
var evidence = JsonSerializer.Deserialize<QualityRiskEvidence>(evidenceJson, QualityAgentJson.Options)!;
var validator = new QualityRiskRecommendationValidator();
var passed = 0;
void Check(string name, Action action, bool rejects = false)
{
    try { action(); if (rejects) throw new Exception($"Expected rejection: {name}"); }
    catch (Exception ex) when (rejects && (ex is QualityRiskException || ex is JsonException)) { }
    passed++;
    Console.WriteLine($"PASS {name}");
}
QualityRiskRecommendation Parse(JsonNode node) => node.Deserialize<QualityRiskRecommendation>(QualityAgentJson.Options)!;
void Reject(string name, Action<JsonNode> mutate)
{
    var node = JsonNode.Parse(recommendationJson)!;
    mutate(node);
    Check(name, () => validator.Validate(Parse(node), evidence), true);
}
Check("valid rejected-item recommendation", () => validator.Validate(Parse(JsonNode.Parse(recommendationJson)!), evidence));
Reject("inspection identity", n => n["inspectionId"] = 99);
Reject("foreign item", n => n["itemRecommendations"]![0]!["inspectionItemId"] = 99);
var duplicateEvidence = evidence with
{
    Items = [evidence.Items[0], evidence.Items[0] with { InspectionItemId = 11, DeliveryItemId = 21 }],
    EvidenceReferences = [.. evidence.EvidenceReferences, "inspection-item:11"]
};
var duplicateRecommendation = JsonNode.Parse(recommendationJson)!;
duplicateRecommendation["itemRecommendations"]!.AsArray().Add(duplicateRecommendation["itemRecommendations"]![0]!.DeepClone());
Check("duplicate items specifically reach duplicate-ID validation", () =>
{
    try
    {
        validator.Validate(Parse(duplicateRecommendation), duplicateEvidence);
        throw new Exception("Expected duplicate-ID rejection.");
    }
    catch (QualityRiskException ex) when (ex.Message == "Duplicate inspection item.") { }
});
Reject("invalid evidence reference", n => n["riskFlags"]![0]!["evidenceReferences"]![0] = "ncr:999");
Reject("invalid severity", n => n["itemRecommendations"]![0]!["suggestedSeverity"] = "Unknown");
Reject("missing corrective action", n => n["itemRecommendations"]![0]!["suggestedCorrectiveAction"] = null);
Reject("inconsistent overall flag", n => n["ncrRecommended"] = false);
Reject("attempted decision change", n => n["overallDecision"] = "Accepted");
Reject("attempted NCR creation", n => n["createNcr"] = true);
Reject("oversized text", n => n["rationaleSummary"] = new string('x', 2001));
Reject("null flag", n => n["riskFlags"]![0] = null);
Reject("missing required property", n => n.AsObject().Remove("ncrRecommended"));
var noRejected = evidence with { Items = evidence.Items.Select(i => i with { RejectedQuantity = 0, AcceptedQuantity = 10 }).ToList() };
Check("NCR prohibited without rejected quantity", () => validator.Validate(Parse(JsonNode.Parse(recommendationJson)!), noRejected), true);
var noNcr = JsonNode.Parse(recommendationJson)!;
noNcr["ncrRecommended"] = false;
noNcr["itemRecommendations"] = new JsonArray();
Check("advisory risk without NCR is allowed", () => validator.Validate(Parse(noNcr), noRejected));
var envelope = new QualityAgentResult
{
    Success = true, Recommendation = Parse(JsonNode.Parse(recommendationJson)!),
    IterationCount = 4, ModelIdentifier = "offline-test-model", ErrorCode = null,
    Trace = [
        new() { Iteration = 1, Action = "get_current_inspection_evidence", Arguments = new(), Success = true, DurationMs = 1 },
        new() { Iteration = 2, Action = "get_supplier_quality_history", Arguments = new() { ["limit"] = 10 }, Success = true, DurationMs = 1 },
        new() { Iteration = 3, Action = "get_prior_non_conformance_summary", Arguments = new(), Success = true, DurationMs = 1 },
        new() { Iteration = 4, Action = "final_output", Arguments = new(), Success = true, DurationMs = 1 }
    ]
};
Check("valid bounded tool trace", () => validator.ValidateEnvelope(envelope));
envelope.Trace.RemoveAt(0);
Check("reject success without evidence tool use", () => validator.ValidateEnvelope(envelope), true);
Console.WriteLine($"{passed} offline validator checks passed. No database or Gemini calls.");
