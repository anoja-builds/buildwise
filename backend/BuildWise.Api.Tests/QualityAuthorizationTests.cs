using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BuildWise.Api.Controllers;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace BuildWise.Api.Tests;

// Real JWT bearer validation and MVC authorization, with an isolated in-memory
// database. Does not execute Program.cs, database migrations, or the AI service.
public class QualityAuthorizationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private IConfiguration _configuration = null!;

    public async Task InitializeAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = key,
            ["Jwt:Issuer"] = "QualityTests",
            ["Jwt:Audience"] = "QualityTests"
        }).Build();
        _host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_configuration);
                services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.AddScoped<QualityInspectionService>();
                services.AddScoped<NonConformanceService>();
                services.AddScoped<QualityRiskEvidenceService>();
                services.AddScoped<QualityRiskRecommendationValidator>();
                services.AddScoped<QualityRiskAgentService>();
                services.AddHttpClient<QualityRiskAgentClient>();
                services.AddControllers().AddApplicationPart(typeof(InspectionsController).Assembly);
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true, ValidateAudience = true,
                        ValidateLifetime = true, ValidateIssuerSigningKey = true,
                        ValidIssuer = "QualityTests", ValidAudience = "QualityTests",
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                        ClockSkew = TimeSpan.FromMinutes(1)
                    };
                });
                services.AddAuthorization();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            })).StartAsync();
        _client = _host.GetTestClient();
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Users.AddRange(
            new User { Id = 7, FullName = "Acting inspector", Email = "actor@example.test" },
            new User { Id = 8, FullName = "Original inspector", Email = "original@example.test" },
            new User { Id = 9, FullName = "Inactive inspector", Email = "inactive@example.test", IsActive = false });
        db.Inspections.Add(new Inspection
        {
            Id = 1, InspectorUserId = 8, Status = InspectionStatus.Completed,
            OverallDecision = InspectionDecision.Accepted,
            Delivery = new Delivery
            {
                Status = DeliveryStatus.Received,
                PurchaseOrder = new PurchaseOrder { Supplier = new Supplier { Name = "Test supplier" } }
            }
        });
        await db.SaveChangesAsync();
    }

    private void SignIn(string role, int userId = 7)
    {
        var token = new JwtTokenService(_configuration).GenerateToken(
            new User { Id = userId, FullName = "Test user", Email = "test@example.test" }, [role]).Token;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public static IEnumerable<object[]> ProtectedEndpoints()
    {
        (string Method, string Path)[] endpoints =
        [
            ("GET", "/api/inspections/pending-deliveries"),
            ("POST", "/api/inspections"),
            ("GET", "/api/inspections/1"),
            ("POST", "/api/inspections/1/complete"),
            ("GET", "/api/non-conformances"),
            ("GET", "/api/non-conformances/1"),
            ("POST", "/api/non-conformances"),
            ("PATCH", "/api/non-conformances/1/corrective-action"),
            ("POST", "/api/non-conformances/1/resolve"),
            ("POST", "/api/non-conformances/1/close"),
            ("POST", "/api/quality-risk-agent/inspections/1/analyse"),
            ("GET", "/api/quality-risk-agent/workflows/1")
        ];
        foreach (var endpoint in endpoints)
        {
            yield return [endpoint.Method, endpoint.Path, "", HttpStatusCode.Unauthorized];
            yield return [endpoint.Method, endpoint.Path, "ProcurementOfficer", HttpStatusCode.Forbidden];
            yield return [endpoint.Method, endpoint.Path, "ReceivingOfficer", HttpStatusCode.Forbidden];
        }
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Every_quality_endpoint_requires_an_allowed_role(
        string method, string path, string role, HttpStatusCode expected)
    {
        if (role.Length > 0) SignIn(role);
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { })
        };
        using var response = await _client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("QualityInspector")]
    [InlineData("Administrator")]
    public async Task Allowed_roles_can_read_quality_data(string role)
    {
        SignIn(role);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/inspections/pending-deliveries")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/inspections/1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/non-conformances")).StatusCode);
    }

    [Fact]
    public async Task Inspection_detail_exposes_delivery_quantities_without_creating_inspection_items()
    {
        SignIn("QualityInspector");
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inspection = await db.Inspections.SingleAsync();
        db.DeliveryItems.Add(new DeliveryItem { DeliveryId = inspection.DeliveryId,
            PurchaseOrderItemId = 11, ReceivedQuantity = 240, DamagedQuantity = 5 });
        await db.SaveChangesAsync();
        var response = await _client.GetFromJsonAsync<BuildWise.Api.Models.Dtos.QualityInspectionResponseDto>("/api/inspections/1");
        Assert.NotNull(response);
        var item = Assert.Single(response.DeliveryItems);
        Assert.Equal(240, item.ReceivedQuantity);
        Assert.Equal(5, item.DamagedQuantity);
        Assert.Equal(11, item.PurchaseOrderItemId);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Workflow_records_JWT_actor_not_original_inspector_or_spoofed_body()
    {
        SignIn("QualityInspector");
        using var response = await _client.PostAsJsonAsync("/api/quality-risk-agent/inspections/1/analyse",
            new { initiatedByUserId = 8 });
        // The subject intentionally has no inspection items: evidence validation
        // fails before any AI request, but the initiating audit must be durable.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workflow = await db.AgentWorkflows.SingleAsync();
        Assert.Equal(7, workflow.InitiatedByUserId);
        Assert.Equal(WorkflowStatus.Failed, workflow.Status);
        Assert.Equal(3, await db.AgentWorkflowSteps.CountAsync());
        Assert.Equal(HttpStatusCode.OK,
            (await _client.GetAsync($"/api/quality-risk-agent/workflows/{workflow.Id}")).StatusCode);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(999)]
    public async Task Start_cannot_substitute_body_identity_for_inactive_or_missing_JWT_user(int userId)
    {
        SignIn("QualityInspector", userId);
        using var response = await _client.PostAsJsonAsync("/api/inspections",
            new { deliveryId = 1, inspectorUserId = 7 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(999)]
    public async Task Inactive_or_missing_actor_cannot_create_workflow(int userId)
    {
        SignIn("QualityInspector", userId);
        using var response = await _client.PostAsJsonAsync("/api/quality-risk-agent/inspections/1/analyse",
            new { initiatedByUserId = 7 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = _host.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AgentWorkflows.ToListAsync());
    }

    [Theory]
    [InlineData("/api/inspections")]
    [InlineData("/api/quality-risk-agent/inspections/1/analyse")]
    public async Task Invalid_JWT_user_id_is_rejected(string path)
    {
        SignIn("QualityInspector", 0);
        using var response = await _client.PostAsJsonAsync(path, new { deliveryId = 1, inspectorUserId = 7 });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}
