using System.Security.Claims;
using BuildWise.Api.Controllers;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace BuildWise.Api.Tests;

public class DeliveryReconciliationTests
{
    private static readonly string[] Cases = ["full", "partial", "damaged", "split", "repeat", "negative", "negativeDamage", "excessDamage", "over", "unknown", "foreign", "duplicate", "missing", "empty"];
    public static IEnumerable<object[]> Scenarios() => Cases.Select(c => new object[] { c });

    [Theory, MemberData(nameof(Scenarios))]
    public async Task Reconcile_using_actual_controller(string scenario)
    {
        await using var db = TestDbFactory.CreateInMemory();
        await Exercise(db, scenario);
    }

    [InspectionPostgresTheory]
    [InlineData(true)]
    public async Task Reconcile_on_isolated_PostgreSQL(bool _)
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("BUILDWISE_TEST_POSTGRES"))
            { Database = "postgres", Pooling = false };
        await using var admin = new NpgsqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        var name = "buildwise_delivery_test_" + Guid.NewGuid().ToString("N");
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            builder.Database = name;
            foreach (var scenario in Cases)
            {
                await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(builder.ConnectionString).Options);
                await db.Database.EnsureCreatedAsync();
                await Exercise(db, scenario);
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task Exercise(ApplicationDbContext db, string scenario)
    {
        var user = new User { FullName = "Receiver", Email = Guid.NewGuid() + "@test.example", PasswordHash = "unused" };
        var material = new Material { Name = "Cement", Unit = "bags" };
        var po = new PurchaseOrder { OrderDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = PurchaseOrderStatus.InProgress,
            Items = [new PurchaseOrderItem { Material = material, OrderedQuantity = 100, UnitPrice = 2 }] };
        var item = po.Items.Single();
        var delivery = new Delivery { PurchaseOrder = po, Items = [new DeliveryItem { PurchaseOrderItem = item }] };
        var other = new PurchaseOrderItem { PurchaseOrder = new PurchaseOrder { OrderDate = DateOnly.FromDateTime(DateTime.UtcNow) }, Material = material, OrderedQuantity = 1 };
        db.Users.Add(user);
        db.Deliveries.Add(delivery);
        db.PurchaseOrderItems.Add(other);
        await db.SaveChangesAsync();
        var id = delivery.Id;
        var poId = po.Id;
        var itemId = item.Id;
        var actorId = user.Id;
        var controller = new DeliveriesController(db, new DeliveryRiskAgentService(db, new ConfigurationBuilder().Build()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new Claim(ClaimTypes.Role, "ReceivingOfficer")], "Test")) } }
        };
        ReceiveDeliveryDto Payload(decimal received, decimal damaged = 0) => new() {
            ReceivedByUserId = 999, Items = [new ReceiveDeliveryItemDto { PurchaseOrderItemId = itemId,
                ReceivedQuantity = received, DamagedQuantity = damaged }] };
        var dto = Payload(100);
        switch (scenario)
        {
            case "partial": dto = Payload(60); break;
            case "damaged": dto = Payload(100, 10); break;
            case "split":
                db.Deliveries.Add(new Delivery { PurchaseOrderId = poId, Status = DeliveryStatus.DiscrepancyReported,
                    ReceivedAt = DateTime.UtcNow, Items = [new DeliveryItem { PurchaseOrderItemId = itemId, ReceivedQuantity = 60, DamagedQuantity = 10 }] });
                await db.SaveChangesAsync(); dto = Payload(50); break;
            case "negative": dto = Payload(-1); break;
            case "negativeDamage": dto = Payload(10, -1); break;
            case "excessDamage": dto = Payload(10, 11); break;
            case "over": dto = Payload(101); break;
            case "unknown": dto.Items[0].PurchaseOrderItemId = int.MaxValue; break;
            case "foreign": dto.Items[0].PurchaseOrderItemId = other.Id; break;
            case "duplicate": dto.Items.Add(dto.Items[0]); break;
            case "missing":
                delivery.Items.Add(new DeliveryItem { PurchaseOrderItem = new PurchaseOrderItem { PurchaseOrder = po, Material = material, OrderedQuantity = 1 } });
                await db.SaveChangesAsync(); break;
            case "empty": dto.Items.Clear(); break;
            case "repeat": dto = Payload(60); break;
        }
        // A fresh tracking state approximates a new HTTP request, on both providers.
        db.ChangeTracker.Clear();
        var result = await controller.ReceiveDelivery(id, dto);
        var valid = scenario is "full" or "partial" or "damaged" or "split" or "repeat";
        if (valid) Assert.IsType<OkObjectResult>(result);
        else Assert.IsType<BadRequestObjectResult>(result);
        db.ChangeTracker.Clear();
        var saved = await db.Deliveries.Include(d => d.Items).SingleAsync(d => d.Id == id);
        var savedPo = await db.PurchaseOrders.SingleAsync(p => p.Id == poId);
        if (!valid)
        {
            Assert.Null(saved.ReceivedAt);
            Assert.All(saved.Items, i => Assert.Equal(0, i.ReceivedQuantity));
            Assert.Equal(PurchaseOrderStatus.InProgress, savedPo.Status);
            return;
        }
        Assert.Equal(actorId, saved.ReceivedByUserId);
        Assert.Equal(dto.Items[0].ReceivedQuantity, Assert.Single(saved.Items).ReceivedQuantity);
        Assert.Equal(scenario is "full" or "split" ? PurchaseOrderStatus.Completed : PurchaseOrderStatus.InProgress, savedPo.Status);
        Assert.Equal(scenario is "full" or "split" ? DeliveryStatus.Received : DeliveryStatus.DiscrepancyReported, saved.Status);
        if (scenario == "repeat")
        {
            db.ChangeTracker.Clear();
            Assert.IsType<BadRequestObjectResult>(await controller.ReceiveDelivery(id, Payload(100)));
            db.ChangeTracker.Clear();
            Assert.Equal(60, (await db.DeliveryItems.SingleAsync(i => i.DeliveryId == id)).ReceivedQuantity);
        }
    }
}
