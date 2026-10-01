using System.Net;
using System.Text;
using System.Text.Json;
using BuildWise.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BuildWise.Api.Tests;

public class DeliveryDiscrepancyAgentClientTests
{
    private static DeliveryAgentEvidence Evidence() => new(8, 3, "Supplier", DateTime.UtcNow,
        [new("po-item:1", "Cement", "bags", 20, 13, 7, 2, 7, 2, 0, false)],
        [], false, [], false, ["delivery:8", "po-item:1"]);

    private const string ValidJson = """
        {"success":true,"recommendation":{"riskLevel":"High","summary":"Damage warrants review.",
        "likelyCauses":["Possible handling damage."],"recommendedActions":["Contact supplier."],
        "supplierFollowUpRequired":true,"evidenceRefs":["delivery:8"]},
        "trace":[{"iteration":1,"action":"get_current_delivery_evidence","arguments":{},"success":true},
        {"iteration":2,"action":"get_supplier_delivery_history","arguments":{"limit":1},"success":true},
        {"iteration":3,"action":"get_previous_discrepancy_summary","arguments":{},"success":true},
        {"iteration":4,"action":"final_output","arguments":{},"success":true}],
        "iterationCount":4,"modelIdentifier":"test-model","errorCode":null}
        """;

    [Fact]
    public async Task ClientUsesAuthenticatedInternalEndpointAndCamelCaseEvidence()
    {
        var handler = new Handler(async request =>
        {
            Assert.Equal("http://internal.test/delivery-discrepancy/analyse", request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-internal-key", request.Headers.GetValues("X-Quality-Agent-Key").Single());
            Assert.Equal(HttpMethod.Post, request.Method);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(8, body.RootElement.GetProperty("deliveryId").GetInt32());
            Assert.False(body.RootElement.GetProperty("items")[0].GetProperty("overDelivery").GetBoolean());
            return Reply(ValidJson);
        });
        var result = await Client(handler).AnalyseAsync(Evidence());
        DeliveryAgentValidator.Validate(result, Evidence());
        Assert.True(result.Success);
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
        var result = await new DeliveryDiscrepancyAgentClient(http, configuration).AnalyseAsync(Evidence());
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("string-bool")]
    [InlineData("oversized")]
    [InlineData("malformed")]
    public async Task ClientRejectsInvalidJsonContracts(string variant)
    {
        var json = variant switch
        {
            "extra" => ValidJson.Replace("\"riskLevel\":", "\"deliveryStatus\":\"Received\",\"riskLevel\":"),
            "missing" => ValidJson.Replace("\"supplierFollowUpRequired\":true,", ""),
            "string-bool" => ValidJson.Replace("\"supplierFollowUpRequired\":true", "\"supplierFollowUpRequired\":\"true\""),
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
            new DeliveryDiscrepancyAgentClient(http, new ConfigurationBuilder().Build()).AnalyseAsync(Evidence()));
    }

    [Fact]
    public void BackendRejectsReferencesOutsideObservedHistoryAndInvalidExecutionBounds()
    {
        var valid = JsonSerializer.Deserialize<DeliveryAgentResponse>(ValidJson, DeliveryDiscrepancyAgentClient.JsonOptions)!;
        var evidence = Evidence() with
        {
            History = [new("delivery:7", 7, DateTime.UtcNow.AddDays(-1), "Received", 1, 0),
                       new("delivery:6", 6, DateTime.UtcNow.AddDays(-2), "Received", 1, 0)],
            EvidenceRefs = ["delivery:8", "po-item:1", "delivery:7", "delivery:6"]
        };
        Assert.Throws<JsonException>(() => DeliveryAgentValidator.Validate(valid with
        {
            Recommendation = valid.Recommendation! with { EvidenceRefs = ["delivery:8", "delivery:6"] }
        }, evidence));
        Assert.Throws<JsonException>(() => DeliveryAgentValidator.Validate(valid with { IterationCount = 7 }, evidence));
        Assert.Throws<JsonException>(() => DeliveryAgentValidator.Validate(valid with
        {
            Trace = [new(1, "change_delivery_status", [], true)]
        }, evidence));
    }

    private static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static DeliveryDiscrepancyAgentClient Client(HttpMessageHandler handler) => new(new HttpClient(handler),
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
