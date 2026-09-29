using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BuildWise.Api.Controllers;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace BuildWise.Api.Tests;

public class ProcurementPostgresTheoryAttribute : TheoryAttribute
{
    public ProcurementPostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BUILDWISE_TEST_POSTGRES")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BUILDWISE_TEST_AGENT_URL")))
            Skip = "Requires BUILDWISE_TEST_POSTGRES and a running quotation agent at BUILDWISE_TEST_AGENT_URL.";
    }
}
public class ProcurementLiveClientsTheoryAttribute : ProcurementPostgresTheoryAttribute
{
    public ProcurementLiveClientsTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BUILDWISE_LIVE_DIR")))
            Skip = "Opt-in live React/Flutter verification host (BUILDWISE_LIVE_DIR).";
    }
}

// Real HTTP, real JWT/login, PostgreSQL and the internal Python agent. The only
// substituted integration is email, to avoid sending test notifications.
public class ProcurementIntegrationTests : IAsyncLifetime
{
    private readonly string _database = "buildwise_procurement_test_" + Guid.NewGuid().ToString("N");
    private string _adminConnection = null!;
    private string _connection = null!;
    private bool _created;
    private IHost? _host;
    private Uri _base = null!;
    private readonly List<HttpClient> _clients = [];
    private ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_connection).Options);

    public async Task InitializeAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("BUILDWISE_TEST_POSTGRES"))
            { Database = "postgres", Pooling = false };
        _adminConnection = builder.ConnectionString;
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using (var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin)) await cmd.ExecuteNonQueryAsync();
        _created = true;
        builder.Database = _database;
        _connection = builder.ConnectionString;
        await using (var db = Db())
        {
            await db.Database.EnsureCreatedAsync();
            await DbSeeder.SeedAsync(db);
        }
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Jwt:Key"] = key, ["Jwt:Issuer"] = "ProcurementIntegration", ["Jwt:Audience"] = "ProcurementIntegration"
        }).Build();
        _host = await new HostBuilder().ConfigureWebHost(web => web.UseKestrel().UseUrls("http://127.0.0.1:0")
            .ConfigureServices(services => {
                services.AddSingleton<IConfiguration>(config);
                services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(_connection));
                services.AddSingleton<JwtTokenService>(); services.AddScoped<AuthService>();
                services.AddScoped<ProcurementWorkflowService>(); services.AddScoped<ProcurementValidationService>();
                services.AddScoped<ProcurementPlanningAgentService>();
                services.AddScoped<DeliveryDiscrepancyAgentService>();
                services.AddScoped<DeliveryRiskAgentService>(); services.AddSingleton<IEmailService, NoOpEmailService>();
                services.AddHttpClient<QuotationAgentClient>(c => c.BaseAddress = new Uri(Environment.GetEnvironmentVariable("BUILDWISE_TEST_AGENT_URL")!));
                services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly).AddJsonOptions(o => {
                    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                    o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                });
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters {
                    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                    ValidIssuer = "ProcurementIntegration", ValidAudience = "ProcurementIntegration", IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)) });
                services.AddAuthorization(); services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
            }).Configure(app => {
                app.UseRouting(); app.UseCors(); app.UseAuthentication(); app.UseAuthorization();
                app.UseEndpoints(e => e.MapControllers());
            })).StartAsync();
        _base = new Uri(_host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
    }
    private async Task<HttpClient> Login(string email)
    {
        var client = new HttpClient { BaseAddress = _base }; _clients.Add(client);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DbSeeder.DemoPassword });
        var data = await Json(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.GetProperty("token").GetString());
        return client;
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    private async Task<int> NewRequest(HttpClient site)
    {
        var options = await Json(await site.GetAsync("/api/MaterialRequests/options"));
        var material = options.GetProperty("materials")[0];
        var result = await Json(await site.PostAsJsonAsync("/api/MaterialRequests", new {
            projectId = options.GetProperty("projects")[0].GetProperty("id").GetInt32(),
            requiredDate = DateTime.UtcNow.AddDays(14), reason = "Phase 4 integrated request", submitImmediately = true,
            items = new[] { new { materialId = material.GetProperty("id").GetInt32(), unit = material.GetProperty("unit").GetString(), quantity = 250 } }
        }));
        return result.GetProperty("id").GetInt32();
    }
    private async Task<(int WorkflowId, int QuoteId)> Prepare(HttpClient site, HttpClient officer, int requestId)
    {
        var pm = await Login("procurement.manager@buildwise.demo");
        Assert.Equal(HttpStatusCode.OK, (await pm.PostAsJsonAsync($"/api/MaterialRequests/{requestId}/approve", new { decision = "Approved" })).StatusCode);
        var queue = await Json(await officer.GetAsync("/api/MaterialRequests?status=Approved"));
        Assert.Contains(queue.EnumerateArray(), r => r.GetProperty("id").GetInt32() == requestId);
        var detail = await Json(await officer.GetAsync($"/api/MaterialRequests/{requestId}"));
        var lineId = detail.GetProperty("items")[0].GetProperty("id").GetInt32();
        var supplierData = await Json(await officer.GetAsync("/api/suppliers"));
        int winnerQuote = 0;
        foreach (var supplier in supplierData.GetProperty("items").EnumerateArray())
        {
            var name = supplier.GetProperty("name").GetString()!;
            var qty = name.StartsWith("Supplier C") ? 200 : 250;
            var price = name.StartsWith("Supplier A") ? 2100 : name.StartsWith("Supplier B") ? 2040 : 2050;
            var quote = await Json(await officer.PostAsJsonAsync($"/api/material-requests/{requestId}/quotations", new {
                supplierId = supplier.GetProperty("id").GetInt32(), quotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)), totalAmount = 1,
                items = new[] { new { materialRequestItemId = lineId, quantity = qty, unitPrice = price } }
            }));
            Assert.Equal(qty * price, quote.GetProperty("totalAmount").GetDecimal());
            if (name.StartsWith("Supplier A")) winnerQuote = quote.GetProperty("id").GetInt32();
        }
        Assert.Equal("QuotationsInProgress", (await Json(await site.GetAsync($"/api/material-requests/{requestId}/procurement-status"))).GetProperty("status").GetString());
        var started = await Json(await officer.PostAsJsonAsync($"/api/material-requests/{requestId}/procurement-workflow", new { objective = "Select the eligible full-coverage cement quotation", initiatedByUserId = 999 }));
        Assert.Equal("AwaitingApproval", started.GetProperty("status").GetString());
        var workflowId = started.GetProperty("workflowId").GetInt32();
        var workflow = await Json(await officer.GetAsync($"/api/procurement-workflow/{workflowId}"));
        var rec = workflow.GetProperty("recommendation");
        Assert.Equal(winnerQuote, rec.GetProperty("recommendedQuotationId").GetInt32());
        Assert.Single(rec.GetProperty("rankedAlternatives").EnumerateArray());
        Assert.Contains("Suspended", rec.GetProperty("warnings").ToString());
        Assert.Contains("200", rec.GetProperty("warnings").ToString());
        Assert.Contains(rec.GetProperty("executionMode").GetString(), new[] { "PythonDeterministicFallback", "ProviderBacked" });
        Assert.Equal(2, rec.GetProperty("toolsUsed").GetArrayLength());
        Assert.True(workflow.GetProperty("validation").GetProperty("isValid").GetBoolean());
        Assert.Equal(3, workflow.GetProperty("steps").GetArrayLength());
        Assert.Equal(workflowId, (await Json(await officer.GetAsync($"/api/material-requests/{requestId}/procurement-workflow"))).GetProperty("id").GetInt32());
        return (workflowId, winnerQuote);
    }

    [ProcurementPostgresTheory]
    [InlineData("Approve")]
    [InlineData("Reject")]
    [InlineData("RevisionRequested")]
    public async Task Component1_to_procurement_to_site_status(string decision)
    {
        var site = await Login("site.engineer@buildwise.demo");
        var officer = await Login("procurement.officer@buildwise.demo");
        var manager = await Login("procurement.manager@buildwise.demo");
        var id = await NewRequest(site);
        Assert.Equal(HttpStatusCode.BadRequest, (await officer.PostAsJsonAsync($"/api/material-requests/{id}/procurement-workflow", new {})).StatusCode);
        var (workflowId, _) = await Prepare(site, officer, id);
        Assert.Equal(HttpStatusCode.Forbidden, (await officer.PostAsJsonAsync($"/api/procurement-workflow/{workflowId}/decision", new { decision })).StatusCode);
        var result = await Json(await manager.PostAsJsonAsync($"/api/procurement-workflow/{workflowId}/decision", new { decision, comment = "Reviewed", reviewedByUserId = 999 }));
        await using var db = Db();
        var reviewer = await db.Users.SingleAsync(u => u.Email == "procurement.manager@buildwise.demo");
        Assert.Equal(reviewer.Id, (await db.AgentApprovals.SingleAsync(a => a.AgentWorkflowId == workflowId)).ReviewedByUserId);
        var status = await Json(await site.GetAsync($"/api/material-requests/{id}/procurement-status"));
        Assert.Equal(decision == "Approve" ? "PurchaseOrderCreated" : decision == "Reject" ? "Rejected" : "RevisionRequested", status.GetProperty("status").GetString());
        Assert.Equal(4, status.EnumerateObject().Count());
        Assert.DoesNotContain("supplier", status.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("price", status.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync($"/api/procurement-workflow/{workflowId}/decision", new { decision })).StatusCode);
        if (decision != "Approve") { Assert.Empty(await db.PurchaseOrders.ToListAsync()); return; }
        var poId = result.GetProperty("purchaseOrderId").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync($"/api/procurement-workflow/{workflowId}/purchase-order", new {})).StatusCode);
        Assert.Equal(1, await db.PurchaseOrders.CountAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await site.GetAsync("/api/purchase-orders")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await officer.PatchAsJsonAsync($"/api/purchase-orders/{poId}/status", new { status = "Completed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await officer.PatchAsJsonAsync($"/api/purchase-orders/{poId}/status", new { status = "Confirmed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await site.GetAsync($"/api/purchase-orders/{poId}")).StatusCode);
        var visible = await Json(await manager.GetAsync($"/api/purchase-orders/{poId}"));
        Assert.Equal("Confirmed", visible.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await officer.PatchAsJsonAsync($"/api/purchase-orders/{poId}/status", new { status = "Cancelled" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync($"/api/procurement-workflow/{workflowId}/purchase-order", new {})).StatusCode);
        Assert.Equal(1, await db.PurchaseOrders.CountAsync());
    }

    [ProcurementPostgresTheory]
    [InlineData(0)]
    public async Task Site_receives_procurement_order_using_only_delivery_context(int _)
    {
        var site = await Login("site.engineer@buildwise.demo");
        var officer = await Login("procurement.officer@buildwise.demo");
        var manager = await Login("procurement.manager@buildwise.demo");
        var requestId = await NewRequest(site);
        var (workflow, _) = await Prepare(site, officer, requestId);
        var approved = await Json(await manager.PostAsJsonAsync($"/api/procurement-workflow/{workflow}/decision", new { decision = "Approve" }));
        var poId = approved.GetProperty("purchaseOrderId").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await officer.PatchAsJsonAsync($"/api/purchase-orders/{poId}/status", new { status = "Confirmed" })).StatusCode);
        var scheduled = await Json(await officer.PostAsJsonAsync("/api/Deliveries", new { purchaseOrderId = poId, deliveryReference = "RBAC-RECEIPT" }));
        var id = scheduled.GetProperty("id").GetInt32();
        var expected = await Json(await site.GetAsync("/api/Deliveries/expected"));
        var delivery = Assert.Single(expected.EnumerateArray());
        Assert.Equal(id, delivery.GetProperty("id").GetInt32());
        Assert.DoesNotContain("unitPrice", delivery.ToString());
        Assert.DoesNotContain("quotationId", delivery.ToString());
        var items = delivery.GetProperty("items").EnumerateArray().Select(i => new {
            purchaseOrderItemId = i.GetProperty("purchaseOrderItemId").GetInt32(),
            receivedQuantity = i.GetProperty("outstandingQuantity").GetDecimal(), damagedQuantity = 0
        }).ToArray();
        Assert.Equal(HttpStatusCode.OK, (await site.PostAsJsonAsync($"/api/Deliveries/{id}/receive", new { items })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await site.GetAsync($"/api/purchase-orders/{poId}")).StatusCode);
        await using var db = Db();
        Assert.Equal(PurchaseOrderStatus.Completed, (await db.PurchaseOrders.SingleAsync()).Status);
    }

    [ProcurementPostgresTheory]
    [InlineData("expired")]
    [InlineData("partial")]
    [InlineData("suspended")]
    public async Task Approval_revalidates_persisted_quotation(string fault)
    {
        var site = await Login("site.engineer@buildwise.demo");
        var officer = await Login("procurement.officer@buildwise.demo");
        var manager = await Login("procurement.manager@buildwise.demo");
        var id = await NewRequest(site);
        var (workflow, quote) = await Prepare(site, officer, id);
        await using (var db = Db()) {
            var q = await db.Quotations.Include(q => q.Items).Include(q => q.Supplier).SingleAsync(q => q.Id == quote);
            if (fault == "expired") q.ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            if (fault == "partial") q.Items.First().Quantity = 1;
            if (fault == "suspended") q.Supplier.Status = SupplierStatus.Suspended;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync($"/api/procurement-workflow/{workflow}/decision", new { decision = "Approve" })).StatusCode);
        await using var verify = Db();
        Assert.Empty(await verify.PurchaseOrders.ToListAsync());
        Assert.Empty(await verify.AgentApprovals.ToListAsync());
    }

    [ProcurementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_workflow_approvals_create_at_most_one_order(bool anotherWorkflow)
    {
        var site = await Login("site.engineer@buildwise.demo");
        var officer = await Login("procurement.officer@buildwise.demo");
        var manager = await Login("procurement.manager@buildwise.demo");
        var id = await NewRequest(site);
        var (workflow, quote) = await Prepare(site, officer, id);
        var secondWorkflow = workflow;
        if (anotherWorkflow)
        {
            await using var db = Db();
            var original = await db.AgentWorkflows.Include(w => w.Steps).SingleAsync(w => w.Id == workflow);
            var second = new AgentWorkflow { MaterialRequestId = id, InitiatedByUserId = original.InitiatedByUserId,
                Status = WorkflowStatus.AwaitingApproval, Steps = [new AgentWorkflowStep {
                    AgentRole = "QuotationSupplierAnalysisAgent",
                    StructuredResult = original.Steps.Single(s => s.AgentRole == "QuotationSupplierAnalysisAgent").StructuredResult }] };
            db.AgentWorkflows.Add(second);
            await db.SaveChangesAsync();
            secondWorkflow = second.Id;
        }
        var paths = new[] { $"/api/procurement-workflow/{workflow}/decision", $"/api/procurement-workflow/{secondWorkflow}/decision" };
        var responses = await Task.WhenAll(paths.Select(path => manager.PostAsJsonAsync(path, new { decision = "Approved" })));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        await using var verify = Db(); Assert.Equal(1, await verify.PurchaseOrders.CountAsync());
    }

    [ProcurementPostgresTheory]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("negative")]
    [InlineData("validity")]
    [InlineData("unapproved")]
    public async Task Quotation_input_rejects_invalid_data(string fault)
    {
        var site = await Login("site.engineer@buildwise.demo");
        var officer = await Login("procurement.officer@buildwise.demo");
        var id = await NewRequest(site);
        if (fault != "unapproved") {
            var pm = await Login("procurement.manager@buildwise.demo");
            Assert.Equal(HttpStatusCode.OK, (await pm.PostAsJsonAsync($"/api/MaterialRequests/{id}/approve", new { decision = "Approved" })).StatusCode);
        }
        var detail = await Json(await officer.GetAsync($"/api/MaterialRequests/{id}"));
        var lineId = detail.GetProperty("items")[0].GetProperty("id").GetInt32();
        var suppliers = await Json(await officer.GetAsync("/api/suppliers"));
        var items = new List<object> { new { materialRequestItemId = fault == "foreign" ? int.MaxValue : lineId,
            quantity = fault == "negative" ? -1 : 250, unitPrice = 2 } };
        if (fault == "duplicate") items.Add(items[0]);
        var response = await officer.PostAsJsonAsync($"/api/material-requests/{id}/quotations", new {
            supplierId = suppliers.GetProperty("items")[0].GetProperty("id").GetInt32(),
            quotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(fault == "validity" ? -1 : 15)), items });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = Db(); Assert.Empty(await db.Quotations.ToListAsync());
    }

    [ProcurementPostgresTheory]
    [InlineData(true)]
    public async Task Site_status_is_owner_scoped_and_read_only(bool _)
    {
        var site = await Login("site.engineer@buildwise.demo");
        var id = await NewRequest(site);
        using var anonymous = new HttpClient { BaseAddress = _base };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/material-requests/{id}/procurement-status")).StatusCode);
        var registered = await Json(await anonymous.PostAsJsonAsync("/api/auth/register", new {
            fullName = "Other site", email = "other@test.example", password = "Password123!", roleName = "SiteEngineer" }));
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync($"/api/material-requests/{id}/procurement-status")).StatusCode);
        var status = await Json(await site.GetAsync($"/api/material-requests/{id}/procurement-status"));
        Assert.Equal("NotStarted", status.GetProperty("status").GetString());
        Assert.Equal(4, status.EnumerateObject().Count());
    }

    [ProcurementLiveClientsTheory]
    [InlineData(true)]
    public async Task Live_client_verification_host(bool _)
    {
        var directory = Environment.GetEnvironmentVariable("BUILDWISE_LIVE_DIR")!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "host.json"), JsonSerializer.Serialize(new { apiBaseUrl = _base.ToString().TrimEnd('/') + "/api" }));
        var deadline = DateTime.UtcNow.AddMinutes(45);
        while (!File.Exists(Path.Combine(directory, "complete")) && DateTime.UtcNow < deadline)
            await Task.Delay(500);
        Assert.True(File.Exists(Path.Combine(directory, "complete")), "Live client verification did not signal completion.");
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients) client.Dispose();
        if (_host != null) { await _host.StopAsync(); _host.Dispose(); }
        if (!_created) return;
        await using var admin = new NpgsqlConnection(_adminConnection); await admin.OpenAsync();
        // Dropping only this fixture's database must not require terminating other
        // PostgreSQL processes (for example an autovacuum worker).
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var cmd = new NpgsqlCommand($"DROP DATABASE \"{_database}\"", admin);
                await cmd.ExecuteNonQueryAsync();
                break;
            }
            catch (PostgresException ex) when (ex.SqlState == "55006" && attempt < 4)
            {
                await Task.Delay(250);
            }
        }
    }
}
