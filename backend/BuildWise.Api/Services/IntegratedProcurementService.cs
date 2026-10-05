using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class IntegratedProcurementService
{
    private readonly ApplicationDbContext _context;

    public IntegratedProcurementService(ApplicationDbContext context)
    {
        _context = context;
    }

    // --- Component 1 Business Rules ---
    public async Task<MaterialRequest> CreateMaterialRequestAsync(MaterialRequest request)
    {
        var project = await _context.Projects.FindAsync(request.ProjectId);
        if (project == null || project.Status != ProjectStatus.Active)
            throw new InvalidOperationException("Requests can only be created for Active projects.");

        if (request.RequiredDate <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new InvalidOperationException("Required date must be in the future.");

        request.Status = MaterialRequestStatus.PendingApproval;
        _context.MaterialRequests.Add(request);
        await _context.SaveChangesAsync();
        return request;
    }

    // --- Component 2 Deterministic Business Rules ---
    public async Task<PurchaseOrder> CreatePurchaseOrderFromQuotationAsync(int quotationId)
    {
        var quotation = await _context.Quotations
            .Include(q => q.Supplier)
            .Include(q => q.MaterialRequest)
            .Include(q => q.Items)
            .ThenInclude(i => i.MaterialRequestItem)
            .ThenInclude(mri => mri.Material)
            .FirstOrDefaultAsync(q => q.Id == quotationId);

        if (quotation == null) throw new KeyNotFoundException("Quotation not found.");

        // 7 Deterministic Safety Checks
        if (quotation.MaterialRequest?.Status != MaterialRequestStatus.Approved)
            throw new InvalidOperationException("Linked material request is not approved.");
        if (quotation.Supplier?.Status != SupplierStatus.Active)
            throw new InvalidOperationException("Supplier is suspended or inactive.");
        if (quotation.ValidUntil < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new InvalidOperationException("Quotation is expired.");

        var po = new PurchaseOrder
        {
            QuotationId = quotationId,
            TotalAmount = quotation.TotalAmount,
            Status = PurchaseOrderStatus.Confirmed,
            CreatedAt = DateTime.UtcNow,
            Items = quotation.Items.Select(i => new PurchaseOrderItem
            {
                MaterialId = i.MaterialRequestItem.MaterialId,
                OrderedQuantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                QuotationItemId = i.Id
            }).ToList()
        };

        _context.PurchaseOrders.Add(po);
        await _context.SaveChangesAsync();
        return po;
    }

    // --- Component 3 Delivery Receiving Rules ---
    public async Task<Delivery> RecordDeliveryAsync(Delivery delivery)
    {
        var po = await _context.PurchaseOrders
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == delivery.PurchaseOrderId);

        if (po == null || po.Status != PurchaseOrderStatus.Confirmed)
            throw new InvalidOperationException("Deliveries can only be recorded for Confirmed Purchase Orders.");

        bool hasDiscrepancy = false;
        foreach (var item in delivery.Items)
        {
            var poItem = po.Items.FirstOrDefault(i => i.MaterialId == item.MaterialId);
            if (poItem == null || item.ReceivedQuantity < poItem.OrderedQuantity || item.DamagedQuantity > 0)
            {
                hasDiscrepancy = true;
            }
        }

        delivery.Status = hasDiscrepancy ? DeliveryStatus.DiscrepancyReported : DeliveryStatus.Received;
        _context.Deliveries.Add(delivery);
        await _context.SaveChangesAsync();
        return delivery;
    }
}
