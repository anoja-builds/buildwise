using System.Net;
using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildWise.Api.Tests;

/// <summary>
/// Step 5 of the real-world scenario: budget validation.
/// <para>
/// Before this, the word "budget" appeared nowhere in the backend â€” a
/// recommendation could be any price at all and nothing would flag it. These
/// tests pin the intended behaviour: an over-budget award is surfaced as a
/// warning to the approving manager but is never silently blocked, because
/// knowingly approving over budget is a legitimate business decision.
/// </para>
/// </summary>
public class BudgetValidationTests : IAsyncLifetime
{
    private const decimal Budget = 1_100_000m;

    private RbacApiFactory _factory = null!;
    private ApplicationDbContext _db = null!;
    private Project _project = null!;
    private MaterialRequest _request = null!;
    private MaterialRequestItem _requestItem = null!;
    private Supplier _supplier = null!;

    public async Task InitializeAsync()
    {
        _factory = new RbacApiFactory();
        _db = await _factory.GetSeededDbAsync();

        _project = new Project
        {
            Name = "Colombo Apartment",
            Status = ProjectStatus.Active,
            MaterialBudgetAmount = Budget
        };
        var cement = new Material { Name = "Cement (50kg bag)", Unit = "bag", IsActive = true };
        _supplier = new Supplier { Name = "Supplier A", Status = SupplierStatus.Active };
        _db.AddRange(_project, cement, _supplier);
        await _db.SaveChangesAsync();

        _request = new MaterialRequest
        {
            ProjectId = _project.Id,
            RequestedByUserId = 1,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21)),
            Reason = "10-storey apartment",
            Status = MaterialRequestStatus.Approved
        };
        _db.MaterialRequests.Add(_request);
        await _db.SaveChangesAsync();

        _requestItem = new MaterialRequestItem
        {
            MaterialRequestId = _request.Id,
            MaterialId = cement.Id,
            RequestedQuantity = 500m
        };
        _db.MaterialRequestItems.Add(_requestItem);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Creates a compliant, full-coverage quotation at the given unit price.</summary>
    private async Task<Quotation> AddQuotationAsync(decimal unitPrice, decimal transportCharge = 0m)
    {
        var quotation = new Quotation
        {
            MaterialRequestId = _request.Id,
            SupplierId = _supplier.Id,
            QuotationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            PromisedDeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            PaymentTerms = "Net 30",
            TransportCharge = transportCharge,
            Status = QuotationStatus.Submitted,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = _requestItem.Id, Quantity = 500m, UnitPrice = unitPrice }
            }
        };
        quotation.TotalAmount = quotation.Items.Sum(i => i.Quantity * i.UnitPrice);
        _db.Quotations.Add(quotation);
        await _db.SaveChangesAsync();
        return quotation;
    }

    private ProcurementValidationService NewValidator() => new(_db);

    // --- The scenario's own numbers: budget 1,100,000 ---------------------

    [Fact]
    public async Task Supplier_B_at_2180_is_within_budget()
    {
        // 500 x 2,180 = 1,090,000 against a 1,100,000 budget.
        var quotation = await AddQuotationAsync(2180m);

        var result = await NewValidator().ValidateRecommendationAsync(quotation.Id, _request.Id);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Contains(result.Warnings, w => w.Contains("Within budget"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Budget exceeds"));
    }

    [Fact]
    public async Task Supplier_A_at_2250_is_flagged_as_exceeding_budget()
    {
        // 500 x 2,250 = 1,125,000, which is 25,000 over the 1,100,000 budget.
        var quotation = await AddQuotationAsync(2250m);

        var result = await NewValidator().ValidateRecommendationAsync(quotation.Id, _request.Id);

        var warning = Assert.Single(result.Warnings, w => w.Contains("Budget exceeds planned amount"));
        Assert.Contains("1,125,000", warning);
        Assert.Contains("1,100,000", warning);
        Assert.Contains("25,000", warning);
    }

    /// <summary>
    /// Over budget must NOT block. The manager decides whether to absorb the
    /// overrun, seek a client variation, or reject â€” which is exactly why the
    /// human approval gate exists.
    /// </summary>
    [Fact]
    public async Task Over_budget_quotation_remains_valid_for_manager_decision()
    {
        var quotation = await AddQuotationAsync(2250m);

        var result = await NewValidator().ValidateRecommendationAsync(quotation.Id, _request.Id);

        Assert.True(result.IsValid, "An over-budget award must stay approvable by a manager.");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Budget_check_includes_transport_charge()
    {
        // 500 x 2,190 = 1,095,000 goods, plus 10,000 transport = 1,105,000,
        // which breaches the budget even though the goods total alone does not.
        var quotation = await AddQuotationAsync(2190m, transportCharge: 10_000m);

        var result = await NewValidator().ValidateRecommendationAsync(quotation.Id, _request.Id);

        Assert.Contains(result.Warnings, w => w.Contains("Budget exceeds planned amount"));
    }

    [Fact]
    public async Task No_budget_set_means_the_check_does_not_apply()
    {
        // Null means "no allocation recorded", not "a budget of zero".
        _project.MaterialBudgetAmount = null;
        await _db.SaveChangesAsync();

        var quotation = await AddQuotationAsync(999_999m);

        var result = await NewValidator().ValidateRecommendationAsync(quotation.Id, _request.Id);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("budget"));
    }

    [Fact]
    public async Task Quotation_expiring_within_a_week_is_flagged_as_incomplete()
    {
        var quotation = await AddQuotationAsync(2180m);
        quotation.ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        await _db.SaveChangesAsync();

        var result = await NewValidator().ValidateRecommendationAsync(quotation.Id, _request.Id);

        Assert.Contains(result.Warnings, w => w.Contains("expires in"));
    }

    [Fact]
    public async Task Missing_payment_terms_is_flagged_as_incomplete()
    {
        var quotation = await AddQuotationAsync(2180m);
        quotation.PaymentTerms = null;
        await _db.SaveChangesAsync();

        var result = await NewValidator().ValidateRecommendationAsync(quotation.Id, _request.Id);

        Assert.Contains(result.Warnings, w => w.Contains("no payment terms"));
    }
    // --- Budget endpoint access control -----------------------------------

    [Fact]
    public async Task Budget_is_readable_by_procurement_and_approvers()
    {
        foreach (var role in new[] { "ProcurementOfficer", "ProcurementManager", "SiteManager", "Administrator" })
        {
            using var client = _factory.CreateClientFor(role);
            using var response = await client.GetAsync($"/api/projects/{_project.Id}/budget");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(Budget, body.GetProperty("materialBudgetAmount").GetDecimal());
        }
    }

    [Theory]
    [InlineData("SiteEngineer")]
    [InlineData("SiteOfficer")]
    [InlineData("SiteOfficer")]
    [InlineData("QualityInspector")]
    public async Task Budget_is_not_readable_by_site_or_quality_roles(string role)
    {
        using var client = _factory.CreateClientFor(role);

        using var response = await client.GetAsync($"/api/projects/{_project.Id}/budget");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Budget_is_not_visible_to_a_legacy_supplier_token()
    {
        // A budget is commercial information. A supplier has no BuildWise
        // account at all, so a token carrying the legacy role reaches nothing.
        using var client = _factory.CreateClientFor("Supplier");

        using var response = await client.GetAsync($"/api/projects/{_project.Id}/budget");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Procurement_officer_may_not_change_the_budget()
    {
        using var client = _factory.CreateClientFor("ProcurementOfficer");

        using var response = await client.PutAsync(
            $"/api/projects/{_project.Id}/budget",
            new StringContent("""{"materialBudgetAmount":9999999}""",
                System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_can_set_the_budget_and_negative_values_are_refused()
    {
        using var manager = _factory.CreateClientFor("ProcurementManager");

        using var ok = await manager.PutAsync(
            $"/api/projects/{_project.Id}/budget",
            new StringContent("""{"materialBudgetAmount":2500000}""",
                System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var bad = await manager.PutAsync(
            $"/api/projects/{_project.Id}/budget",
            new StringContent("""{"materialBudgetAmount":-1}""",
                System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Project_list_does_not_leak_the_budget_to_every_internal_role()
    {
        // /api/projects is readable by all internal staff, so it must not carry
        // commercial budget information.
        using var client = _factory.CreateClientFor("SiteEngineer");

        var raw = await client.GetStringAsync("/api/projects");

        Assert.DoesNotContain("MaterialBudgetAmount", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1100000", raw, StringComparison.Ordinal);
    }
}
