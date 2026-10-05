using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildWise.Api.Tests;

/// <summary>
/// Purchase-order status is a lifecycle. Before this rule existed the PATCH
/// endpoint accepted any status from any status, so a Completed order could be
/// moved back to Confirmed and a Cancelled order could be reopened. These tests
/// drive the real HTTP endpoint so the controller logic is what is asserted.
/// </summary>
public class PurchaseOrderStatusTransitionTests : IClassFixture<RbacApiFactory>
{
    private readonly RbacApiFactory _factory;

    public PurchaseOrderStatusTransitionTests(RbacApiFactory factory) => _factory = factory;

    [Theory]
    // Forward moves through the lifecycle are allowed.
    [InlineData(PurchaseOrderStatus.Created, PurchaseOrderStatus.Confirmed, HttpStatusCode.NoContent)]
    [InlineData(PurchaseOrderStatus.Confirmed, PurchaseOrderStatus.InProgress, HttpStatusCode.NoContent)]
    // Skipping InProgress is refused: it would record fulfilment that never happened.
    [InlineData(PurchaseOrderStatus.Confirmed, PurchaseOrderStatus.Completed, HttpStatusCode.BadRequest)]
    [InlineData(PurchaseOrderStatus.InProgress, PurchaseOrderStatus.Completed, HttpStatusCode.NoContent)]
    // Cancellation is allowed only while the order is still live.
    [InlineData(PurchaseOrderStatus.Created, PurchaseOrderStatus.Cancelled, HttpStatusCode.NoContent)]
    [InlineData(PurchaseOrderStatus.Confirmed, PurchaseOrderStatus.Cancelled, HttpStatusCode.NoContent)]
    [InlineData(PurchaseOrderStatus.InProgress, PurchaseOrderStatus.Cancelled, HttpStatusCode.NoContent)]
    // Backward moves are refused.
    [InlineData(PurchaseOrderStatus.Completed, PurchaseOrderStatus.InProgress, HttpStatusCode.BadRequest)]
    [InlineData(PurchaseOrderStatus.Completed, PurchaseOrderStatus.Confirmed, HttpStatusCode.BadRequest)]
    [InlineData(PurchaseOrderStatus.InProgress, PurchaseOrderStatus.Confirmed, HttpStatusCode.BadRequest)]
    [InlineData(PurchaseOrderStatus.Confirmed, PurchaseOrderStatus.Created, HttpStatusCode.BadRequest)]
    // Terminal states cannot be reopened or re-cancelled.
    [InlineData(PurchaseOrderStatus.Cancelled, PurchaseOrderStatus.Confirmed, HttpStatusCode.BadRequest)]
    [InlineData(PurchaseOrderStatus.Cancelled, PurchaseOrderStatus.InProgress, HttpStatusCode.BadRequest)]
    [InlineData(PurchaseOrderStatus.Completed, PurchaseOrderStatus.Cancelled, HttpStatusCode.BadRequest)]
    // A no-op change is not a transition.
    [InlineData(PurchaseOrderStatus.Confirmed, PurchaseOrderStatus.Confirmed, HttpStatusCode.BadRequest)]
    public async Task Status_transitions_follow_the_purchase_order_lifecycle(
        PurchaseOrderStatus from, PurchaseOrderStatus to, HttpStatusCode expected)
    {
        var db = await _factory.GetSeededDbAsync();
        var purchaseOrder = await SeedPurchaseOrderAsync(db, from);

        var client = _factory.CreateClientFor(Roles.ProcurementOfficer);
        var response = await client.PatchAsJsonAsync(
            $"/api/purchase-orders/{purchaseOrder.Id}/status",
            new { status = to.ToString() });

        Assert.Equal(expected, response.StatusCode);

        // The persisted status must never have moved on a refused request.
        var reloaded = await db.PurchaseOrders.AsNoTracking().SingleAsync(po => po.Id == purchaseOrder.Id);
        if (expected == HttpStatusCode.BadRequest)
        {
            Assert.Equal(from, reloaded.Status);
        }
        else
        {
            Assert.Equal(to, reloaded.Status);
        }
    }

    [Fact]
    public async Task An_unknown_status_is_rejected()
    {
        var db = await _factory.GetSeededDbAsync();
        var purchaseOrder = await SeedPurchaseOrderAsync(db, PurchaseOrderStatus.Confirmed);

        var client = _factory.CreateClientFor(Roles.ProcurementOfficer);
        var response = await client.PatchAsJsonAsync(
            $"/api/purchase-orders/{purchaseOrder.Id}/status",
            new { status = "Teleported" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_non_procurement_role_cannot_change_status()
    {
        var db = await _factory.GetSeededDbAsync();
        var purchaseOrder = await SeedPurchaseOrderAsync(db, PurchaseOrderStatus.Confirmed);

        // Reading POs is widely allowed; changing one is procurement-desk only.
        var client = _factory.CreateClientFor(Roles.SiteOfficer);
        var response = await client.PatchAsJsonAsync(
            $"/api/purchase-orders/{purchaseOrder.Id}/status",
            new { status = nameof(PurchaseOrderStatus.Completed) });

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"expected 401/403 for SiteOfficer, got {(int)response.StatusCode}");
    }

    private static async Task<PurchaseOrder> SeedPurchaseOrderAsync(
        ApplicationDbContext db, PurchaseOrderStatus status)
    {
        var project = new Project
        {
            Name = $"PO transition project {Guid.NewGuid()}",
            Status = ProjectStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var purchaseOrder = new PurchaseOrder
        {
            ProjectId = project.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpectedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            Status = status,
            TotalAmount = 1000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.PurchaseOrders.Add(purchaseOrder);
        await db.SaveChangesAsync();

        return purchaseOrder;
    }
}