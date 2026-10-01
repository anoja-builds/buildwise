using System.Net;
using System.Text;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BuildWise.Api.Tests;

public class QualityRiskAgentClientTests
{
    private static QualityRiskEvidence Evidence() => new(
        1,
        1,
        1,
        "Supplier",
        "Active",
        DateTime.UtcNow,
        "Completed",
        DateTime.UtcNow,
        [],
        [],
        "",
        0,
        [],
        false,
        [],
        false,
        [],
        false,
        ["inspection:1"]
    );

    private const string ValidJson = """
        {
            "success": true,
            "recommendation": {
                "inspectionId": 1,
                "riskLevel": "Low",
                "riskFlags": [],
                "evidenceSummary": "Acceptable",
                "ncrRecommended": false,
                "itemRecommendations": [],
                "rationaleSummary": "Compliant"
            },
            "trace": [],
            "iterationCount": 1,
            "modelIdentifier": "test-model",
            "errorCode": null
        }
        """;

    [Theory]
    [InlineData("test-internal-key\r\n")]
    [InlineData(" \ttest-internal-key\n")]
    [InlineData("test-internal-key ")]
    public async Task ClientTrimsSurroundingServiceKeyWhitespaceBeforeAddingHeader(string configuredKey)
    {
        var handler = new Handler(request =>
        {
            Assert.Equal("test-internal-key", request.Headers.GetValues("X-Quality-Agent-Key").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ValidJson, Encoding.UTF8, "application/json")
            });
        });

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgentService:BaseUrl"] = "http://internal.test/",
            ["AgentService:ApiKey"] = configuredKey
        }).Build();

        using var http = new HttpClient(handler);
        var client = new QualityRiskAgentClient(http, configuration);
        var result = await client.AnalyseAsync(Evidence(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }
}
