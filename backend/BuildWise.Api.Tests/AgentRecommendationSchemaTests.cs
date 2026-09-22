using System.Net;
using System.Text;
using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildWise.Api.Tests;

/// <summary>
/// Spec §11 ("AI-specific tests"): the agent's structured output is gated before it is
/// accepted into agent_workflow_steps.structured_result (§5.7), and a schema violation must
/// fail the workflow instead of advancing it to AwaitingApproval (§10 failure handling).
///
/// These also pin the cross-language contract: the Python agent emits snake_case
/// (`recommended_quotation_id`, `ranked_alternatives`), while <see cref="AgentRecommendationDto"/>
/// is PascalCase. When that boundary broke (every field bound to null), no purchase order could
/// ever be created — the existing tests missed it because none of them crossed the boundary.
/// A stub <see cref="HttpMessageHandler"/> stands in for the agent service here, so these tests
/// need neither the microservice nor the network.
/// </summary>
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
    public async Task Workflow_Stores_Agent_Recommendation_When_Service_Answers()
    {
        var db = TestDbFactory.CreateInMemory();
        var (data, supplier, quotation) = await SeedApprovedRequestWithQuotationAsync(db);

        var agentJson = JsonSerializer.Serialize(new
        {
            recommended_quotation_id = quotation.Id,
            recommended_supplier_id = supplier.Id,
            recommended_supplier_name = supplier.Name,
            rationale = AgentRationaleText,
            ranked_alternatives = new[]
            {
                new
                {
                    quotation_id = quotation.Id,
                    supplier_id = supplier.Id,
                    supplier_name = supplier.Name,
                    rank = 1,
                    total_amount = 525000m,
                    reason = "Full coverage across all items, total 525,000.00"
                }
            },
            warnings = Array.Empty<string>()
        });

        var handler = new StubAgentHandler(agentJson);
        var service = BuildWorkflowService(db, handler);

        var start = await service.StartWorkflowAsync(data.Request.Id, initiatedByUserId: 1);
        var details = await service.GetWorkflowDetailsAsync(start.WorkflowId);

        Assert.Equal("AwaitingApproval", start.Status);
        Assert.NotNull(details);
        Assert.NotNull(details!.Recommendation);

        // The agent's own values must survive the snake_case -> PascalCase boundary.
        Assert.Equal(quotation.Id, details.Recommendation.RecommendedQuotationId);
        Assert.Equal(supplier.Id, details.Recommendation.RecommendedSupplierId);
        Assert.Equal(supplier.Name, details.Recommendation.RecommendedSupplierName);
        Assert.Equal(AgentRationaleText, details.Recommendation.Rationale);
        Assert.Single(details.Recommendation.RankedAlternatives);

        // The outbound payload is still the snake_case contract the agent service expects.
        Assert.Equal("/analyze", handler.LastRequestUri!.AbsolutePath);
        Assert.Contains("material_request_id", handler.LastRequestBody!);
        Assert.Contains("requested_quantities", handler.LastRequestBody!);

        // Deterministic validation re-checks the recommendation (§5) before it is routed on.
        Assert.NotNull(details.Validation);
        Assert.True(details.Validation.IsValid);
    }

    [Fact]
    public async Task Workflow_Fails_Without_Awaiting_Approval_When_Agent_Payload_Is_Schema_Invalid()
    {
        var db = TestDbFactory.CreateInMemory();
        var (data, _, _) = await SeedApprovedRequestWithQuotationAsync(db);

        // HTTP 200 whose body does not satisfy the recommendation schema (§10 failure handling).
        var agentJson = JsonSerializer.Serialize(new
        {
            recommended_quotation_id = (int?)null,
            recommended_supplier_id = (int?)null,
            recommended_supplier_name = (string?)null,
            rationale = "",
            ranked_alternatives = Array.Empty<object>(),
            warnings = Array.Empty<string>()
        });

        var service = BuildWorkflowService(db, new StubAgentHandler(agentJson));

        var start = await service.StartWorkflowAsync(data.Request.Id, initiatedByUserId: 1);
        var details = await service.GetWorkflowDetailsAsync(start.WorkflowId);

        Assert.Equal("Failed", start.Status);
        Assert.Contains("Agent schema validation failed", start.Message);

        Assert.NotNull(details);
        Assert.Equal("Failed", details!.Status);
        Assert.Contains("schema violation", details.FinalOutcome!, StringComparison.OrdinalIgnoreCase);

        // The gate held: the analysis step failed, nothing was stored as a recommendation,
        // the workflow never reached AwaitingApproval, and no purchase order was created.
        var analysisStep = details.Steps.Single(s => s.AgentRole == "QuotationSupplierAnalysisAgent");
        Assert.Equal("Failed", analysisStep.Status);
        Assert.Null(analysisStep.StructuredResult);
        Assert.Null(details.Recommendation);
        Assert.NotEqual("AwaitingApproval", details.Status);
        Assert.Empty(db.PurchaseOrders);
    }

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
            NullLogger<ProcurementWorkflowService>.Instance);
    }

    /// <summary>Stands in for the Python agent service and records what the API sent it.</summary>
    private sealed class StubAgentHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public StubAgentHandler(string responseJson) => _responseJson = responseJson;

        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
