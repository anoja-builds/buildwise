using System.Net;
using System.Text;
using System.Text.Json;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BuildWise.Api.Tests;

public class ProcurementPlanningAgentClientTests
{
    private static PlanningAgentEvidence Evidence() => new(
        10,
        1,
        "Test Project",
        "Active",
        "Site Location A",
        "2026-10-15",
        14,
        "Initial foundation",
        "PendingApproval",
        DateTime.UtcNow,
        [new("item:1", 1, 1, "Cement", "bags", 100, "Grade 42.5")],
        [new("request:9", 9, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), "Approved", 2)],
        false,
        ["request:10", "project:1", "item:1", "request:9"]
    );

    private const string ValidJson = """
        {"success":true,"recommendation":{"riskLevel":"Low","summary":"Lead time is sufficient. Proceed with standard competitive RFQ.",
        "planningFlags":["Adequate schedule (>7 days)."],"requiredChecks":["Verify engineer specs for 100 bags of Cement."],
        "recommendedApproach":"Standard Competitive RFQ Process (Min. 2 Quotations)","evidenceRefs":["request:10","project:1","item:1"]},
        "trace":[{"iteration":1,"action":"get_current_material_request_evidence","arguments":{},"success":true},
        {"iteration":2,"action":"get_project_context","arguments":{},"success":true},
        {"iteration":3,"action":"get_recent_material_request_history","arguments":{"limit":5},"success":true},
        {"iteration":4,"action":"final_output","arguments":{},"success":true}],
        "iterationCount":4,"modelIdentifier":"test-gemini-model","errorCode":null}
        """;

    [Fact]
    public async Task ClientUsesAuthenticatedInternalEndpointAndCamelCaseEvidence()
    {
        var handler = new Handler(async request =>
        {
            Assert.Equal("http://internal.test/planning/analyse", request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-internal-key", request.Headers.GetValues("X-Quality-Agent-Key").Single());
            Assert.Equal(HttpMethod.Post, request.Method);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(10, body.RootElement.GetProperty("requestId").GetInt32());
            Assert.Equal(1, body.RootElement.GetProperty("projectId").GetInt32());
            Assert.Equal(100, body.RootElement.GetProperty("items")[0].GetProperty("requestedQuantity").GetInt32());
            return Reply(ValidJson);
        });

        var result = await Client(handler).AnalyseAsync(Evidence());
        PlanningAgentValidator.Validate(result, Evidence());
        Assert.True(result.Success);
        Assert.Equal("Low", result.Recommendation!.RiskLevel);
        Assert.StartsWith("Standard", result.Recommendation.RecommendedApproach);
    }

    [Theory]
    [InlineData("test-internal-key\r\n")]
    [InlineData(" \ttest-internal-key\n")]
    public async Task ClientTrimsSurroundingServiceKeyWhitespaceBeforeAddingHeader(string configuredKey)
    {
        var handler = new Handler(request =>
        {
            Assert.Equal("test-internal-key", request.Headers.GetValues("X-Quality-Agent-Key").Single());
            return Task.FromResult(Reply(ValidJson));
        });

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgentService:BaseUrl"] = "http://internal.test/",
            ["AgentService:ApiKey"] = configuredKey
        }).Build();

        using var http = new HttpClient(handler);
        var result = await new PlanningAgentClient(http, configuration).AnalyseAsync(Evidence());
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("string-int")]
    [InlineData("oversized")]
    [InlineData("malformed")]
    public async Task ClientRejectsInvalidJsonContracts(string variant)
    {
        var json = variant switch
        {
            "extra" => ValidJson.Replace("\"riskLevel\":", "\"unknownProperty\":\"invalid\",\"riskLevel\":"),
            "missing" => ValidJson.Replace("\"recommendedApproach\":\"Standard Competitive RFQ Process (Min. 2 Quotations)\",", ""),
            "string-int" => ValidJson.Replace("\"iterationCount\":4", "\"iterationCount\":\"4\""),
            "oversized" => new string(' ', 128_001),
            _ => "not json"
        };
        await Assert.ThrowsAsync<JsonException>(() => Client(new Handler(_ => Task.FromResult(Reply(json)))).AnalyseAsync(Evidence()));
    }

    [Fact]
    public async Task ClientRejectsHttpFailuresAndMissingConfiguration()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).AnalyseAsync(Evidence()));

        using var http = new HttpClient(new Handler(_ => throw new Exception("No call expected")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PlanningAgentClient(http, new ConfigurationBuilder().Build()).AnalyseAsync(Evidence()));
    }

    [Fact]
    public void BackendRejectsReferencesOutsideObservedHistoryAndInvalidExecutionBounds()
    {
        var valid = JsonSerializer.Deserialize<PlanningAgentResponse>(ValidJson, PlanningAgentClient.JsonOptions)!;
        var evidence = Evidence() with
        {
            History = [new("request:8", 8, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), "Approved", 1)],
            EvidenceRefs = ["request:10", "project:1", "item:1", "request:8"]
        };

        // Model did not call get_recent_material_request_history with limit >= 1 for request:8, but cited it
        Assert.Throws<JsonException>(() => PlanningAgentValidator.Validate(valid with
        {
            Recommendation = valid.Recommendation! with { EvidenceRefs = ["request:10", "request:8"] },
            Trace = valid.Trace.Where(t => t.Action != "get_recent_material_request_history").ToList()
        }, evidence));

        // Iteration bounds > 6
        Assert.Throws<JsonException>(() => PlanningAgentValidator.Validate(valid with { IterationCount = 7 }, evidence));

        // Unauthorized tool name
        Assert.Throws<JsonException>(() => PlanningAgentValidator.Validate(valid with
        {
            Trace = [new(1, "approve_material_request", [], true)]
        }, evidence));
    }

    [Fact]
    public async Task ServiceUsesAgenticAiWhenClientSucceeds()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var scenario = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var handler = new Handler(async request =>
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var reqId = doc.RootElement.GetProperty("requestId").GetInt32();
            var projId = doc.RootElement.GetProperty("projectId").GetInt32();
            var firstItemId = doc.RootElement.GetProperty("items")[0].GetProperty("itemId").GetInt32();
            var dynamicJson = $$"""
            {"success":true,"recommendation":{"riskLevel":"Low","summary":"Lead time is sufficient. Proceed with standard competitive RFQ.",
            "planningFlags":["Adequate schedule (>7 days)."],"requiredChecks":["Verify engineer specs."],
            "recommendedApproach":"Standard Competitive RFQ Process (Min. 2 Quotations)","evidenceRefs":["request:{{reqId}}","project:{{projId}}","item:{{firstItemId}}"]},
            "trace":[{"iteration":1,"action":"get_current_material_request_evidence","arguments":{},"success":true},
            {"iteration":2,"action":"get_project_context","arguments":{},"success":true},
            {"iteration":3,"action":"get_recent_material_request_history","arguments":{"limit":5},"success":true},
            {"iteration":4,"action":"final_output","arguments":{},"success":true}],
            "iterationCount":4,"modelIdentifier":"test-gemini-model","errorCode":null}
            """;
            return Reply(dynamicJson);
        });

        var client = Client(handler);
        var service = new ProcurementPlanningAgentService(db, client);

        var result = await service.EvaluateMaterialRequestPlanAsync(scenario.Request.Id, 77);

        Assert.Equal("AgenticAI", result.ExecutionMode);
        Assert.Equal("Low", result.RiskLevel);
        Assert.NotNull(result.Advisory);
        Assert.NotEmpty(result.RequiredChecks);
        Assert.NotEmpty(result.EvidenceRefs);

        var workflow = await db.AgentWorkflows.Include(w => w.Steps).SingleAsync(w => w.MaterialRequestId == scenario.Request.Id);
        Assert.Contains("Mode: AgenticAI", workflow.FinalOutcome);
        var step = Assert.Single(workflow.Steps);
        Assert.Equal(WorkflowStepStatus.Completed, step.Status);
        Assert.Contains("AgenticAI", step.StructuredResult);
    }

    [Fact]
    public async Task ServiceFallsBackToDeterministicWhenClientFailsOrThrows()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var scenario = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        // Client that throws 503
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var client = Client(handler);
        var service = new ProcurementPlanningAgentService(db, client);

        var result = await service.EvaluateMaterialRequestPlanAsync(scenario.Request.Id, 77);

        Assert.Equal("DeterministicFallback", result.ExecutionMode);
        Assert.NotNull(result.Execution);
        Assert.Equal("service_unavailable", result.Execution.FallbackReason);
        Assert.NotNull(result.PlanningFlags);
        Assert.NotEmpty(result.RequiredChecks);

        var workflow = await db.AgentWorkflows.Include(w => w.Steps).SingleAsync(w => w.MaterialRequestId == scenario.Request.Id);
        Assert.Contains("Mode: DeterministicFallback", workflow.FinalOutcome);
        var step = Assert.Single(workflow.Steps);
        Assert.Equal(WorkflowStepStatus.Completed, step.Status);
        Assert.Contains("DeterministicFallback", step.StructuredResult);
    }

    [Fact]
    public async Task ServiceFallsBackToDeterministicWhenNoClientInjected()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var scenario = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var service = new ProcurementPlanningAgentService(db, null);

        var result = await service.EvaluateMaterialRequestPlanAsync(scenario.Request.Id, 77);

        Assert.Equal("DeterministicFallback", result.ExecutionMode);
        Assert.Equal("not_configured", result.Execution?.FallbackReason);

        var workflow = await db.AgentWorkflows.Include(w => w.Steps).SingleAsync(w => w.MaterialRequestId == scenario.Request.Id);
        Assert.Contains("Mode: DeterministicFallback", workflow.FinalOutcome);
    }

    private static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static PlanningAgentClient Client(HttpMessageHandler handler) => new(new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgentService:BaseUrl"] = "http://internal.test/",
            ["AgentService:ApiKey"] = "test-internal-key"
        }).Build());

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }
}

