using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildWise.Api.Tests;

public class ProcurementPlanningAgentServiceTests
{
    [Fact]
    public async Task CreatesStructuredPlanWithOnlyApprovedReadTools()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await SeedPlanningScenarioAsync(db);
        var service = new ProcurementPlanningAgentService(db);

        var result = await service.CreatePlanAsync(new ProcurementPlanningInput(data.Request.Id, "Select a compliant supplier."));

        Assert.Equal("ProcurementPlanningAgent", result.AgentRole);
        Assert.Equal("Select a compliant supplier.", result.Objective);
        Assert.Equal(new[] { "GetMaterialRequest", "GetMaterialDetails", "GetProjectDetails", "GetAvailableQuotations" }, result.AllowedTools);
        Assert.Equal(4, result.ToolResults.Count);
        Assert.All(result.ToolResults, tool => Assert.Contains(tool.ToolName, ProcurementPlanningAgentService.AllowedToolNames));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, result.Steps.Select(s => s.StepOrder));
        Assert.Equal("DeliveryRiskAgent", result.Steps.Single(s => s.Name == "Assess delivery risk").DelegatedAgentRole);
        Assert.Contains(result.Steps, s => s.RequiresHumanApproval && s.DelegatedAgentRole == "ProcurementManager");
        Assert.Contains(result.RequiredChecks, check => check.Contains("authorized-manager decision"));
        Assert.Equal(new[] { "Approve procurement", "Issue purchase order", "Modify supplier records" }, result.ProhibitedCapabilities);
        Assert.DoesNotContain("Approve procurement", result.Steps.SelectMany(s => new[] { s.Action, s.Name, s.DelegatedAgentRole }));
    }

    [Fact]
    public async Task FlagsIneligibleAndExpiredQuotationRisk()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await SeedPlanningScenarioAsync(db, includeSecondQuotation: false);
        var suspended = new Supplier { Name = "Suspended", Status = SupplierStatus.Suspended };
        var expired = new Supplier { Name = "Expired", Status = SupplierStatus.Active };
        db.Suppliers.AddRange(suspended, expired);
        await db.SaveChangesAsync();
        db.Quotations.Add(new Quotation
        {
            MaterialRequestId = data.Request.Id, SupplierId = suspended.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-20)),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), Status = QuotationStatus.Submitted,
            TotalAmount = 520000m, Items = new List<QuotationItem> { new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250, UnitPrice = 2080 } }
        });
        db.Quotations.Add(new Quotation
        {
            MaterialRequestId = data.Request.Id, SupplierId = expired.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-20)),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), Status = QuotationStatus.Submitted,
            TotalAmount = 510000m, Items = new List<QuotationItem> { new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250, UnitPrice = 2040 } }
        });
        await db.SaveChangesAsync();

        var result = await new ProcurementPlanningAgentService(db).CreatePlanAsync(new ProcurementPlanningInput(data.Request.Id));

        Assert.Contains("INELIGIBLE_SUPPLIER_PRESENT", result.RiskFlags);
        Assert.Contains("EXPIRED_QUOTATION_PRESENT", result.RiskFlags);
    }

    [Fact]
    public async Task FlagsSingleQuotationComparison()
    {
        var db = TestDbFactory.CreateInMemory();
        var data = await SeedPlanningScenarioAsync(db, includeSecondQuotation: false);
        var result = await new ProcurementPlanningAgentService(db).CreatePlanAsync(new ProcurementPlanningInput(data.Request.Id));
        Assert.Contains("SINGLE_QUOTATION_COMPARISON", result.RiskFlags);
    }

    private static async Task<StandardScenarioEntities> SeedPlanningScenarioAsync(ApplicationDbContext db, bool includeSecondQuotation = true)
    {
        var data = await TestDbFactory.SeedStandardScenarioDataAsync(db);
        var first = new Supplier { Name = "Active One", Status = SupplierStatus.Active };
        db.Suppliers.Add(first);
        await db.SaveChangesAsync();
        db.Quotations.Add(new Quotation
        {
            MaterialRequestId = data.Request.Id, SupplierId = first.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow), ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            Status = QuotationStatus.Submitted, TotalAmount = 525000m,
            Items = new List<QuotationItem> { new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250, UnitPrice = 2100 } }
        });
        if (includeSecondQuotation)
        {
            var second = new Supplier { Name = "Active Two", Status = SupplierStatus.Active };
            db.Suppliers.Add(second); await db.SaveChangesAsync();
            db.Quotations.Add(new Quotation
            {
                MaterialRequestId = data.Request.Id, SupplierId = second.Id,
                QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow), ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
                Status = QuotationStatus.Submitted, TotalAmount = 510000m,
                Items = new List<QuotationItem> { new() { MaterialRequestItemId = data.RequestItem.Id, Quantity = 250, UnitPrice = 2040 } }
            });
        }
        await db.SaveChangesAsync();
        return data;
    }
}
