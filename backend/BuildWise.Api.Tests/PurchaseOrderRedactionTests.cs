using System.Net;
using System.Text.Json;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Xunit;

namespace BuildWise.Api.Tests;

/// <summary>
/// Phase 1 regression tests for the purchase order information exposure fix.
/// <para>
/// Before the fix, a single <c>PurchaseOrderDto</c> — carrying
/// <c>TotalAmount</c> and per-line <c>UnitPrice</c> — was returned to every role
/// covered by the class-level <c>[Authorize]</c>, and
/// <c>GET /api/deliveries/confirmed-orders</c> returned the raw
/// <c>PurchaseOrder</c> entity graph to any authenticated caller.
/// </para>
/// <para>
/// These tests pin the corrected behaviour: procurement-desk roles keep full
/// commercial visibility, while site, receiving and quality roles receive the
/// operational fields with every monetary field absent.
/// </para>
/// </summary>
public class PurchaseOrderRedactionTests : IAsyncLifetime
{
    private readonly RbacApiFactory _factory = new();
    private int _purchaseOrderId;

    public async Task InitializeAsync()
    {
        var db = await _factory.GetSeededDbAsync();

        var supplier = new Supplier
        {
            Name = "Redaction Test Supplier",
            Status = SupplierStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var material = new Material
        {
            Name = "Cement (50kg bag)",
            Unit = "bag",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.AddRange(supplier, material);
        await db.SaveChangesAsync();

        var order = new PurchaseOrder
        {
            SupplierId = supplier.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpectedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            Status = PurchaseOrderStatus.Confirmed,
            TotalAmount = 525_000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        order.Items.Add(new PurchaseOrderItem
        {
            PurchaseOrder = order,
            MaterialId = material.Id,
            OrderedQuantity = 250m,
            UnitPrice = 2_100m
        });

        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();
        _purchaseOrderId = order.Id;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // --- Roles that must see commercial terms ---------------------------

    [Theory]
    [InlineData("ProcurementOfficer")]
    [InlineData("ProcurementManager")]
    [InlineData("SiteManager")]
    [InlineData("Administrator")]
    public async Task Procurement_roles_receive_unit_prices_and_order_total(string role)
    {
        using var client = _factory.CreateClientFor(role);

        using var response = await client.GetAsync($"/api/purchase-orders/{_purchaseOrderId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJson(response);
        Assert.Equal(525_000m, body.GetProperty("totalAmount").GetDecimal());

        var item = body.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(2_100m, item.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(525_000m, item.GetProperty("lineTotal").GetDecimal());
    }
    // --- Roles that must NOT see commercial terms -----------------------

    [Theory]
    [InlineData("SiteEngineer")]
    [InlineData("SiteOfficer")]
    [InlineData("ReceivingOfficer")]
    [InlineData("QualityInspector")]
    public async Task Non_procurement_roles_receive_operational_fields_without_prices(string role)
    {
        using var client = _factory.CreateClientFor(role);

        using var response = await client.GetAsync($"/api/purchase-orders/{_purchaseOrderId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJson(response);

        // The order itself is still readable — the inspector needs to know what
        // to expect — but the money is gone.
        Assert.Equal(PurchaseOrderStatus.Confirmed.ToString(), body.GetProperty("status").GetString());
        Assert.Equal("Redaction Test Supplier", body.GetProperty("supplierName").GetString());

        Assert.True(
            body.GetProperty("totalAmount").ValueKind is JsonValueKind.Null,
            $"totalAmount leaked to {role}: {body.GetProperty("totalAmount")}");

        var item = body.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(250m, item.GetProperty("orderedQuantity").GetDecimal());

        Assert.False(item.TryGetProperty("unitPrice", out _), $"unitPrice leaked to {role}.");
        Assert.False(item.TryGetProperty("lineTotal", out _), $"lineTotal leaked to {role}.");
    }

    [Theory]
    [InlineData("SiteOfficer")]
    [InlineData("QualityInspector")]
    [InlineData("ReceivingOfficer")]
    public async Task Purchase_order_list_applies_the_same_redaction(string role)
    {
        using var client = _factory.CreateClientFor(role);

        using var response = await client.GetAsync("/api/purchase-orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJson(response);
        var first = body.GetProperty("items").EnumerateArray().Single();
        Assert.True(first.GetProperty("totalAmount").ValueKind is JsonValueKind.Null);
    }

    /// <summary>
    /// The second, larger leak: this endpoint used to return the raw
    /// <c>PurchaseOrder</c> entity graph, so every per-line price and the order
    /// total went to any authenticated caller regardless of role.
    /// </summary>
    [Theory]
    [InlineData("SiteEngineer")]
    [InlineData("SiteOfficer")]
    [InlineData("QualityInspector")]
    [InlineData("ReceivingOfficer")]
    public async Task Confirmed_orders_endpoint_never_returns_prices(string role)
    {
        using var client = _factory.CreateClientFor(role);

        using var response = await client.GetAsync("/api/deliveries/confirmed-orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();

        // No per-line price fields exist on the receiving projection at all,
        // and the order total is present but explicitly null.
        Assert.DoesNotContain("unitPrice", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lineTotal", raw, StringComparison.OrdinalIgnoreCase);

        // Assert the values never appear, so a renamed field cannot hide a leak.
        Assert.DoesNotContain("525000", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("2100", raw, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(raw);
        foreach (var order in document.RootElement.EnumerateArray())
        {
            Assert.True(
                order.GetProperty("totalAmount").ValueKind is JsonValueKind.Null,
                $"confirmed-orders leaked a total: {order.GetProperty("totalAmount")}");
        }
    }

    [Fact]
    public async Task Delivery_records_do_not_carry_purchase_order_commercial_terms()
    {
        using var client = _factory.CreateClientFor("SiteOfficer");

        using var response = await client.GetAsync("/api/deliveries");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("unitPrice", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lineTotal", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("525000", raw, StringComparison.Ordinal);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }
}