using System.Net;
using System.Text.Json;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildWise.Api.Tests;

/// <summary>
/// Phase 1 tests for the new <c>Supplier</c> portal role.
/// <para>
/// The critical property is tenant isolation: a supplier login is bound to
/// exactly one supplier by a signed JWT claim, and no endpoint accepts a
/// supplier id from the request. These tests prove one supplier can neither
/// read nor quote against another supplier's RFQs.
/// </para>
/// </summary>
public class SupplierPortalIsolationTests : IAsyncLifetime
{
    private readonly RbacApiFactory _factory = new();

    private MaterialRequestItem _requestItem = null!;
    private MaterialRequest _request = null!;
    private Supplier _supplierA = null!;
    private Supplier _supplierB = null!;
    private Rfq _rfq = null!;

    public async Task InitializeAsync()
    {
        var db = await _factory.GetSeededDbAsync();

        var project = new Project { Name = "Isolation Test Site", Status = ProjectStatus.Active };
        var material = new Material { Name = "Cement", Unit = "bag", IsActive = true };
        _supplierA = new Supplier { Name = "Supplier A", Status = SupplierStatus.Active };
        _supplierB = new Supplier { Name = "Supplier B", Status = SupplierStatus.Active };
        db.AddRange(project, material, _supplierA, _supplierB);
        await db.SaveChangesAsync();

        _request = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            Reason = "Isolation scenario",
            Status = MaterialRequestStatus.Approved
        };
        db.MaterialRequests.Add(_request);
        await db.SaveChangesAsync();

        _requestItem = new MaterialRequestItem
        {
            MaterialRequestId = _request.Id,
            MaterialId = material.Id,
            RequestedQuantity = 100m
        };
        db.MaterialRequestItems.Add(_requestItem);
        await db.SaveChangesAsync();

        // Both suppliers are invited to the same RFQ — the standard competition
        // scenario, and the exact case where cross-supplier leakage would occur.
        _rfq = new Rfq
        {
            MaterialRequestId = _request.Id,
            IssuedByUserId = 1,
            RequiredResponseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            Status = RfqStatus.Issued
        };
        _rfq.Suppliers.Add(new RfqSupplier { Rfq = _rfq, SupplierId = _supplierA.Id, Status = RfqSupplierStatus.Invited });
        _rfq.Suppliers.Add(new RfqSupplier { Rfq = _rfq, SupplierId = _supplierB.Id, Status = RfqSupplierStatus.Invited });
        db.Rfqs.Add(_rfq);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Creates a second RFQ that invited only Supplier B.</summary>
    private async Task<Rfq> CreateBOnlyRfqAsync()
    {
        var db = await _factory.GetSeededDbAsync();
        var other = new Rfq
        {
            MaterialRequestId = _request.Id,
            IssuedByUserId = 1,
            RequiredResponseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = RfqStatus.Issued
        };
        other.Suppliers.Add(new RfqSupplier { Rfq = other, SupplierId = _supplierB.Id });
        db.Rfqs.Add(other);
        await db.SaveChangesAsync();
        return other;
    }
    [Fact]
    public async Task Supplier_profile_returns_the_bound_supplier_only()
    {
        using var client = _factory.CreateSupplierClient(_supplierA.Id);

        using var response = await client.GetAsync("/api/supplier-portal/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(_supplierA.Id, body.GetProperty("supplierId").GetInt32());
        Assert.Equal("Supplier A", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Rfq_list_does_not_reveal_other_invited_suppliers()
    {
        using var client = _factory.CreateSupplierClient(_supplierA.Id);

        using var response = await client.GetAsync("/api/supplier-portal/rfqs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();

        // Supplier B was invited to the same RFQ but must be invisible.
        Assert.DoesNotContain("Supplier B", raw, StringComparison.Ordinal);

        var body = JsonDocument.Parse(raw).RootElement.EnumerateArray().ToList();
        Assert.Single(body);
        Assert.Equal(_rfq.Id, body[0].GetProperty("rfqId").GetInt32());
    }

    [Fact]
    public async Task Supplier_cannot_read_an_rfq_it_was_not_invited_to()
    {
        var otherRfq = await CreateBOnlyRfqAsync();
        using var client = _factory.CreateSupplierClient(_supplierA.Id);

        using var response = await client.GetAsync($"/api/supplier-portal/rfqs/{otherRfq.Id}/items");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Supplier_cannot_submit_a_quotation_against_another_suppliers_rfq()
    {
        var db = await _factory.GetSeededDbAsync();
        var otherRfq = await CreateBOnlyRfqAsync();

        using var client = _factory.CreateSupplierClient(_supplierA.Id);

        using var response = await client.PostAsync(
            $"/api/supplier-portal/rfqs/{otherRfq.Id}/quotations",
            JsonBody(new
            {
                quotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
                transportCharge = 0,
                items = new[] { new { materialRequestItemId = _requestItem.Id, quantity = 100m, unitPrice = 100m } }
            }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Nothing was written on Supplier B's behalf.
        Assert.Empty(db.Quotations);
    }
    [Fact]
    public async Task Supplier_can_quote_against_its_own_rfq_and_total_is_server_computed()
    {
        using var client = _factory.CreateSupplierClient(_supplierA.Id);

        // A deliberately wrong declared total: the API must ignore it entirely.
        using var response = await client.PostAsync(
            $"/api/supplier-portal/rfqs/{_rfq.Id}/quotations",
            JsonBody(new
            {
                rfqId = _rfq.Id,
                quotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
                promisedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
                paymentTerms = "Net 30",
                transportCharge = 500m,
                totalAmount = 1m,
                items = new[] { new { materialRequestItemId = _requestItem.Id, quantity = 100m, unitPrice = 250m } }
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        // 100 * 250. The client-declared 1 is discarded.
        Assert.Equal(25_000m, body.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task Supplier_quotation_is_scoped_to_its_own_supplier_record()
    {
        using var client = _factory.CreateSupplierClient(_supplierB.Id);

        using var response = await client.PostAsync(
            $"/api/supplier-portal/rfqs/{_rfq.Id}/quotations",
            JsonBody(new
            {
                quotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
                transportCharge = 0,
                items = new[] { new { materialRequestItemId = _requestItem.Id, quantity = 40m, unitPrice = 100m } }
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(4_000m, body.GetProperty("totalAmount").GetDecimal());

        // The quotation is attributed to Supplier B, not to whoever submitted it.
        var db = await _factory.GetSeededDbAsync();
        var stored = await db.Quotations.SingleAsync();
        Assert.Equal(_supplierB.Id, stored.SupplierId);
    }

    [Fact]
    public async Task Suspended_supplier_cannot_submit_a_quotation()
    {
        var db = await _factory.GetSeededDbAsync();
        _supplierA.Status = SupplierStatus.Suspended;
        await db.SaveChangesAsync();

        using var client = _factory.CreateSupplierClient(_supplierA.Id);

        using var response = await client.PostAsync(
            $"/api/supplier-portal/rfqs/{_rfq.Id}/quotations",
            JsonBody(new
            {
                quotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
                transportCharge = 0,
                items = new[] { new { materialRequestItemId = _requestItem.Id, quantity = 10m, unitPrice = 10m } }
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(db.Quotations);
    }
    [Fact]
    public async Task Supplier_only_sees_its_own_quotations()
    {
        var db = await _factory.GetSeededDbAsync();

        foreach (var supplier in new[] { _supplierA, _supplierB })
        {
            db.Quotations.Add(new Quotation
            {
                MaterialRequestId = _request.Id,
                RfqId = _rfq.Id,
                SupplierId = supplier.Id,
                QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)),
                Status = QuotationStatus.Submitted,
                TotalAmount = 1000m
            });
        }
        await db.SaveChangesAsync();

        using var client = _factory.CreateSupplierClient(_supplierA.Id);

        using var response = await client.GetAsync("/api/supplier-portal/quotations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.EnumerateArray().ToList();

        Assert.Single(rows);
    }

    [Fact]
    public async Task Supplier_portal_token_without_a_supplier_binding_is_refused()
    {
        // A Supplier role token carrying no supplier_id claim must not be able to
        // fall back to reading any supplier's data.
        using var client = _factory.CreateClientFor("Supplier");

        foreach (var path in new[] { "/api/supplier-portal/profile", "/api/supplier-portal/rfqs", "/api/supplier-portal/quotations" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Supplier_sees_only_its_own_purchase_orders()
    {
        var db = await _factory.GetSeededDbAsync();

        db.PurchaseOrders.Add(new PurchaseOrder
        {
            SupplierId = _supplierA.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PurchaseOrderStatus.Confirmed,
            TotalAmount = 25_000m
        });
        db.PurchaseOrders.Add(new PurchaseOrder
        {
            SupplierId = _supplierB.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = PurchaseOrderStatus.Confirmed,
            TotalAmount = 4_000m
        });
        await db.SaveChangesAsync();

        using var client = _factory.CreateSupplierClient(_supplierA.Id);

        using var response = await client.GetAsync("/api/supplier-portal/purchase-orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.EnumerateArray().ToList();

        Assert.Single(rows);
        Assert.Equal(25_000m, rows[0].GetProperty("totalAmount").GetDecimal());
    }

    private static StringContent JsonBody(object value) =>
        new(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");
}