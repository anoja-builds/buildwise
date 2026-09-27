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
public class SecurityAuthorizationTests : IAsyncLifetime
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
            ["Jwt:Issuer"] = "SecurityTests",
            ["Jwt:Audience"] = "SecurityTests"
        }).Build();
        _host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_configuration);
                services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.AddScoped<AuthService>();
                services.AddSingleton<JwtTokenService>();
                services.AddScoped<ProcurementPlanningAgentService>();
                services.AddScoped<DeliveryRiskAgentService>();
                services.AddScoped<SupplierEvaluationAgentService>();
                services.AddScoped<ProcurementValidationService>();
                services.AddScoped<ProcurementWorkflowService>();
                services.AddSingleton<IEmailService, NoOpEmailService>();
                services.AddHttpClient<QuotationAgentClient>();
                services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly)
                    .AddJsonOptions(o => { o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()); o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles; });
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true, ValidateAudience = true,
                        ValidateLifetime = true, ValidateIssuerSigningKey = true,
                        ValidIssuer = "SecurityTests", ValidAudience = "SecurityTests",
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
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new User { Id = 77, FullName = "JWT actor", Email = "actor@test.example" });
        var scenario = await TestDbFactory.SeedStandardScenarioDataAsync(db);
        var supplier = new Supplier { Name = "Active supplier", Status = SupplierStatus.Active };
        var quotation = new Quotation { MaterialRequestId = scenario.Request.Id, Supplier = supplier,
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), TotalAmount = 500,
            Items = [new QuotationItem { MaterialRequestItemId = scenario.RequestItem.Id, Quantity = 250, UnitPrice = 2 }] };
        db.Quotations.Add(quotation);
        db.ProcurementRecommendations.Add(new ProcurementRecommendation { MaterialRequestId = scenario.Request.Id,
            RecommendedSupplier = supplier, RecommendedQuotation = quotation });
        db.AgentWorkflows.Add(new AgentWorkflow { MaterialRequestId = scenario.Request.Id,
            Status = WorkflowStatus.AwaitingApproval, Steps = [new AgentWorkflowStep {
                StructuredResult = System.Text.Json.JsonSerializer.Serialize(new BuildWise.Api.DTOs.AgentRecommendationDto(
                    1, 1, "Active supplier", "Best quote", [], [])) }] });
        db.Deliveries.Add(new Delivery { PurchaseOrder = new PurchaseOrder { Supplier = supplier } });
        await db.SaveChangesAsync();
    }

    private void SignIn(string role, int userId = 77)
    {
        var token = new JwtTokenService(_configuration).GenerateToken(
            new User { Id = userId, FullName = "Actor", Email = "actor@test.example" }, [role]).Token;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public static IEnumerable<object[]> ProtectedEndpoints()
    {
        (string Method, string Path, string WrongRole)[] endpoints =
        [
            ("GET", "/api/MaterialRequests", "ReceivingOfficer"),
            ("GET", "/api/MaterialRequests/1", "ReceivingOfficer"),
            ("POST", "/api/MaterialRequests", "ProcurementOfficer"),
            ("POST", "/api/MaterialRequests/1/submit", "ProcurementOfficer"),
            ("POST", "/api/MaterialRequests/1/approve", "SiteEngineer"),
            ("POST", "/api/MaterialRequests/1/plan", "ReceivingOfficer"),
            ("GET", "/api/Deliveries/expected", "SiteEngineer"),
            ("GET", "/api/Deliveries/history", "SiteEngineer"),
            ("GET", "/api/Deliveries/1", "SiteEngineer"),
            ("GET", "/api/Deliveries/schedules", "SiteEngineer"),
            ("GET", "/api/Deliveries/issues", "SiteEngineer"),
            ("POST", "/api/Deliveries", "SiteEngineer"),
            ("POST", "/api/Deliveries/1/receive", "ProcurementOfficer"),
            ("POST", "/api/Deliveries/1/evidence", "ProcurementOfficer"),
            ("POST", "/api/Deliveries/schedule", "SiteEngineer"),
            ("POST", "/api/Deliveries/report-issue", "ProcurementOfficer"),
            ("POST", "/api/Deliveries/evaluate-risk/1", "SiteEngineer"),
            ("POST", "/api/Deliveries/1/risk-analysis", "SiteEngineer"),
            ("GET", "/api/Procurement/rfqs", "SiteEngineer"),
            ("POST", "/api/Procurement/rfqs", "SiteEngineer"),
            ("POST", "/api/Procurement/quotations", "SiteEngineer"),
            ("GET", "/api/Procurement/comparison/1", "SiteEngineer"),
            ("POST", "/api/Procurement/1/evaluate", "SiteEngineer"),
            ("GET", "/api/Procurement/recommendations", "SiteEngineer"),
            ("GET", "/api/Procurement/purchase-orders", "SiteEngineer"),
            ("POST", "/api/Procurement/recommendations/1/approve", "ProcurementOfficer"),
            ("POST", "/api/procurement-workflow/1/decision", "ProcurementOfficer"),
            ("POST", "/api/procurement-workflow/1/purchase-order", "ProcurementOfficer"),
            ("POST", "/api/material-requests/1/procurement-workflow", "SiteEngineer")
        ];
        foreach (var endpoint in endpoints)
        {
            yield return [endpoint.Method, endpoint.Path, "", HttpStatusCode.Unauthorized];
            yield return [endpoint.Method, endpoint.Path, endpoint.WrongRole, HttpStatusCode.Forbidden];
        }
    }

    [Theory, MemberData(nameof(ProtectedEndpoints))]
    public async Task Routes_enforce_authentication_and_roles(string method, string path, string role, HttpStatusCode expected)
    {
        if (role.Length > 0) SignIn(role);
        using var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("ProjectManager")]
    [InlineData("ProcurementOfficer")]
    [InlineData("ProcurementManager")]
    [InlineData("ReceivingOfficer")]
    [InlineData("QualityInspector")]
    [InlineData("siteengineer")]
    public async Task Public_registration_rejects_privileged_or_unrecognized_roles(string role)
    {
        var dto = new BuildWise.Api.DTOs.RegisterRequestDto("Attacker", "new@test.example", "Password123", role);
        using var response = await _client.PostAsJsonAsync("/api/auth/register", dto);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _host.Services.CreateScope();
        await Assert.ThrowsAsync<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<AuthService>().RegisterAsync(dto));
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AnyAsync(u => u.Email == dto.Email));
    }

    [Fact]
    public async Task Public_registration_allows_only_site_engineer()
    {
        using var response = await _client.PostAsJsonAsync("/api/auth/register",
            new BuildWise.Api.DTOs.RegisterRequestDto("Engineer", "new@test.example", "Password123", "SiteEngineer"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<BuildWise.Api.DTOs.AuthResponseDto>();
        Assert.Equal("SiteEngineer", Assert.Single(auth!.User.Roles));
    }

    [Fact]
    public async Task Request_creation_ignores_spoofed_actor()
    {
        SignIn("SiteEngineer");
        using var response = await _client.PostAsJsonAsync("/api/MaterialRequests", new {
            projectId = 1, requestedByUserId = 999, reason = "Test", items = new[] { new { materialId = 1, quantity = 1, unit = "Bags" } } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(77, (await db.MaterialRequests.OrderByDescending(r => r.Id).FirstAsync()).RequestedByUserId);
    }

    [Fact]
    public async Task Receipt_and_issue_use_JWT_actor()
    {
        SignIn("ReceivingOfficer");
        using var receipt = await _client.PostAsJsonAsync("/api/Deliveries/1/receive", new { receivedByUserId = 999, items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        using var issue = await _client.PostAsJsonAsync("/api/Deliveries/report-issue", new { deliveryId = 1, reportedByUserId = 999, issueType = "Damage", description = "Test" });
        Assert.Equal(HttpStatusCode.OK, issue.StatusCode);
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(77, (await db.Deliveries.SingleAsync()).ReceivedByUserId);
        Assert.Equal(77, (await db.DeliveryIssues.SingleAsync()).ReportedByUserId);
    }

    [Theory]
    [InlineData("ProcurementManager")]
    [InlineData("Administrator")]
    public async Task Alternative_approval_records_JWT_and_creates_linked_order_once(string role)
    {
        SignIn(role);
        using var response = await _client.PostAsJsonAsync("/api/Procurement/recommendations/1/approve", new { userId = 999, decision = "Approved" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rec = await db.ProcurementRecommendations.SingleAsync();
        Assert.Equal(77, rec.ApprovedByUserId);
        var po = await db.PurchaseOrders.Include(p => p.Items).SingleAsync(p => p.QuotationId == 1);
        Assert.Equal(500, po.TotalAmount);
        Assert.Equal(1, Assert.Single(po.Items).MaterialId);
        Assert.Equal(po.Id, rec.GeneratedPurchaseOrderId);
        using var repeat = await _client.PostAsJsonAsync("/api/Procurement/recommendations/1/approve", new { decision = "Approved" });
        Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("inactive")]
    [InlineData("quantity")]
    [InlineData("total")]
    [InlineData("request")]
    [InlineData("supplier")]
    [InlineData("duplicate")]
    [InlineData("decision")]
    public async Task Alternative_approval_rejects_invalid_data_without_writes(string fault)
    {
        SignIn("ProcurementManager");
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rec = await db.ProcurementRecommendations.Include(r => r.RecommendedQuotation).ThenInclude(q => q!.Items)
            .Include(r => r.RecommendedSupplier).Include(r => r.MaterialRequest).SingleAsync();
        switch (fault)
        {
            case "missing": rec.RecommendedQuotationId = null; rec.RecommendedQuotation = null; break;
            case "expired": rec.RecommendedQuotation!.ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)); break;
            case "inactive": rec.RecommendedSupplier!.Status = SupplierStatus.Inactive; break;
            case "quantity": rec.RecommendedQuotation!.Items.Single().Quantity = 1; break;
            case "total": rec.RecommendedQuotation!.TotalAmount = 1; break;
            case "request": rec.MaterialRequest!.Status = MaterialRequestStatus.PendingApproval; break;
            case "supplier": rec.RecommendedSupplier = new Supplier { Name = "Different supplier" }; break;
            case "duplicate": db.PurchaseOrders.Add(new PurchaseOrder { QuotationId = 1 }); break;
        }
        await db.SaveChangesAsync();
        int count = await db.PurchaseOrders.CountAsync();
        using var response = await _client.PostAsJsonAsync("/api/Procurement/recommendations/1/approve", new { userId = 999, decision = fault == "decision" ? "AwaitingApproval" : "Approved" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        db.ChangeTracker.Clear();
        rec = await db.ProcurementRecommendations.SingleAsync();
        Assert.Equal(RecommendationStatus.AwaitingApproval, rec.Status);
        Assert.Null(rec.ApprovedByUserId);
        Assert.Equal(count, await db.PurchaseOrders.CountAsync());
    }

    [Fact]
    public async Task Workflow_review_records_JWT_reviewer()
    {
        SignIn("ProcurementManager");
        using var response = await _client.PostAsJsonAsync("/api/procurement-workflow/1/decision", new { decision = "Reject", reviewedByUserId = 999 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _host.Services.CreateScope();
        Assert.Equal(77, (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AgentApprovals.SingleAsync()).ReviewedByUserId);
    }

    [Theory]
    [InlineData("/api/Procurement/recommendations/1/approve", "ProcurementManager")]
    [InlineData("/api/procurement-workflow/1/decision", "ProcurementManager")]
    [InlineData("/api/Deliveries/1/receive", "ReceivingOfficer")]
    [InlineData("/api/Deliveries/1/risk-analysis", "ReceivingOfficer")]
    [InlineData("/api/MaterialRequests/1/plan", "SiteEngineer")]
    public async Task Invalid_claim_cannot_fall_back_to_body_or_query(string path, string role)
    {
        SignIn(role, 0);
        using var response = await _client.PostAsJsonAsync(path + "?userId=77", new { userId = 77, reviewedByUserId = 77, receivedByUserId = 77, decision = "Approved" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Workflow_order_requires_manager_decision()
    {
        SignIn("ProcurementManager");
        using var response = await _client.PostAsJsonAsync("/api/procurement-workflow/1/purchase-order", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Workflow_approval_revalidates_quote_and_commits_atomically(bool expired)
    {
        SignIn("ProcurementManager");
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (expired) { (await db.Quotations.SingleAsync()).ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)); await db.SaveChangesAsync(); }
        using var response = await _client.PostAsJsonAsync("/api/procurement-workflow/1/decision", new { decision = "Approve", reviewedByUserId = 999 });
        Assert.Equal(expired ? HttpStatusCode.BadRequest : HttpStatusCode.OK, response.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(expired ? 0 : 1, await db.PurchaseOrders.CountAsync(p => p.QuotationId == 1));
        Assert.Equal(expired ? 0 : 1, await db.AgentApprovals.CountAsync());
    }

    [Fact]
    public async Task Planning_uses_JWT_actor_instead_of_query()
    {
        using (var setup = _host.Services.CreateScope())
        {
            var setupDb = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            setupDb.AgentWorkflowSteps.RemoveRange(setupDb.AgentWorkflowSteps);
            setupDb.AgentWorkflows.RemoveRange(setupDb.AgentWorkflows);
            await setupDb.SaveChangesAsync();
        }
        SignIn("SiteEngineer");
        using var response = await _client.PostAsJsonAsync("/api/MaterialRequests/1/plan?userId=999", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(77, (await db.AgentWorkflows.OrderByDescending(w => w.Id).FirstAsync()).InitiatedByUserId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Orders_created_on_one_route_block_duplicates_on_the_other(bool alternativeFirst)
    {
        SignIn("ProcurementManager");
        var alternative = "/api/Procurement/recommendations/1/approve";
        var workflow = "/api/procurement-workflow/1/decision";
        using var first = await _client.PostAsJsonAsync(alternativeFirst ? alternative : workflow, new { decision = "Approved" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var second = await _client.PostAsJsonAsync(alternativeFirst ? workflow : alternative, new { decision = "Approved" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        using var scope = _host.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PurchaseOrders.CountAsync(p => p.QuotationId == 1));
    }

    [Fact]
    public async Task Engineer_cannot_submit_another_users_draft()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.MaterialRequests.SingleAsync()).Status = MaterialRequestStatus.Draft;
        await db.SaveChangesAsync();
        SignIn("SiteEngineer");
        using var response = await _client.PostAsJsonAsync("/api/MaterialRequests/1/submit", new { userId = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Project_manager_can_approve_pending_request_only()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.MaterialRequests.SingleAsync()).Status = MaterialRequestStatus.PendingApproval;
        await db.SaveChangesAsync();
        SignIn("ProjectManager");
        using var response = await _client.PostAsJsonAsync("/api/MaterialRequests/1/approve", new { userId = 999, decision = "Approved" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var repeat = await _client.PostAsJsonAsync("/api/MaterialRequests/1/approve", new { decision = "Rejected" });
        Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}
