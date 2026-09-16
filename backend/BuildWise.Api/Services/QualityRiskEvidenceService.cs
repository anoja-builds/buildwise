using BuildWise.Api.Data;
using BuildWise.Api.Models.Dtos;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BuildWise.Api.Services;

public class QualityRiskEvidenceService(ApplicationDbContext db)
{
    public async Task<Inspection> GetSubjectAsync(int id, CancellationToken ct)
    {
        var inspection = await db.Inspections.AsNoTracking()
            .Include(i => i.Delivery)!.ThenInclude(d => d!.PurchaseOrder)!.ThenInclude(po => po!.Supplier)
            .SingleOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new QualityRiskException(404, "Inspection not found.");
        if (inspection.Status != InspectionStatus.Completed || !inspection.OverallDecision.HasValue)
            throw new QualityRiskException(409, "Quality analysis requires a completed inspection with a decision.");
        if (inspection.Delivery?.PurchaseOrder?.Supplier == null)
            throw new QualityRiskException(400, "Inspection supplier provenance is incomplete.");
        return inspection;
    }

    public async Task<QualityRiskEvidence> CollectAsync(int id, CancellationToken ct)
    {
        var inspection = await GetSubjectAsync(id, ct);
        var delivery = inspection.Delivery!;
        var supplier = delivery.PurchaseOrder!.Supplier!;
        var rows = await db.InspectionItems.AsNoTracking().Where(i => i.InspectionId == id)
            .Include(i => i.DeliveryItem)!.ThenInclude(di => di!.PurchaseOrderItem)!.ThenInclude(poi => poi!.Material)
            .OrderBy(i => i.Id).Take(101).ToListAsync(ct);
        if (rows.Count is 0 or > 100 || rows.Select(i => i.DeliveryItemId).Distinct().Count() != rows.Count)
            throw new QualityRiskException(400, "Inspection must contain 1 to 100 distinct valid items for analysis.");
        var positiveIds = await db.DeliveryItems.Where(di => di.DeliveryId == delivery.Id && di.ReceivedQuantity > 0)
            .Select(di => di.Id).ToListAsync(ct);
        if (!positiveIds.ToHashSet().SetEquals(rows.Select(i => i.DeliveryItemId)))
            throw new QualityRiskException(400, "Inspection does not cover the received delivery items.");
        var items = new List<QualityItemEvidence>();
        foreach (var row in rows)
        {
            var di = row.DeliveryItem;
            var a = row.AcceptedQuantity;
            var r = row.RejectedQuantity;
            if (di == null || di.DeliveryId != delivery.Id || a < 0 || r < 0
                || a > 9999999999.99m || r > 9999999999.99m || a + r <= 0
                || a + r > di.ReceivedQuantity || di.DamagedQuantity < 0
                || di.DamagedQuantity > di.ReceivedQuantity || di.ReceivedQuantity > 9999999999.99m
                || decimal.Round(a, 2) != a || decimal.Round(r, 2) != r)
                throw new QualityRiskException(400, "Inspection contains invalid quantity evidence.");
            var material = di.PurchaseOrderItem?.Material;
            items.Add(new(row.Id, di.Id, material?.Id, Clip(material?.Name, 200), Clip(material?.Unit, 50),
                di.ReceivedQuantity, di.DamagedQuantity, a, r, r / (a + r), (a + r) / di.ReceivedQuantity,
                Clip(row.Condition, 100), Clip(row.Remarks, 2000)));
        }
        var accepted = items.Any(i => i.AcceptedQuantity > 0);
        var rejected = items.Any(i => i.RejectedQuantity > 0);
        var validDecision = inspection.OverallDecision switch
        {
            InspectionDecision.Accepted => accepted && !rejected,
            InspectionDecision.Rejected => !accepted && rejected,
            InspectionDecision.PartiallyAccepted => accepted && rejected,
            _ => false
        };
        if (!validDecision) throw new QualityRiskException(400, "Inspection decision is inconsistent with its evidence.");

        var collectedAt = DateTime.UtcNow;
        // UpdatedAt is the existing completion timestamp. No historical completion column is invented.
        var cutoff = inspection.UpdatedAt;
        var history = await db.Inspections.AsNoTracking()
            .Where(i => i.Id != id && i.Status == InspectionStatus.Completed && i.OverallDecision != null
                && i.UpdatedAt < cutoff && i.Delivery!.PurchaseOrder!.SupplierId == supplier.Id)
            .OrderByDescending(i => i.UpdatedAt).ThenByDescending(i => i.Id).Take(20)
            .Select(i => new QualityHistoryEvidence(i.Id, i.DeliveryId, i.InspectionDate,
                i.OverallDecision!.Value.ToString(), i.Items.Count, i.Items.Count(x => x.RejectedQuantity > 0)))
            .ToListAsync(ct);
        var ncrQuery = db.NonConformances.AsNoTracking().Where(n => n.CreatedAt < cutoff
            && n.InspectionItem!.Inspection!.Delivery!.PurchaseOrder!.SupplierId == supplier.Id);
        var count = await ncrQuery.CountAsync(ct);
        var ncrRows = await ncrQuery.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(50).ToListAsync(ct);
        var ncrs = ncrRows.Select(n => new QualityNcrEvidence(n.Id, n.InspectionItemId,
            n.Severity.ToString(), n.Status.ToString(), Clip(n.IssueDescription, 2000)!, Clip(n.CorrectiveAction, 2000))).ToList();
        var discrepancyRows = await db.Deliveries.AsNoTracking()
            .Where(d => d.PurchaseOrder!.SupplierId == supplier.Id && d.Status == DeliveryStatus.DiscrepancyReported
                && d.CreatedAt <= collectedAt)
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).Take(21)
            .Select(d => new QualityDiscrepancyEvidence(d.Id, d.Status.ToString())).ToListAsync(ct);
        var issueRows = await db.DeliveryIssues.AsNoTracking()
            .Where(x => x.Delivery!.PurchaseOrder!.SupplierId == supplier.Id && x.ReportedAt <= collectedAt)
            .OrderByDescending(x => x.ReportedAt).ThenByDescending(x => x.Id).Take(21).ToListAsync(ct);
        var issues = issueRows.Take(20).Select(x => new QualityIssueEvidence(x.Id, x.DeliveryId,
            x.IssueType.ToString(), x.Severity.ToString(), Clip(x.Description, 2000)!)).ToList();
        var discrepancies = discrepancyRows.Take(20).ToList();
        var references = new List<string> { $"inspection:{id}", $"supplier:{supplier.Id}", $"delivery:{delivery.Id}" };
        references.AddRange(items.Select(i => $"inspection-item:{i.InspectionItemId}"));
        references.AddRange(history.Select(i => $"inspection:{i.InspectionId}"));
        references.AddRange(ncrs.Select(n => $"ncr:{n.Id}"));
        references.AddRange(discrepancies.Select(d => $"delivery:{d.DeliveryId}"));
        references.AddRange(issues.Select(i => $"delivery-issue:{i.Id}"));
        return new(id, delivery.Id, supplier.Id, Clip(supplier.Name, 200)!, supplier.Status.ToString(),
            inspection.InspectionDate, inspection.OverallDecision!.Value.ToString(), collectedAt, items, history,
            history.Count == 0 ? "Insufficient historical evidence." : "At most 20 earlier completed inspection events; re-inspections are separate events, not additional delivered volume.",
            count, ncrs, count > ncrs.Count, discrepancies, discrepancyRows.Count > 20,
            issues, issueRows.Count > 20, references.Distinct().ToList());
    }

    private static string? Clip(string? value, int max) => value?.Length > max ? value[..max] : value;
}

public class QualityRiskException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
