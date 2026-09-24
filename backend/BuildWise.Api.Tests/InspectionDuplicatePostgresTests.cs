using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BuildWise.Api.Controllers;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace BuildWise.Api.Tests;

// Opt in with BUILDWISE_TEST_POSTGRES (credentials with CREATEDB permission).
// Its Database is ignored: only postgres and a newly generated database are opened.
// Never runs Program.cs, migrations, seeders, or the running API.
public sealed class InspectionPostgresTheoryAttribute : TheoryAttribute
{
    public InspectionPostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BUILDWISE_TEST_POSTGRES")))
            Skip = "Set BUILDWISE_TEST_POSTGRES to run isolated PostgreSQL integration tests.";
    }
}

public class InspectionDuplicatePostgresTests : IAsyncLifetime
{
    private readonly string _database = "buildwise_inspection_test_" + Guid.NewGuid().ToString("N");
    private string _adminConnection = null!;
    private string _connection = null!;
    private bool _created;
    private IHost? _host;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("BUILDWISE_TEST_POSTGRES"))
        {
            Database = "postgres", Pooling = false, Timeout = 10, CommandTimeout = 15
        };
        _adminConnection = builder.ConnectionString;
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin);
        await create.ExecuteNonQueryAsync();
        _created = true;
        try
        {
            builder.Database = _database;
            builder.ApplicationName = _database;
            _connection = builder.ConnectionString;
            await using var db = CreateDb();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new User { Id = 7, FullName = "Test inspector", Email = "inspector@example.test" });
            await db.SaveChangesAsync();

            var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = key, ["Jwt:Issuer"] = "InspectionTests", ["Jwt:Audience"] = "InspectionTests"
            }).Build();
            _host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(_connection));
                    services.AddScoped<QualityInspectionService>();
                    services.AddScoped<NonConformanceService>();
                    services.AddControllers().AddApplicationPart(typeof(InspectionsController).Assembly);
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true, ValidateAudience = true,
                            ValidateLifetime = true, ValidateIssuerSigningKey = true,
                            ValidIssuer = "InspectionTests", ValidAudience = "InspectionTests",
                            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
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
            _client.Timeout = TimeSpan.FromSeconds(30);
            var token = new JwtTokenService(configuration).GenerateToken(
                new User { Id = 7, FullName = "Test inspector", Email = "inspector@example.test" },
                ["QualityInspector"]).Token;
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(_connection).Options);

    private async Task<Delivery> SeedDelivery(DeliveryStatus status)
    {
        await using var db = CreateDb();
        var order = new PurchaseOrder { OrderDate = new DateOnly(2026, 1, 1) };
        var delivery = new Delivery
        {
            Status = status, PurchaseOrder = order,
            Items = [new DeliveryItem { ReceivedQuantity = 10,
                PurchaseOrderItem = new PurchaseOrderItem { PurchaseOrder = order, OrderedQuantity = 10 } }]
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }

    private Task<HttpResponseMessage> Start(int deliveryId) =>
        _client.PostAsJsonAsync("/api/inspections", new StartInspectionDto { DeliveryId = deliveryId });

    [InspectionPostgresTheory]
    [InlineData(DeliveryStatus.Received)]
    [InlineData(DeliveryStatus.DiscrepancyReported)]
    public async Task History_NCR_lifecycle_and_quotation_evidence_use_real_PostgreSQL(DeliveryStatus status)
    {
        var delivery = await SeedDelivery(status);
        await using (var db = CreateDb())
        {
            var scenario = await TestDbFactory.SeedStandardScenarioDataAsync(db);
            var quotation = new Quotation { MaterialRequestId = scenario.Request.Id,
                Supplier = new Supplier { Name = "Canonical supplier" },
                Items = [new QuotationItem { MaterialRequestItemId = scenario.RequestItem.Id, Quantity = 10 }] };
            db.Quotations.Add(quotation);
            await db.SaveChangesAsync();
            var order = await db.PurchaseOrders.Include(o => o.Items).SingleAsync();
            order.QuotationId = quotation.Id;
            order.Items.Single().QuotationItemId = quotation.Items.Single().Id;
            await db.SaveChangesAsync();
        }
        using var started = await Start(delivery.Id);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var inspection = (await started.Content.ReadFromJsonAsync<QualityInspectionResponseDto>())!;
        using var completed = await _client.PostAsJsonAsync($"/api/inspections/{inspection.Id}/complete",
            new CompleteInspectionDto { OverallDecision = InspectionDecision.PartiallyAccepted,
                Items = [new CompleteInspectionItemDto { DeliveryItemId = delivery.Items.Single().Id,
                    AcceptedQuantity = 8, RejectedQuantity = 2, Condition = "Damaged" }] });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var detail = (await completed.Content.ReadFromJsonAsync<QualityInspectionResponseDto>())!;
        var history = (await _client.GetFromJsonAsync<List<InspectionHistoryDto>>("/api/inspections"))!;
        Assert.Equal(inspection.Id, Assert.Single(history).Id);
        Assert.Equal("Test inspector", history[0].InspectorName);
        Assert.Equal(InspectionDecision.PartiallyAccepted, history[0].OverallDecision);
        await using (var db = CreateDb())
        {
            var evidence = await new QualityRiskEvidenceService(db).CollectAsync(inspection.Id, default);
            Assert.Equal("Canonical supplier", evidence.SupplierName);
            Assert.Equal("Portland Composite Cement (50kg)", Assert.Single(evidence.Items).MaterialName);
            Assert.Empty(evidence.History);
        }
        using var created = await _client.PostAsJsonAsync("/api/non-conformances", new CreateNonConformanceDto {
            InspectionItemId = detail.Items.Single().Id, IssueDescription = "Damaged material", Severity = NonConformanceSeverity.High });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ncr = (await created.Content.ReadFromJsonAsync<NonConformanceResponseDto>())!;
        Assert.Equal(NonConformanceStatus.Open, ncr.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync($"/api/non-conformances/{ncr.Id}/resolve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync($"/api/non-conformances/{ncr.Id}/close", null)).StatusCode);
        using var edited = await _client.PatchAsJsonAsync($"/api/non-conformances/{ncr.Id}/corrective-action",
            new UpdateCorrectiveActionDto { CorrectiveAction = "Replace rejected material" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(NonConformanceStatus.CorrectiveActionRequired, (await edited.Content.ReadFromJsonAsync<NonConformanceResponseDto>())!.Status);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/non-conformances/{ncr.Id}/resolve", null)).StatusCode);
        var resolved = (await _client.GetFromJsonAsync<NonConformanceResponseDto>($"/api/non-conformances/{ncr.Id}"))!;
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/non-conformances/{ncr.Id}/close", null)).StatusCode);
        var closed = (await _client.GetFromJsonAsync<NonConformanceResponseDto>($"/api/non-conformances/{ncr.Id}"))!;
        Assert.Equal(NonConformanceStatus.Closed, closed.Status);
        Assert.Equal(resolved.ResolvedAt, closed.ResolvedAt);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PatchAsJsonAsync($"/api/non-conformances/{ncr.Id}/corrective-action",
            new UpdateCorrectiveActionDto { CorrectiveAction = "Too late" })).StatusCode);
        Assert.Equal(ncr.Id, Assert.Single((await _client.GetFromJsonAsync<List<NonConformanceResponseDto>>("/api/non-conformances"))!).Id);
    }

    [InspectionPostgresTheory]
    [InlineData(DeliveryStatus.Received)]
    [InlineData(DeliveryStatus.DiscrepancyReported)]
    public async Task Completed_delivery_is_not_pending_and_cannot_be_inspected_again(DeliveryStatus status)
    {
        var delivery = await SeedDelivery(status);
        var available = await SeedDelivery(status);
        using var started = await Start(delivery.Id);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var inspection = (await started.Content.ReadFromJsonAsync<QualityInspectionResponseDto>())!;
        using var completed = await _client.PostAsJsonAsync($"/api/inspections/{inspection.Id}/complete",
            new CompleteInspectionDto { OverallDecision = InspectionDecision.Accepted,
                Items = [new CompleteInspectionItemDto { DeliveryItemId = delivery.Items.Single().Id,
                    AcceptedQuantity = 10, Condition = "Good" }] });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(InspectionStatus.Completed,
            (await completed.Content.ReadFromJsonAsync<QualityInspectionResponseDto>())!.Status);

        using var pendingResponse = await _client.GetAsync("/api/inspections/pending-deliveries");
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        var pending = (await pendingResponse.Content.ReadFromJsonAsync<List<PendingInspectionDeliveryDto>>())!;
        Assert.DoesNotContain(pending, d => d.DeliveryId == delivery.Id);
        Assert.Equal(available.Id, Assert.Single(pending).DeliveryId);
        using var duplicate = await Start(delivery.Id);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("This delivery already has an inspection.",
            (await duplicate.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
        await using var db = CreateDb();
        var persisted = Assert.Single(await db.Inspections.Include(i => i.Items)
            .Where(i => i.DeliveryId == delivery.Id).ToListAsync());
        Assert.Equal(inspection.Id, persisted.Id);
        Assert.Equal(InspectionStatus.Completed, persisted.Status);
        Assert.Equal(InspectionDecision.Accepted, persisted.OverallDecision);
        Assert.Equal(10m, Assert.Single(persisted.Items).AcceptedQuantity);
        using var fresh = await Start(available.Id);
        Assert.Equal(HttpStatusCode.Created, fresh.StatusCode);
    }

    [InspectionPostgresTheory]
    [InlineData(DeliveryStatus.Received)]
    [InlineData(DeliveryStatus.DiscrepancyReported)]
    public async Task Concurrent_starts_create_exactly_one_inspection(DeliveryStatus status)
    {
        var delivery = await SeedDelivery(status);
        await using var blocker = new NpgsqlConnection(_connection);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var rowLock = new NpgsqlCommand(
            "SELECT * FROM deliveries WHERE \"Id\" = @id FOR UPDATE", blocker, transaction);
        rowLock.Parameters.AddWithValue("id", delivery.Id);
        await rowLock.ExecuteNonQueryAsync();

        var first = Start(delivery.Id);
        var second = Start(delivery.Id);
        try
        {
            // Observe both independent HTTP requests blocked in PostgreSQL before release.
            // This fails if production row locking is removed; no scheduling-based sleep race.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            // Autocommit refreshes pg_stat_activity on every poll (the lock holder's
            // transaction would retain its first statistics snapshot).
            await using var observer = new NpgsqlConnection(_connection);
            await observer.OpenAsync(timeout.Token);
            await using var waiting = new NpgsqlCommand("""
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database() AND application_name = @app
                  AND cardinality(pg_blocking_pids(pid)) > 0
                  AND query LIKE '%FOR UPDATE%'
                """, observer);
            waiting.Parameters.AddWithValue("app", _database);
            while ((long)(await waiting.ExecuteScalarAsync(timeout.Token))! < 2)
                await Task.Delay(25, timeout.Token);
        }
        finally
        {
            await transaction.RollbackAsync();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(25));
        }
        using var firstResponse = await first;
        using var secondResponse = await second;
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict },
            new[] { firstResponse.StatusCode, secondResponse.StatusCode }.OrderBy(code => code).ToArray());
        var winner = firstResponse.StatusCode == HttpStatusCode.Created ? firstResponse : secondResponse;
        var created = (await winner.Content.ReadFromJsonAsync<QualityInspectionResponseDto>())!;
        await using var db = CreateDb();
        var persisted = Assert.Single(await db.Inspections.Where(i => i.DeliveryId == delivery.Id).ToListAsync());
        Assert.Equal(created.Id, persisted.Id);
        Assert.Equal(InspectionStatus.UnderInspection, persisted.Status);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
        }
        if (_created)
        {
            // Only this instance's generated database can be dropped; no supplied name is used.
            await using var admin = new NpgsqlConnection(_adminConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{_database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
            _created = false;
        }
    }
}
