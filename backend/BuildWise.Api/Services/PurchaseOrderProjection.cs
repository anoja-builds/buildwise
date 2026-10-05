using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Security;

namespace BuildWise.Api.Services;

/// <summary>
/// Maps purchase orders to the shape a given caller is allowed to see.
/// <para>
/// This is the Phase 1 fix for purchase order information exposure. Previously a
/// single <c>PurchaseOrderDto</c> carried <c>TotalAmount</c> and per-line
/// <c>UnitPrice</c> to every role on a class-level <c>[Authorize]</c>, so Site
/// Engineers, Site Officers, Receiving Officers and Quality Inspectors could read
/// commercial terms they have no need for — and <c>GET /api/deliveries/
/// confirmed-orders</c> returned the raw entity graph, leaking the same data to
/// any authenticated caller.
/// </para>
/// <para>
/// Roles inside the procurement function keep full commercial visibility. Every
/// other internal role receives <see cref="PurchaseOrderReceivingDto"/>, where
/// every monetary field is <c>null</c>. External Supplier portal users are scoped
/// separately to their own supplier's orders.
/// </para>
/// </summary>
public static class PurchaseOrderProjection
{
    /// <summary>Roles permitted to read unit prices and order totals.</summary>
    private static readonly string[] CommercialRoles =
    {
        Roles.ProcurementOfficer,
        Roles.ProcurementManager,
        Roles.SiteManager,
        Roles.Administrator
    };

    public static bool CanSeeCommercialTerms(System.Security.Claims.ClaimsPrincipal caller) =>
        caller.IsInAnyRole(CommercialRoles);

    /// <summary>Full commercial projection, for procurement desk callers only.</summary>
    public static PurchaseOrderDto ToCommercialDto(PurchaseOrder po) => new(
        po.Id,
        po.QuotationId ?? 0,
        po.Quotation?.MaterialRequestId ?? 0,
        po.SupplierId ?? po.Quotation?.SupplierId ?? 0,
        ResolveSupplierName(po),
        po.OrderDate,
        po.ExpectedDeliveryDate,
        po.Status.ToString(),
        po.TotalAmount,
        po.CreatedAt,
        po.UpdatedAt,
        po.Items.Select(i => new PurchaseOrderItemDto(
            i.Id,
            i.QuotationItemId,
            i.MaterialId,
            ResolveMaterialName(i),
            ResolveUnit(i),
            i.OrderedQuantity,
            i.UnitPrice,
            i.OrderedQuantity * i.UnitPrice
        )).ToList()
    );

    /// <summary>
    /// Redacted projection for site, receiving and quality roles. Prices are
    /// omitted entirely rather than zeroed, so "not visible" is unambiguous.
    /// </summary>
    public static PurchaseOrderReceivingDto ToReceivingDto(PurchaseOrder po) => new(
        po.Id,
        po.Quotation?.MaterialRequestId ?? po.ProjectId,
        ResolveSupplierName(po),
        po.OrderDate,
        po.ExpectedDeliveryDate,
        po.Status.ToString(),
        null,
        po.CreatedAt,
        po.UpdatedAt,
        po.Items.Select(i => new PurchaseOrderItemReceivingDto(
            i.Id,
            i.MaterialId,
            ResolveMaterialName(i),
            ResolveUnit(i),
            i.OrderedQuantity
        )).ToList()
    );

    /// <summary>
    /// Projects each order using whichever shape the caller is entitled to.
    /// </summary>
    public static IEnumerable<object> ToCallerDto(
        this IEnumerable<PurchaseOrder> orders,
        System.Security.Claims.ClaimsPrincipal caller)
    {
        var commercial = CanSeeCommercialTerms(caller);
        return orders.Select(po => commercial ? ToCommercialDto(po) : (object)ToReceivingDto(po));
    }

    public static object ToCallerDto(this PurchaseOrder po, System.Security.Claims.ClaimsPrincipal caller) =>
        CanSeeCommercialTerms(caller) ? ToCommercialDto(po) : ToReceivingDto(po);

    // C2 POs carry the supplier via the winning quotation; C3 POs carry it
    // directly (SupplierId). Prefer the direct link, fall back to quotation.
    private static string ResolveSupplierName(PurchaseOrder po) =>
        po.Supplier?.Name ?? po.Quotation?.Supplier?.Name
        ?? $"Supplier #{po.SupplierId ?? po.Quotation?.SupplierId ?? 0}";

    private static string ResolveMaterialName(PurchaseOrderItem i) =>
        i.QuotationItem?.MaterialRequestItem?.Material?.Name
        ?? i.Material?.Name
        ?? $"Item #{i.QuotationItemId ?? i.MaterialId ?? i.Id}";

    private static string ResolveUnit(PurchaseOrderItem i) =>
        i.QuotationItem?.MaterialRequestItem?.Material?.Unit
        ?? i.Material?.Unit
        ?? "Units";
}