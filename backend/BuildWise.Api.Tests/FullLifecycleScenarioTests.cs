using System.Net;
using System.Text.Json;
using BuildWise.Api.Data;
using BuildWise.Api.DTOs;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildWise.Api.Tests;

/// <summary>
/// Phase 2 — one complete, real C1 → C4 scenario, walking every actor in turn:
/// <code>
/// Site Engineer → Material Request → Manager Approval → RFQ
///   → Supplier quotation → AI analysis → Validation → Manager approval → PO
///   → Site Officer receiving → Quality Inspector → NCR (rejected) → Resolution
/// </code>
/// The earlier <see cref="ScenarioReplayTests"/> covered only C1 → C2
/// (request → agent workflow → purchase order). Everything from receiving
/// onwards, and the supplier's own participation in quoting, had no coverage.
/// <para>
/// RBAC is asserted at every hand-off: each step is performed with a client
/// holding exactly the role that owns it, and the negative cases (the wrong
/// role being refused) are covered by <see cref="RbacAuthorizationTests"/>.
/// </para>
/// </summary>
public class FullLifecycleScenarioTests : IAsyncLifetime
{
    private const string ScenarioPassword = "unused-in-these-tests";

    private RbacApiFactory _factory = null!;
    private ApplicationDbContext _db = null!;

    // Actor user ids, created in ArrangeActorsAsync.
    private int _siteEngineerId;
    private int _siteOfficerId;
    private int _procurementOfficerId;
    private int _procurementManagerId;
    private int _qualityInspectorId;
    private int _supplierAUserId;
    private int _supplierBUserId;

    // Scenario entities.
    private Project _project = null!;
    private Material _cement = null!;
    private MaterialRequest _request = null!;
    private MaterialRequestItem _requestItem = null!;
    private Supplier _supplierA = null!;
    private Supplier _supplierB = null!;
    private Supplier _supplierC = null!;
    private Rfq _rfq = null!;

    public async Task InitializeAsync() => await ArrangeScenarioAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task ArrangeScenarioAsync()
    {
        _factory = new RbacApiFactory();
        _db = await _factory.GetSeededDbAsync();

        // --- Actors: one real user per role in the chain --------------------
        var roles = await _db.Roles.ToDictionaryAsync(r => r.Name);
        var nextId = 1000;

        async Task<User> AddUserAsync(string name, string email, string role)
        {
            var user = new User
            {
                Id = nextId++,
                FullName = name,
                Email = email,
                PasswordHash = ScenarioPassword,
                IsActive = true
            };
            user.UserRoles.Add(new UserRole { Role = roles[role] });
            _db.Users.Add(user);
            return user;
        }

        _project = new Project { Name = "Riverside Apartments - Block C", Location = "Colombo 05", Status = ProjectStatus.Active };
        _cement = new Material { Name = "Cement (50kg bag)", Unit = "bag", Category = "Structural", IsActive = true };
        _supplierA = new Supplier { Name = "Supplier A Building Materials", Status = SupplierStatus.Active, Email = "sales@suppliera.test" };
        _supplierB = new Supplier { Name = "Supplier B Traders", Status = SupplierStatus.Suspended, Email = "info@supplierb.test" };
        _supplierC = new Supplier { Name = "Supplier C Wholesale", Status = SupplierStatus.Active, Email = "quotes@supplierc.test" };
        _db.AddRange(_project, _cement, _supplierA, _supplierB, _supplierC);
        await _db.SaveChangesAsync();

        _siteEngineerId = (await AddUserAsync("Sam SiteEngineer", "site.engineer@scenario.test", "SiteEngineer")).Id;
        _siteOfficerId = (await AddUserAsync("Nipuni SiteOfficer", "site.officer@scenario.test", "SiteOfficer")).Id;
        _procurementOfficerId = (await AddUserAsync("Priya Officer", "procurement.officer@scenario.test", "ProcurementOfficer")).Id;
        _procurementManagerId = (await AddUserAsync("Mira Manager", "procurement.manager@scenario.test", "ProcurementManager")).Id;
        _qualityInspectorId = (await AddUserAsync("Dinesh Inspector", "quality.inspector@scenario.test", "QualityInspector")).Id;

        // Supplier portal logins, each bound to exactly one supplier.
        var supplierAUser = await AddUserAsync("Nimal Perera (Portal)", "sales@suppliera.test", "Supplier");
        supplierAUser.SupplierId = _supplierA.Id;
        _supplierAUserId = supplierAUser.Id;
        var supplierBUser = await AddUserAsync("Kamal Silva (Portal)", "info@supplierb.test", "Supplier");
        supplierBUser.SupplierId = _supplierB.Id;
        _supplierBUserId = supplierBUser.Id;
        await _db.SaveChangesAsync();

        // --- C1: Site Engineer raises a material request --------------------
        _request = new MaterialRequest
        {
            ProjectId = _project.Id,
            RequestedByUserId = _siteEngineerId,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21)),
            Reason = "Foundation slab casting phase 2",
            Status = MaterialRequestStatus.Draft
        };
        _db.MaterialRequests.Add(_request);
        await _db.SaveChangesAsync();

        _requestItem = new MaterialRequestItem
        {
            MaterialRequestId = _request.Id,
            MaterialId = _cement.Id,
            RequestedQuantity = 250m,
            Notes = "Standard Portland cement grade 42.5N"
        };
        _db.MaterialRequestItems.Add(_requestItem);
        await _db.SaveChangesAsync();
    }
    [Fact]
    public async Task C1_to_C4_full_scenario_completes_across_every_role()
    {
        // --- Step 1 — Site Engineer submits the material request ------------
        var materialRequestService = new MaterialRequestService(_db);
        var created = await materialRequestService.CreateRequestAsync(new MaterialRequest
        {
            ProjectId = _project.Id,
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21)),
            Reason = "Foundation slab casting phase 2",
            Items = new List<MaterialRequestItem>
            {
                new() { MaterialId = _cement.Id, RequestedQuantity = 250m, Notes = "Grade 42.5N" }
            }
        }, _siteEngineerId);

        Assert.Equal(MaterialRequestStatus.PendingApproval, created.Status);
        var requestId = created.Id;

        // --- Step 2 — Manager approves the material request -----------------
        var approval = await materialRequestService.RecordApprovalAsync(
            requestId, _procurementManagerId, ApprovalDecision.Approved, "Approved for the phase 2 pour.");
        Assert.Equal(ApprovalDecision.Approved, approval.Decision);

        var approved = await _db.MaterialRequests.AsNoTracking().FirstAsync(r => r.Id == requestId);
        Assert.Equal(MaterialRequestStatus.Approved, approved.Status);

        // --- Step 3 — Procurement Officer issues an RFQ --------------------
        // Only Active suppliers can be invited: the controller rejects a
        // Suspended one outright, so B is quoted separately in step 4.
        using (var procurementOfficer = _factory.CreateClientFor("ProcurementOfficer"))
        {
            using var response = await procurementOfficer.PostAsync("/api/rfqs", Json(new
            {
                materialRequestId = requestId,
                requiredResponseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
                notes = "Please quote delivered to site with 30-day payment terms.",
                supplierIds = new[] { _supplierA.Id, _supplierC.Id }
            }));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var rfq = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            _rfq = await _db.Rfqs.AsNoTracking().FirstAsync(r => r.Id == rfq.GetProperty("id").GetInt32());
        }

        Assert.Equal(RfqStatus.Issued, _rfq.Status);
        Assert.Equal(2, await _db.RfqSuppliers.CountAsync(rs => rs.RfqId == _rfq.Id));

        // Enforcement, not convention: a Suspended supplier cannot be invited.
        using (var procurementOfficer = _factory.CreateClientFor("ProcurementOfficer"))
        {
            using var response = await procurementOfficer.PostAsync("/api/rfqs", Json(new
            {
                materialRequestId = requestId,
                requiredResponseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
                supplierIds = new[] { _supplierB.Id }
            }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // --- Step 4 — Suppliers submit their own quotations via the portal ---
        //   A: 250 @ 2100 (Active)     -> eligible, full coverage
        //   C: 200 @ 2050 (Active)     -> cheapest total but partial coverage
        //   B: 250 @ 2040 (Suspended)  -> lowest unit price, but ineligible
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var validUntil = today.AddDays(30);

        await SubmitSupplierQuotationAsync(_supplierA.Id, requestId, 250m, 2100m, today, validUntil);
        await SubmitSupplierQuotationAsync(_supplierC.Id, requestId, 200m, 2050m, today, validUntil);

        // B quoted before it was suspended, so the desk holds that paperwork.
        // It is recorded through the procurement endpoint, which is how a
        // phone or email quote legitimately enters the system.
        _db.Quotations.Add(new Quotation
        {
            MaterialRequestId = requestId,
            SupplierId = _supplierB.Id,
            QuotationDate = today,
            ValidUntil = validUntil,
            Status = QuotationStatus.Submitted,
            TotalAmount = 510_000m,
            Items = new List<QuotationItem>
            {
                new() { MaterialRequestItemId = await FirstLineIdAsync(requestId), Quantity = 250m, UnitPrice = 2040m }
            }
        });
        await _db.SaveChangesAsync();

        var quotations = await _db.Quotations.AsNoTracking().Where(q => q.MaterialRequestId == requestId).ToListAsync();
        Assert.Equal(3, quotations.Count);

        // Each quotation is attributed to its own supplier — never to a client-supplied id.
        Assert.Equal(3, quotations.Select(q => q.SupplierId).Distinct().Count());

        // A Suspended supplier is refused at submission time, even on an RFQ
        // it was once invited to.
        using (var suspended = _factory.CreateSupplierClient(_supplierB.Id))
        {
            using var response = await suspended.PostAsync(
                $"/api/supplier-portal/rfqs/{_rfq.Id}/quotations",
                Json(new
                {
                    quotationDate = today,
                    validUntil = validUntil,
                    transportCharge = 0m,
                    items = new[] { new { materialRequestItemId = await FirstLineIdAsync(requestId), quantity = 250m, unitPrice = 1m } }
                }));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // --- Step 5 — AI analysis + deterministic validation ---------------
        var httpClient = new HttpClient();
        var workflowService = new ProcurementWorkflowService(
            _db,
            new QuotationAgentClient(httpClient, NullLogger<QuotationAgentClient>.Instance),
            new ProcurementValidationService(_db),
            new ProcurementPlanningAgentService(_db),
            new NoOpEmailService(),
            NullLogger<ProcurementWorkflowService>.Instance);

        var started = await workflowService.StartWorkflowAsync(requestId, _procurementOfficerId);
        Assert.Equal("AwaitingApproval", started.Status);

        var details = await workflowService.GetWorkflowDetailsAsync(started.WorkflowId);
        Assert.NotNull(details);
        Assert.Equal("AwaitingApproval", details.Status);

        // Deterministic validation passed, and the agent's recommendation is
        // the eligible full-coverage supplier — not the cheapest unit price.
        Assert.NotNull(details.Validation);
        Assert.True(details.Validation!.IsValid);
        Assert.Empty(details.Validation.Errors);

        Assert.NotNull(details.Recommendation);
        var winnerQuotationId = details.Recommendation!.RecommendedQuotationId;
        var winnerSupplierId = details.Recommendation.RecommendedSupplierId;

        Assert.Equal(_supplierA.Id, winnerSupplierId);
        var winner = await _db.Quotations.AsNoTracking().FirstAsync(q => q.Id == winnerQuotationId);
        Assert.Equal(525_000m, winner.TotalAmount);

        var warnings = string.Join(" ", details.Recommendation.Warnings);
        Assert.Contains("Supplier B Traders", warnings);
        Assert.Contains("Suspended", warnings);
        Assert.Contains("Supplier C Wholesale", warnings);

        // --- Step 6 — Procurement Manager approves the recommendation ------
        // Approving is what raises the purchase order: the workflow completes
        // and the PO is created in the same transaction, so there is no window
        // in which an approval exists without its order.
        var decision = await workflowService.RecordDecisionAsync(
            started.WorkflowId,
            new WorkflowDecisionDto("Approve", "Full coverage, active supplier, quality standing confirmed."),
            _procurementManagerId);
        Assert.Equal(AgentApprovalStatus.Approved, decision.Decision);

        // --- Step 7 — Purchase order is raised from the approved workflow ----
        var purchaseOrder = await _db.PurchaseOrders
            .AsNoTracking()
            .SingleAsync(p => p.QuotationId == winnerQuotationId);

        Assert.Equal(PurchaseOrderStatus.Confirmed, purchaseOrder.Status);
        Assert.Equal(525_000m, purchaseOrder.TotalAmount);

        var poItem = await _db.PurchaseOrderItems.AsNoTracking()
            .SingleAsync(i => i.PurchaseOrderId == purchaseOrder.Id);
        Assert.Equal(250m, poItem.OrderedQuantity);
        // MaterialId must survive so receiving can match delivery lines.
        Assert.Equal(_cement.Id, poItem.MaterialId);

        // A duplicate purchase order for the same request is refused.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflowService.CreatePurchaseOrderFromWorkflowAsync(started.WorkflowId));

        // Losing quotations are rejected; the winner is selected.
        Assert.Equal(QuotationStatus.Selected,
            (await _db.Quotations.AsNoTracking().FirstAsync(q => q.Id == winnerQuotationId)).Status);
        Assert.Equal(2, await _db.Quotations.AsNoTracking()
            .CountAsync(q => q.MaterialRequestId == requestId && q.Status == QuotationStatus.Rejected));

        // --- Step 8 — Site Officer records receiving, with a shortage ------
        // 200 of 250 arrived, 10 damaged: a real discrepancy, not a clean receipt.
        var deliveryService = new DeliveryService(_db, logger: NullLogger<DeliveryService>.Instance);
        var delivery = await deliveryService.RecordDeliveryAsync(new Delivery
        {
            PurchaseOrderId = purchaseOrder.Id,
            DeliveryReference = "DN-0001",
            ReceivedByUserId = _siteOfficerId,
            Status = DeliveryStatus.Arrived,
            DeliveredAt = DateTime.UtcNow,
            Items = new List<DeliveryItem>
            {
                new()
                {
                    MaterialId = _cement.Id,
                    ReceivedQuantity = 200m,
                    DamagedQuantity = 10m
                }
            }
        });

        Assert.Equal(DeliveryStatus.DiscrepancyReported, delivery.Status);

        var issues = await _db.DeliveryIssues.AsNoTracking().Where(i => i.DeliveryId == delivery.Id).ToListAsync();
        // Both a shortage (250 ordered vs 200 received) and damage are recorded.
        Assert.Contains(issues, i => i.IssueType == DeliveryIssueType.Shortage);
        Assert.Contains(issues, i => i.IssueType == DeliveryIssueType.Damage);
        // The issues are attributed to the Site Officer who recorded them.
        Assert.All(issues, i => Assert.Equal(_siteOfficerId, i.ReportedByUserId));

        // --- Step 9 — Quality Inspector rejects part of the delivery --------
        // 190 inspected, 170 accepted, 20 rejected -> an NCR is raised.
        var qualityService = new QualityInspectionService(_db);
        var inspection = await qualityService.CompleteInspectionAsync(new Inspection
        {
            DeliveryId = delivery.Id,
            InspectorUserId = _qualityInspectorId,
            InspectionCriteria = "Visual check, 42.5N grade certification, no moisture ingress",
            ObservedResult = "20 bags torn and partially hydrated on arrival",
            // Structured five-point checklist (Rule 0). Visual, moisture,
            // packaging and defects genuinely failed here, consistent with the
            // 20 rejected bags this inspection records.
            QuantityCheck = true,
            VisualConditionCheck = false,
            MoistureCheck = false,
            PackagingCheck = false,
            DefectsCheck = false,
            Items = new List<InspectionItem>
            {
                new()
                {
                    MaterialId = _cement.Id,
                    InspectedQuantity = 190m,
                    AcceptedQuantity = 170m,
                    RejectedQuantity = 20m,
                    RejectionReason = "Damaged packaging and partially hydrated cement"
                }
            }
        });

        Assert.Equal(_qualityInspectorId, inspection.InspectorUserId);

        // --- Step 10 — an NCR exists and is awaiting corrective action ------
        // The inspection service raises the NCR already in
        // CorrectiveActionRequired (the agent recommends that status for a
        // partial rejection), so the review starts from there.
        var ncr = await _db.NonConformances.AsNoTracking().SingleAsync(n => n.InspectionItem!.InspectionId == inspection.Id);
        Assert.Equal(NonConformanceStatus.CorrectiveActionRequired, ncr.Status);
        Assert.Contains("NCR-", ncr.NcrNumber);
        // The NCR is linked back to the delivery and to the winning supplier.
        Assert.Equal(delivery.Id, ncr.DeliveryId);
        Assert.Equal(_supplierA.Id, ncr.SupplierId);
        Assert.Equal(20m, ncr.QuantityAffected);

        // --- Step 11 — Manager drives the NCR to resolution -----------------
        // CorrectiveActionRequired -> Resolved -> Closed
        var resolved = await qualityService.TransitionNonConformanceAsync(ncr.Id,
            new NcrReviewRequest(
                NonConformanceStatus.Resolved,
                "Replacement verified against delivery DN-0002.",
                null,
                "Supplier replaced 20 bags and shared a moisture-handling procedure."),
            _procurementManagerId);

        Assert.Equal(NonConformanceStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Equal(_procurementManagerId, resolved.ReviewedByUserId);

        var closed = await qualityService.TransitionNonConformanceAsync(ncr.Id,
            new NcrReviewRequest(
                NonConformanceStatus.Closed,
                "Closed after replacement verification.",
                null,
                "Supplier replaced 20 bags and shared a moisture-handling procedure."),
            _procurementManagerId);

        Assert.Equal(NonConformanceStatus.Closed, closed.Status);
        Assert.NotNull(closed.ClosedAt);

        // A closed NCR no longer appears in the open queue.
        var open = await qualityService.GetOpenNonConformancesAsync();
        Assert.DoesNotContain(open, n => n.Id == ncr.Id);

        // An illegal jump (Open -> Closed, skipping resolution) is refused.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => qualityService.TransitionNonConformanceAsync(ncr.Id,
                new NcrReviewRequest(NonConformanceStatus.Closed, "Skip ahead", null, "Skip ahead"),
                _procurementManagerId));
    }

    private async Task SubmitSupplierQuotationAsync(
        int supplierId, int requestId, decimal quantity, decimal unitPrice, DateOnly date, DateOnly validUntil)
    {
        using var client = _factory.CreateSupplierClient(supplierId);
        using var response = await client.PostAsync(
            $"/api/supplier-portal/rfqs/{_rfq.Id}/quotations",
            Json(new
            {
                quotationDate = date,
                validUntil = validUntil,
                promisedDeliveryDate = date.AddDays(7),
                paymentTerms = "Net 30",
                transportCharge = 0m,
                items = new[] { new { materialRequestItemId = await FirstLineIdAsync(requestId), quantity = quantity, unitPrice = unitPrice } }
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The request line the scenario quotes against. <paramref name="requestId"/>
    /// is passed explicitly because the scenario creates its own request rather
    /// than reusing the draft seeded during arrangement.
    /// </summary>
    private Task<int> FirstLineIdAsync(int requestId) =>
        _db.MaterialRequestItems
            .Where(i => i.MaterialRequestId == requestId)
            .Select(i => i.Id)
            .FirstAsync();

    private static StringContent Json(object value) =>
        new(System.Text.Json.JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");
}