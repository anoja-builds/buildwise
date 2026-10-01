using System.Net;
using System.Text;
using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildWise.Api.Tests;

/// <summary>Agent 2 advisory output is validated without allowing provider output to select a winner.</summary>
public class AgentRecommendationSchemaTests
{
    private const string AgentRationaleText =
        "Supplier A was selected for full compliant coverage at the lowest eligible price.";

    // ------------------------------------------------------------------ schema gate (§5.7)

    [Fact]
    public void Rejects_Missing_Recommendation()
    {
        var db = TestDbFactory.CreateInMemory();
        var service = new ProcurementValidationService(db);

        var result = service.ValidateRecommendationSchema(null);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Recommendation payload is null"));
    }

    [Fact]
    public void Rejects_Malformed_Agent_Payload()
    {
        var db = TestDbFactory.CreateInMemory();
        var service = new ProcurementValidationService(db);

        // No ids, no rationale, null alternatives — a shape the agent must never be trusted with.
        var malformed = new AgentRecommendationDto(
            RecommendedQuotationId: null,
            RecommendedSupplierId: null,
            RecommendedSupplierName: null,
            Rationale: "   ",
            RankedAlternatives: null!,
            Warnings: new List<string>()
        );

        var result = service.ValidateRecommendationSchema(malformed);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("recommended_quotation_id"));
        Assert.Contains(result.Errors, e => e.Contains("recommended_supplier_id"));
        Assert.Contains(result.Errors, e => e.Contains("rationale"));
        Assert.Contains(result.Errors, e => e.Contains("ranked_alternatives"));
    }

    [Fact]
    public void Accepts_Schema_Valid_Agent_Payload()
    {
        var db = TestDbFactory.CreateInMemory();
        var service = new ProcurementValidationService(db);

        var valid = new AgentRecommendationDto(
            RecommendedQuotationId: 3,
            RecommendedSupplierId: 1,
            RecommendedSupplierName: "Supplier A Building Materials",
            Rationale: "Lowest-cost compliant supplier with full quantity coverage.",
            RankedAlternatives: new List<RankedAlternativeDto>
            {
                new(3, 1, "Supplier A Building Materials", 1, 525000m, "Full coverage across all items")
            },
            Warnings: new List<string>()
        );

        var result = service.ValidateRecommendationSchema(valid);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // ------------------------------------------------------------------ workflow gate (§10)

    [Fact]
    public async Task Workflow_Persists_Validated_Advisory_WithoutChangingWinnerOrApproval()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (data, supplier, quotation) = await SeedApprovedRequestWithQuotationAsync(db);
        var handler = new StubAgentHandler(ValidAdvisory(data.Request.Id, quotation.Id));
        var service = BuildWorkflowService(db, handler);
        var start = await service.StartWorkflowAsync(data.Request.Id, 1);
        var details = (await service.GetWorkflowDetailsAsync(start.WorkflowId))!;
        Assert.Equal("AwaitingApproval", start.Status);
        Assert.Equal("Pending", details.ApprovalStatus);
        Assert.Empty(db.PurchaseOrders);
        var r = details.Recommendation!;
        Assert.Equal(quotation.Id, r.RecommendedQuotationId);
        Assert.Equal(supplier.Id, r.RecommendedSupplierId);
        Assert.Equal(supplier.Name, r.RecommendedSupplierName);
        Assert.Contains("lowest-cost compliant", r.Rationale);
        Assert.Equal(525000m, Assert.Single(r.RankedAlternatives).TotalAmount);
        Assert.Equal("AgenticAI", r.ExecutionMode);
        Assert.Equal("test-model", r.ModelIdentifier);
        Assert.Equal(4, r.IterationCount);
        Assert.Equal(4, r.ToolTrace!.Count);
        Assert.Equal(3, r.ToolsUsed!.Count);
        Assert.Null(r.FallbackReason);
        Assert.Equal(AgentRationaleText, r.Advisory!.Summary);
        Assert.Equal("/procurement/analyse", handler.LastRequestUri!.AbsolutePath);
        Assert.Contains("selectedQuotationId", handler.LastRequestBody!);
        Assert.True(details.Validation!.IsValid);
        var step = details.Steps.Single(s => s.AgentRole == "ProcurementAdvisoryAgent");
        Assert.Contains("AgenticAI", step.StructuredResult!);
        Assert.Contains("EvidenceRefs", step.StructuredResult!);
        Assert.DoesNotContain("Agent service unavailable", string.Join(" ", r.Warnings));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePurchaseOrderFromWorkflowAsync(start.WorkflowId));
        Assert.Empty(db.PurchaseOrders);
        await service.RecordDecisionAsync(start.WorkflowId, new("Approve", "Manager reviewed the fixed selection.", 2));
        Assert.Equal(quotation.Id, (await db.PurchaseOrders.SingleAsync()).QuotationId);
    }

    [Fact]
    public async Task AdvisorySeesFixedRankingAndScopedSupplierEvidence()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (data, supplier, winner) = await SeedApprovedRequestWithQuotationAsync(db);
        var suspended = new Supplier { Name = "Cheaper suspended supplier", Status = SupplierStatus.Suspended };
        var partial = new Supplier { Name = "Cheaper partial supplier", Status = SupplierStatus.Active };
        var secondEligible = new Supplier { Name = "More expensive eligible supplier", Status = SupplierStatus.Active };
        Quotation Quote(Supplier s, decimal quantity, decimal price) => new()
        {
            MaterialRequestId = data.Request.Id, Supplier = s, Status = QuotationStatus.Submitted,
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), TotalAmount = quantity * price,
            Items = [new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = quantity, UnitPrice = price }]
        };
        var excluded = Quote(suspended, 250, 2000);
        var incomplete = Quote(partial, 200, 2000);
        var runnerUp = Quote(secondEligible, 250, 2200);
        db.Quotations.AddRange(excluded, incomplete, runnerUp);
        db.PurchaseOrders.Add(new() { SupplierId = supplier.Id, Status = PurchaseOrderStatus.Completed, CreatedAt = DateTime.UtcNow.AddDays(-10) });
        db.PurchaseOrders.Add(new() { Supplier = new() { Name = "Unrelated supplier" }, Status = PurchaseOrderStatus.Completed });
        await db.SaveChangesAsync();
        var handler = new StubAgentHandler(ValidAdvisory(data.Request.Id, winner.Id));
        var service = BuildWorkflowService(db, handler);
        var start = await service.StartWorkflowAsync(data.Request.Id, 1);
        var details = (await service.GetWorkflowDetailsAsync(start.WorkflowId))!;
        var r = details.Recommendation!;
        Assert.Equal("AgenticAI", r.ExecutionMode);
        Assert.Equal(winner.Id, r.RecommendedQuotationId);
        Assert.Equal(new[] { winner.Id, runnerUp.Id }, r.RankedAlternatives.Select(a => a.QuotationId));
        Assert.Equal(new[] { 525000m, 550000m }, r.RankedAlternatives.Select(a => a.TotalAmount));
        var evidence = r.AdvisoryEvidence!;
        Assert.False(evidence.Quotations.Single(q => q.QuotationId == excluded.Id).Eligible);
        Assert.False(evidence.Quotations.Single(q => q.QuotationId == incomplete.Id).Eligible);
        Assert.Equal(2, evidence.Quotations.Single(q => q.QuotationId == runnerUp.Id).Rank);
        Assert.Equal(4, evidence.Suppliers.Count);
        Assert.Equal(1, evidence.Suppliers.Single(s => s.SupplierId == supplier.Id).CompletedOrderCount);
        Assert.DoesNotContain(evidence.Suppliers, s => s.Name == "Unrelated supplier");
        Assert.Equal(2, await db.PurchaseOrders.CountAsync()); // Only the two seeded historical orders exist.
        Assert.Equal("Pending", details.ApprovalStatus);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("winner")]
    [InlineData("references")]
    [InlineData("missing-tools")]
    [InlineData("missing-summary")]
    [InlineData("risk-level")]
    [InlineData("http-failure")]
    [InlineData("provider-failure")]
    public async Task InvalidOrUnavailableAdvisory_FallsBack_WithoutChangingAuthoritativeResult(string failure)
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (data, _, quotation) = await SeedApprovedRequestWithQuotationAsync(db);
        var json = ValidAdvisory(data.Request.Id, quotation.Id);
        json = failure switch
        {
            "malformed" => "not-json SECRET",
            "winner" => json.Replace("\"riskLevel\":", "\"selectedQuotationId\":999,\"riskLevel\":"),
            "references" => json.Replace($"request:{data.Request.Id}", "SECRET"),
            "missing-tools" => json.Replace("get_material_request_requirements", "create_purchase_order"),
            "missing-summary" => json.Replace("\"summary\":", "\"unknownSummary\":"),
            "risk-level" => json.Replace("Medium", "SECRET"),
            "provider-failure" => JsonSerializer.Serialize(new { success = false, recommendation = (object?)null,
                trace = Array.Empty<object>(), iterationCount = 0, modelIdentifier = "test-model", errorCode = "provider_or_agent_failure" }),
            _ => json
        };
        var handler = new StubAgentHandler(json, failure == "http-failure" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        var service = BuildWorkflowService(db, handler);
        var start = await service.StartWorkflowAsync(data.Request.Id, 1);
        var details = (await service.GetWorkflowDetailsAsync(start.WorkflowId))!;
        Assert.Equal("AwaitingApproval", details.Status);
        Assert.Equal("Pending", details.ApprovalStatus);
        Assert.True(details.Validation!.IsValid);
        Assert.Empty(db.PurchaseOrders);
        Assert.Equal(quotation.Id, details.Recommendation!.RecommendedQuotationId);
        Assert.Contains("lowest-cost compliant", details.Recommendation.Rationale);
        Assert.Equal("DeterministicFallback", details.Recommendation.ExecutionMode);
        Assert.NotNull(details.Recommendation.FallbackReason);
        Assert.Null(details.Recommendation.Advisory);
        Assert.DoesNotContain("SECRET", string.Join("", details.Steps.Select(s => s.StructuredResult)));
    }

    [Fact]
    public async Task InvalidBusinessTotals_BlockApprovalBeforeAdvisory()
    {
        await using var db = TestDbFactory.CreateInMemory();
        var (data, _, quotation) = await SeedApprovedRequestWithQuotationAsync(db);
        quotation.TotalAmount = 1;
        await db.SaveChangesAsync();
        var handler = new StubAgentHandler("{}");
        var service = BuildWorkflowService(db, handler);
        var start = await service.StartWorkflowAsync(data.Request.Id, 1);
        Assert.Equal("Failed", start.Status);
        Assert.Null(handler.LastRequestUri);
        Assert.Empty(db.PurchaseOrders);
    }

    private static string ValidAdvisory(int requestId, int quotationId) => JsonSerializer.Serialize(new
    {
        success = true,
        recommendation = new { summary = AgentRationaleText, riskLevel = "Medium", risks = new[] { "Confirm delivery timing." },
            clarificationQuestions = Array.Empty<string>(), recommendedFollowUps = new[] { "Ask supplier for confirmation." },
            evidenceRefs = new[] { $"request:{requestId}", $"quotation:{quotationId}" } },
        trace = new[] { new { iteration = 1, action = "get_material_request_requirements", arguments = new { }, success = true },
            new { iteration = 2, action = "get_validated_quotation_comparison", arguments = new { }, success = true },
            new { iteration = 3, action = "get_supplier_procurement_evidence", arguments = new { }, success = true },
            new { iteration = 4, action = "final_output", arguments = new { }, success = true } },
        iterationCount = 4, modelIdentifier = "test-model", errorCode = (string?)null
    });

    // ------------------------------------------------------------------ helpers

    private static async Task<(StandardScenarioEntities Data, Supplier Supplier, Quotation Quotation)>
        SeedApprovedRequestWithQuotationAsync(ApplicationDbContext db)
    {
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);

        var supplier = new Supplier { Name = "Supplier A Building Materials", Status = SupplierStatus.Active };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var quotation = new Quotation
        {
            MaterialRequestId = data.Request.Id,
            SupplierId = supplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = QuotationStatus.Submitted,
            TotalAmount = 525000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250m, UnitPrice = 2100m }
            }
        };
        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        return (data, supplier, quotation);
    }

    private static ProcurementWorkflowService BuildWorkflowService(ApplicationDbContext db, StubAgentHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://agent.test") };
        var agentClient = new QuotationAgentClient(http, NullLogger<QuotationAgentClient>.Instance);

        return new ProcurementWorkflowService(
            db,
            agentClient,
            new ProcurementValidationService(db),
            new NoOpEmailService(),
            NullLogger<ProcurementWorkflowService>.Instance,
            new ProcurementAdvisoryService(db, new ProcurementAdvisoryClient(http,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AgentService:BaseUrl"] = "http://agent.test",
                    ["AgentService:ApiKey"] = "test-key\r\n"
                }).Build()), new ProcurementValidationService(db)));
    }

    /// <summary>Stands in for the Python agent service and records what the API sent it.</summary>
    private sealed class StubAgentHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _status;

        public StubAgentHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
        {
            _responseJson = responseJson;
            _status = status;
        }

        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("test-key", request.Headers.GetValues("X-Quality-Agent-Key").Single());
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
