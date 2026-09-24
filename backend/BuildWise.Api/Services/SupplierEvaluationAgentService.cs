using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BuildWise.Api.Services;

public class SupplierEvaluationAgentService
{
    private readonly ApplicationDbContext _dbContext;

    public SupplierEvaluationAgentService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> EvaluateQuotationsAsync(int rfqOrRequestId, bool isRfqId = false)
    {
        IQueryable<Quotation> query = _dbContext.Quotations
            .Include(q => q.Supplier)
            .Include(q => q.MaterialRequest)
                .ThenInclude(r => r!.Project)
            .Include(q => q.Items)
                .ThenInclude(i => i.MaterialRequestItem)
                    .ThenInclude(mri => mri!.Material);

        if (isRfqId)
        {
            query = query.Where(q => q.MaterialRequestId == rfqOrRequestId);
        }
        else
        {
            query = query.Where(q => q.MaterialRequestId == rfqOrRequestId);
        }

        var quotations = await query.ToListAsync();

        if (!quotations.Any())
        {
            throw new ArgumentException("No quotations submitted for evaluation.");
        }

        var materialRequest = quotations.First().MaterialRequest;
        var requiredDate = materialRequest?.RequiredDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

        var evaluations = new List<object>();

        foreach (var q in quotations)
        {
            var riskFlags = new List<string>();
            var pros = new List<string>();

            // Calculate total landed cost
            var totalLandedCost = q.TotalAmount;

            // 1. Delivery Date Assessment
            var promisedDate = q.QuotationDate.AddDays(3); // Default estimate
            var canMeetDate = promisedDate <= requiredDate;
            if (!canMeetDate)
            {
                riskFlags.Add($"DELIVERY DELAY: Estimated delivery date ({promisedDate:yyyy-MM-dd}) is after site required date ({requiredDate:yyyy-MM-dd}).");
            }
            else
            {
                pros.Add($"Timely delivery promised ({promisedDate:yyyy-MM-dd}).");
            }

            // 2. Supplier Status Assessment
            if (q.Supplier?.Status == SupplierStatus.Suspended)
            {
                riskFlags.Add("SUPPLIER SUSPENDED: Supplier currently flagged as Suspended.");
            }
            else if (q.Supplier?.Status == SupplierStatus.Inactive)
            {
                riskFlags.Add("SUPPLIER INACTIVE: Supplier status is Inactive.");
            }

            // 3. Validity Assessment
            if (q.ValidUntil < DateOnly.FromDateTime(DateTime.UtcNow))
            {
                riskFlags.Add($"EXPIRED QUOTATION: Quotation expired on {q.ValidUntil:yyyy-MM-dd}.");
            }

            // 4. Cost breakdown
            pros.Add($"Competitive landed cost: LKR {totalLandedCost:N2}.");

            // Compute score
            double score = 100.0;
            if (!canMeetDate) score -= 40;
            if (q.Supplier?.Status != SupplierStatus.Active) score -= 50;
            if (q.ValidUntil < DateOnly.FromDateTime(DateTime.UtcNow)) score -= 30;

            evaluations.Add(new
            {
                QuotationId = q.Id,
                SupplierId = q.SupplierId,
                SupplierName = q.Supplier?.Name ?? "Unknown Supplier",
                SupplierStatus = q.Supplier?.Status.ToString(),
                TotalLandedCost = totalLandedCost,
                PromisedDeliveryDate = promisedDate,
                CanMeetRequiredDate = canMeetDate,
                Score = Math.Max(0, score),
                Pros = pros,
                RiskFlags = riskFlags
            });
        }

        // Rank suppliers by Score descending, then TotalLandedCost ascending
        var rankedList = evaluations
            .OrderByDescending(e => ((dynamic)e).Score)
            .ThenBy(e => ((dynamic)e).TotalLandedCost)
            .ToList();

        var winner = rankedList.First();

        var recommendation = new ProcurementRecommendation
        {
            MaterialRequestId = materialRequest?.Id ?? quotations.First().MaterialRequestId,
            RecommendedSupplierId = ((dynamic)winner).SupplierId,
            RecommendedQuotationId = ((dynamic)winner).QuotationId,
            Summary = $"AI Recommended Supplier: {((dynamic)winner).SupplierName} for LKR {((dynamic)winner).TotalLandedCost:N2}",
            Justification = string.Join(" ", ((dynamic)winner).Pros) + (((List<string>)((dynamic)winner).RiskFlags).Any() ? " Risk Warning: " + string.Join(" ", (List<string>)((dynamic)winner).RiskFlags) : ""),
            RiskFlagsJson = JsonSerializer.Serialize(((dynamic)winner).RiskFlags),
            Status = RecommendationStatus.AwaitingApproval,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.ProcurementRecommendations.Add(recommendation);
        await _dbContext.SaveChangesAsync();

        return new
        {
            AgentName = "Supplier Evaluation Agent (Agent 2)",
            RecommendationId = recommendation.Id,
            MaterialRequestId = recommendation.MaterialRequestId,
            RecommendedSupplierId = recommendation.RecommendedSupplierId,
            RecommendedSupplierName = ((dynamic)winner).SupplierName,
            TotalLandedCost = ((dynamic)winner).TotalLandedCost,
            Justification = recommendation.Justification,
            Rankings = rankedList,
            HumanApprovalRequired = true,
            Note = "Action requires human Procurement Manager authorization before PO generation."
        };
    }
}
