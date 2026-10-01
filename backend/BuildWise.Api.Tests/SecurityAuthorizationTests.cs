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
public partial class SecurityAuthorizationTests : IAsyncLifetime
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
                services.AddScoped<DeliveryDiscrepancyAgentService>();
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
        db.AgentWorkflows.Add(new AgentWorkflow { MaterialRequestId = scenario.Request.Id,
            Status = WorkflowStatus.AwaitingApproval, Steps = [new AgentWorkflowStep { AgentRole = "QuotationSupplierAnalysisAgent",
                StructuredResult = System.Text.Json.JsonSerializer.Serialize(new BuildWise.Api.DTOs.AgentRecommendationDto(
                    1, 1, "Active supplier", "Best quote", [], [])) }] });
        var receiptOrder = new PurchaseOrder { Supplier = supplier, Status = PurchaseOrderStatus.Confirmed,
            Items = [new PurchaseOrderItem { OrderedQuantity = 10, MaterialId = scenario.Material.Id }] };
        db.Deliveries.Add(new Delivery { PurchaseOrder = receiptOrder,
            Items = [new DeliveryItem { PurchaseOrderItem = receiptOrder.Items.Single() }] });
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
            ("GET", "/api/Deliveries/expected", "ReceivingOfficer"),
            ("GET", "/api/Deliveries/history", "ReceivingOfficer"),
            ("GET", "/api/Deliveries/1", "ReceivingOfficer"),
            ("GET", "/api/Deliveries/schedules", "ReceivingOfficer"),
            ("GET", "/api/Deliveries/issues", "ReceivingOfficer"),
            ("POST", "/api/Deliveries", "SiteEngineer"),
            ("POST", "/api/Deliveries/1/receive", "ProcurementOfficer"),
            ("POST", "/api/Deliveries/1/evidence", "ProcurementOfficer"),
            ("POST", "/api/Deliveries/schedule", "SiteEngineer"),
            ("POST", "/api/Deliveries/report-issue", "ProcurementOfficer"),
            ("POST", "/api/Deliveries/evaluate-risk/1", "SiteEngineer"),
            ("POST", "/api/Deliveries/1/risk-analysis", "SiteEngineer"),
            ("GET", "/api/purchase-orders", "SiteEngineer"),
            ("GET", "/api/material-requests/1/quotations", "SiteEngineer"),
            ("POST", "/api/material-requests/1/quotations", "SiteEngineer"),
            ("GET", "/api/material-requests/1/quotations/compare", "SiteEngineer"),
            ("GET", "/api/material-requests/1/procurement-workflow", "SiteEngineer"),
            ("GET", "/api/procurement-workflow/1", "SiteEngineer"),
            ("GET", "/api/procurement-workflow/1/history", "SiteEngineer"),
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
        SignIn("SiteEngineer");
        using var receipt = await _client.PostAsJsonAsync("/api/Deliveries/1/receive", new { receivedByUserId = 999, items = new[] { new { purchaseOrderItemId = 1, receivedQuantity = 10, damagedQuantity = 0 } } });
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
    public async Task Workflow_approval_records_JWT_and_creates_linked_order_once(string role)
    {
        SignIn(role);
        using var response = await _client.PostAsJsonAsync("/api/procurement-workflow/1/decision", new { reviewedByUserId = 999, decision = "Approved" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(77, (await db.AgentApprovals.SingleAsync()).ReviewedByUserId);
        var po = await db.PurchaseOrders.Include(p => p.Items).SingleAsync(p => p.QuotationId == 1);
        Assert.Equal(500, po.TotalAmount);
        Assert.Equal(1, Assert.Single(po.Items).MaterialId);
        Assert.Equal(po.Id, (await db.AgentWorkflows.SingleAsync()).PurchaseOrderId);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(po.Id, body.RootElement.GetProperty("purchaseOrderId").GetInt32());
        using var repeat = await _client.PostAsJsonAsync("/api/procurement-workflow/1/decision", new { decision = "Approved" });
        Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);
        Assert.Equal(1, await db.AgentApprovals.CountAsync());
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
    public async Task Workflow_approval_rejects_invalid_data_without_writes(string fault)
    {
        SignIn("ProcurementManager");
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var quotation = await db.Quotations.Include(q => q.Items).Include(q => q.Supplier).SingleAsync();
        switch (fault)
        {
            case "missing":
                (await db.AgentWorkflowSteps.SingleAsync()).StructuredResult = "{}";
                break;
            case "expired": quotation.ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)); break;
            case "inactive": quotation.Supplier.Status = SupplierStatus.Inactive; break;
            case "quantity": quotation.Items.Single().Quantity = 249; break;
            case "total": quotation.TotalAmount = 501; break;
            case "request": quotation.MaterialRequestId = 999; break;
            case "supplier": quotation.SupplierId = 999; quotation.Supplier = null!; break;
            case "duplicate":
                db.PurchaseOrders.Add(new PurchaseOrder { QuotationId = quotation.Id, SupplierId = quotation.SupplierId,
                    OrderDate = DateOnly.FromDateTime(DateTime.UtcNow), TotalAmount = 500 });
                break;
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var count = await db.PurchaseOrders.CountAsync();
        using var response = await _client.PostAsJsonAsync("/api/procurement-workflow/1/decision",
            new { decision = fault == "decision" ? "Invalid" : "Approved" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(WorkflowStatus.AwaitingApproval, (await db.AgentWorkflows.SingleAsync()).Status);
        Assert.Empty(await db.AgentApprovals.ToListAsync());
        Assert.Equal(count, await db.PurchaseOrders.CountAsync());
    }

    [Theory]
    [InlineData("Reject")]
    [InlineData("RevisionRequested")]
    public async Task Workflow_review_records_JWT_reviewer(string decision)
    {
        SignIn("ProcurementManager");
        using var response = await _client.PostAsJsonAsync("/api/procurement-workflow/1/decision", new { decision, reviewedByUserId = 999 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _host.Services.CreateScope();
        Assert.Equal(77, (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AgentApprovals.SingleAsync()).ReviewedByUserId);
    }

    [Theory]
    [InlineData("/api/procurement-workflow/1/decision", "ProcurementManager")]
    [InlineData("/api/Deliveries/1/receive", "SiteEngineer")]
    [InlineData("/api/Deliveries/1/risk-analysis", "ProcurementOfficer")]
    [InlineData("/api/MaterialRequests/1/plan", "SiteEngineer")]
    public async Task Invalid_claim_cannot_fall_back_to_body_or_query(string path, string role)
    {
        SignIn(role, 0);
        using var response = await _client.PostAsJsonAsync(path + "?userId=77", new { userId = 77, reviewedByUserId = 77, receivedByUserId = 77, decision = "Approved", items = new[] { new { purchaseOrderItemId = 1, receivedQuantity = 1 } } });
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
    public async Task Workflow_order_blocks_repeat_and_second_workflow(bool anotherWorkflow)
    {
        SignIn("ProcurementManager");
        var secondId = 1;
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (anotherWorkflow)
        {
            var original = await db.AgentWorkflows.Include(w => w.Steps).SingleAsync();
            var second = new AgentWorkflow { MaterialRequestId = original.MaterialRequestId,
                Status = WorkflowStatus.AwaitingApproval, Steps = [new AgentWorkflowStep {
                    AgentRole = "QuotationSupplierAnalysisAgent", StructuredResult = original.Steps.Single().StructuredResult }] };
            db.AgentWorkflows.Add(second);
            await db.SaveChangesAsync();
            secondId = second.Id;
        }
        using var first = await _client.PostAsJsonAsync("/api/procurement-workflow/1/decision", new { decision = "Approved" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var repeatedDecision = await _client.PostAsJsonAsync($"/api/procurement-workflow/{secondId}/decision", new { decision = "Approved" });
        Assert.Equal(HttpStatusCode.BadRequest, repeatedDecision.StatusCode);
        using var repeatedCreation = await _client.PostAsJsonAsync("/api/procurement-workflow/1/purchase-order", new { });
        Assert.Equal(HttpStatusCode.BadRequest, repeatedCreation.StatusCode);
        Assert.Equal(1, await db.PurchaseOrders.CountAsync(p => p.QuotationId == 1));
        Assert.Equal(1, await db.AgentApprovals.CountAsync());
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
    public async Task Procurement_manager_can_approve_pending_request_only()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.MaterialRequests.SingleAsync()).Status = MaterialRequestStatus.PendingApproval;
        await db.SaveChangesAsync();
        SignIn("ProcurementManager");
        using var response = await _client.PostAsJsonAsync("/api/MaterialRequests/1/approve", new { userId = 999, decision = "Approved" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var repeat = await _client.PostAsJsonAsync("/api/MaterialRequests/1/approve", new { decision = "Rejected" });
        Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);
    }

    [Fact]
    public async Task Site_request_manager_decision_and_site_status_share_the_same_record()
    {
        SignIn("SiteEngineer");
        using var optionsResponse = await _client.GetAsync("/api/materialrequests/options");
        Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        using var options = System.Text.Json.JsonDocument.Parse(await optionsResponse.Content.ReadAsStringAsync());
        var projectId = options.RootElement.GetProperty("projects")[0].GetProperty("id").GetInt32();
        var material = options.RootElement.GetProperty("materials")[0];
        using var created = await _client.PostAsJsonAsync("/api/materialrequests", new {
            projectId, reason = "Cross-platform request", items = new[] {
                new { materialId = material.GetProperty("id").GetInt32(), unit = material.GetProperty("unit").GetString(), quantity = 12 } } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetInt32();
        Assert.Equal("PendingApproval", body.RootElement.GetProperty("status").GetString());
        SignIn("ProcurementManager");
        using var approval = await _client.PostAsJsonAsync($"/api/materialrequests/{id}/approve", new { decision = "Approved" });
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        SignIn("SiteEngineer");
        using var detail = System.Text.Json.JsonDocument.Parse(await _client.GetStringAsync($"/api/materialrequests/{id}"));
        Assert.Equal("Approved", detail.RootElement.GetProperty("status").GetString());
        Assert.Equal(77, detail.RootElement.GetProperty("requestedByUserId").GetInt32());
    }

    [Theory]
    [InlineData("SiteEngineer", "/api/suppliers", "POST")]
    [InlineData("SiteEngineer", "/api/material-requests/1/quotations", "POST")]
    [InlineData("SiteEngineer", "/api/purchase-orders", "GET")]
    [InlineData("SiteEngineer", "/api/purchase-orders/1", "GET")]
    [InlineData("SiteEngineer", "/api/purchase-orders/1/status", "PATCH")]
    [InlineData("SiteEngineer", "/api/procurement-workflow/1/decision", "POST")]
    [InlineData("SiteEngineer", "/api/procurement-workflow/1/purchase-order", "POST")]
    [InlineData("ProcurementOfficer", "/api/MaterialRequests/1/approve", "POST")]
    [InlineData("ProcurementOfficer", "/api/MaterialRequests/1/plan", "POST")]
    [InlineData("QualityInspector", "/api/suppliers", "GET")]
    [InlineData("QualityInspector", "/api/procurement-workflow/1/decision", "POST")]
    [InlineData("QualityInspector", "/api/purchase-orders", "GET")]
    [InlineData("ProjectManager", "/api/MaterialRequests/1/approve", "POST")]
    [InlineData("ProjectManager", "/api/MaterialRequests", "GET")]
    [InlineData("ReceivingOfficer", "/api/Deliveries/1/receive", "POST")]
    [InlineData("ReceivingOfficer", "/api/purchase-orders", "GET")]
    public async Task Five_role_boundaries_reject_unauthorized_actions(string role, string path, string method)
    {
        SignIn(role);
        using var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
            { Content = JsonContent.Create(new { }) });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("SiteEngineer")]
    [InlineData("ProcurementOfficer")]
    [InlineData("ProcurementManager")]
    [InlineData("QualityInspector")]
    [InlineData("Administrator")]
    public async Task Delivery_context_is_available_without_sensitive_navigation_properties(string role)
    {
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.DeliverySchedules.Add(new DeliverySchedule { PurchaseOrderId = 1 });
            db.DeliveryIssues.Add(new DeliveryIssue { DeliveryId = 1, ReportedByUserId = 77 });
            await db.SaveChangesAsync();
        }
        SignIn(role);
        foreach (var path in new[] { "expected", "history", "1", "schedules", "issues", "1/discrepancy-history" })
        {
            using var response = await _client.GetAsync("/api/Deliveries/" + path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            foreach (var forbidden in new[] { "unitPrice", "totalAmount", "quotationId", "passwordHash", "userRoles" })
                Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Engineer_can_submit_own_draft_and_record_damage_and_evidence()
    {
        SignIn("SiteEngineer");
        using var created = await _client.PostAsJsonAsync("/api/MaterialRequests", new {
            projectId = 1, reason = "Draft", submitImmediately = false,
            items = new[] { new { materialId = 1, quantity = 1, unit = "Bags" } } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetInt32();
        using var submitted = await _client.PostAsJsonAsync($"/api/MaterialRequests/{id}/submit", new { });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        using var receipt = await _client.PostAsJsonAsync("/api/Deliveries/1/receive", new {
            items = new[] { new { purchaseOrderItemId = 1, receivedQuantity = 10, damagedQuantity = 2 } } });
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        using var evidence = await _client.PostAsJsonAsync("/api/Deliveries/1/evidence", new { imageUrl = "https://example.test/damage.jpg" });
        Assert.Equal(HttpStatusCode.OK, evidence.StatusCode);
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, (await db.DeliveryItems.SingleAsync()).DamagedQuantity);
        Assert.Equal(DeliveryStatus.DiscrepancyReported, (await db.Deliveries.SingleAsync()).Status);
    }

    [Fact]
    public async Task Repeat_receiving_context_uses_remaining_usable_quantity()
    {
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Deliveries.Add(new Delivery { PurchaseOrderId = 1, Status = DeliveryStatus.DiscrepancyReported,
                Items = [new DeliveryItem { PurchaseOrderItemId = 1, ReceivedQuantity = 7, DamagedQuantity = 2 }] });
            await db.SaveChangesAsync();
        }
        SignIn("SiteEngineer");
        using var expected = System.Text.Json.JsonDocument.Parse(await _client.GetStringAsync("/api/Deliveries/expected"));
        var item = expected.RootElement[0].GetProperty("items")[0];
        Assert.Equal(5, item.GetProperty("previouslyReceivedUsableQuantity").GetDecimal());
        Assert.Equal(5, item.GetProperty("outstandingQuantity").GetDecimal());
        using var detail = System.Text.Json.JsonDocument.Parse(await _client.GetStringAsync("/api/Deliveries/1"));
        Assert.Equal(5, detail.RootElement.GetProperty("items")[0].GetProperty("outstandingQuantity").GetDecimal());
    }

    [Theory]
    [InlineData("ProcurementOfficer")]
    [InlineData("ProcurementManager")]
    [InlineData("Administrator")]
    public async Task Procurement_roles_can_record_suppliers_and_quotations(string role)
    {
        SignIn(role);
        using var supplier = await _client.PostAsJsonAsync("/api/suppliers", new { name = "New supplier" });
        Assert.Equal(HttpStatusCode.Created, supplier.StatusCode);
        using var quotation = await _client.PostAsJsonAsync("/api/material-requests/1/quotations", new {
            supplierId = 1, quotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            items = new[] { new { materialRequestItemId = 1, quantity = 250, unitPrice = 2 } } });
        Assert.Equal(HttpStatusCode.OK, quotation.StatusCode);
        using var scope = _host.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Quotations.CountAsync());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/purchase-orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/MaterialRequests/1")).StatusCode);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}
