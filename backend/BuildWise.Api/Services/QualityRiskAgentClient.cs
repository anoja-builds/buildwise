using System.Net.Http.Json;
using System.Text.Json;
using BuildWise.Api.Models.Dtos;

namespace BuildWise.Api.Services;

public class QualityRiskAgentClient(HttpClient http, IConfiguration configuration)
{
    public async Task<QualityAgentResult> AnalyseAsync(QualityRiskEvidence evidence, CancellationToken ct)
    {
        var baseUrl = configuration["AgentService:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new QualityRiskException(503, "Internal agent service URL is not configured.");
        var key = configuration["AgentService:ApiKey"] ?? Environment.GetEnvironmentVariable("QUALITY_AGENT_SERVICE_KEY");
        if (string.IsNullOrWhiteSpace(key)) throw new QualityRiskException(503, "Internal agent service credentials are not configured.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(uri.AbsoluteUri.TrimEnd('/') + "/quality-risk/analyse"))
        {
            Content = JsonContent.Create(evidence, options: QualityAgentJson.Options)
        };
        request.Headers.Add("X-Quality-Agent-Key", key);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new QualityRiskException(502, "Internal agent service rejected the analysis request.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + read > 1_000_000) throw new QualityRiskException(502, "Agent response exceeded the size limit.");
            buffer.Write(chunk, 0, read);
        }
        return JsonSerializer.Deserialize<QualityAgentResult>(buffer.ToArray(), QualityAgentJson.Options)
            ?? throw new QualityRiskException(502, "Agent response was empty.");
    }
}
