using System.Net;
using BuildWise.Api.Models.Entities;
using BuildWise.Api.Models.Enums;
using BuildWise.Api.Security;
using Xunit;

namespace BuildWise.Api.Tests;

/// <summary>
/// Phase 1 RBAC regression suite.
/// <para>
/// Two guarantees are asserted for every sensitive endpoint:
/// <list type="number">
/// <item><b>401</b> for an unauthenticated caller — the endpoint is never anonymous.</item>
/// <item><b>403</b> for an authenticated caller holding the wrong role.</item>
/// </list>
/// Purchase order redaction and supplier portal isolation are covered by
/// dedicated tests alongside this matrix.
/// </para>
/// </summary>
public class RbacAuthorizationTests : IClassFixture<RbacApiFactory>
{
    private readonly RbacApiFactory _factory;

    public RbacAuthorizationTests(RbacApiFactory factory) => _factory = factory;

    /// <summary>
    /// Every sensitive endpoint paired with a role that must NOT reach it.
    /// Kept as explicit data so that adding a new endpoint forces a decision
    /// here rather than silently shipping an unauthenticated surface.
    /// </summary>
    public static TheoryData<string, string, string> ForbiddenEndpoints => new()
    {
        // method, path, role that must be rejected

        // --- Purchase orders: the Phase 1 information-exposure fix ---------
        // Note: QualityInspector/SiteEngineer/SiteOfficer are deliberately
        // *allowed* to read purchase orders — they need the material, quantity
        // and status to receive and inspect. They are denied the commercial
        // fields instead, which PurchaseOrderRedactionTests asserts directly.
        { "GET",    "/api/purchase-orders/1",                       "Supplier" },
        { "GET",    "/api/purchase-orders",                          "Supplier" },
        { "PATCH",  "/api/purchase-orders/1/status",                 "SiteOfficer" },
        { "PATCH",  "/api/purchase-orders/1/status",                 "QualityInspector" },
        { "POST",   "/api/procurement-workflow/1/purchase-order",    "ProcurementOfficer" },

        // --- Supplier master data -----------------------------------------
        { "GET",    "/api/suppliers",                                "SiteEngineer" },
        { "POST",   "/api/suppliers",                                "ProcurementManager" },
        { "PUT",    "/api/suppliers/1",                              "QualityInspector" },
        { "PATCH",  "/api/suppliers/1/status",                       "SiteOfficer" },

        // --- RFQ ----------------------------------------------------------
        { "GET",    "/api/rfqs",                                     "SiteEngineer" },
        { "POST",   "/api/rfqs",                                     "ProcurementManager" },
        { "POST",   "/api/rfqs/1/suppliers",                         "ProcurementManager" },
        { "POST",   "/api/rfqs/1/close",                             "ProcurementOfficer" },

        // --- Quotations ---------------------------------------------------
        { "GET",    "/api/material-requests/1/quotations",           "SiteEngineer" },
        { "POST",   "/api/material-requests/1/quotations",           "SiteOfficer" },
        { "GET",    "/api/material-requests/1/quotations/compare",    "QualityInspector" },
        { "GET",    "/api/quotations/1",                             "SiteOfficer" },

        // --- Agent workflows & approvals ----------------------------------
        { "GET",    "/api/agent-workflows",                          "SiteEngineer" },
        { "GET",    "/api/agent-workflows/1",                        "QualityInspector" },
        { "POST",   "/api/material-requests/1/procurement-workflow",  "SiteEngineer" },
        { "POST",   "/api/procurement-workflow/1/decision",          "ProcurementOfficer" },

        // --- Administration -----------------------------------------------
        { "GET",    "/api/admin/users",                              "ProcurementManager" },
        { "PUT",    "/api/admin/users/1/roles",                      "QualityInspector" },
        { "GET",    "/api/admin/audit-logs",                         "SiteEngineer" },

        // --- Quality ------------------------------------------------------
        { "POST",   "/api/quality-inspections",                      "SiteEngineer" },
        { "POST",   "/api/quality-inspections",                      "ProcurementOfficer" },
        { "POST",   "/api/quality-inspections/non-conformances/1/transition", "QualityInspector" },
        { "PUT",    "/api/quality-inspections/non-conformances/1/status",     "SiteOfficer" },

        // --- Deliveries ---------------------------------------------------
        { "POST",   "/api/deliveries",                               "ProcurementOfficer" },
        { "GET",    "/api/deliveries/confirmed-orders",              "Supplier" },
        { "GET",    "/api/deliveries/expected",                      "Supplier" },

        // --- Shared internal surfaces: closed to the supplier portal -----
        { "GET",    "/api/dashboard",                                "Supplier" },
        { "GET",    "/api/notifications",                            "Supplier" },
        { "GET",    "/api/materials",                                "Supplier" },
        { "GET",    "/api/projects",                                 "Supplier" },

        // --- Supplier portal: closed to every internal role --------------
        { "GET",    "/api/supplier-portal/profile",                  "ProcurementOfficer" },
        { "GET",    "/api/supplier-portal/rfqs",                     "QualityInspector" },
        { "POST",   "/api/supplier-portal/rfqs/1/quotations",        "SiteEngineer" },
        { "GET",    "/api/supplier-portal/purchase-orders",          "ProcurementManager" },
    };
    [Theory]
    [MemberData(nameof(ForbiddenEndpoints))]
    public async Task Sensitive_endpoint_rejects_unauthenticated_caller(string method, string path, string role)
    {
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(BuildRequest(method, path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ForbiddenEndpoints))]
    public async Task Sensitive_endpoint_rejects_wrong_role(string method, string path, string role)
    {
        using var client = _factory.CreateClientFor(role);

        using var response = await client.SendAsync(BuildRequest(method, path));

        // 403 (authenticated but unauthorised) and 404 (row deliberately not
        // visible to this role) are both valid denials. 200/401 are not.
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"{method} {path} as {role} returned {(int)response.StatusCode} {response.StatusCode}; expected 403 or 404.");
    }

    [Fact]
    public async Task Public_endpoints_remain_reachable_without_a_token()
    {
        using var client = _factory.CreateClient();

        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        // /api/auth/me is the canonical "must be authenticated" probe.
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Authenticated_caller_with_correct_role_passes_authorization()
    {
        // Reaching the action (not 401/403) is what matters: an empty in-memory
        // database yields 404 or 200, both of which prove authorize succeeded.
        using var client = _factory.CreateClientFor(Roles.ProcurementOfficer);

        using var response = await client.GetAsync("/api/suppliers");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Administrator)]
    [InlineData(Roles.SiteEngineer)]
    [InlineData(Roles.SiteOfficer)]
    [InlineData(Roles.ReceivingOfficer)]
    [InlineData(Roles.QualityInspector)]
    [InlineData(Roles.SiteManager)]
    [InlineData(Roles.ProjectManager)]
    public async Task Supplier_portal_is_closed_to_every_internal_role(string role)
    {
        using var client = _factory.CreateClientFor(role);

        using var response = await client.GetAsync("/api/supplier-portal/profile");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.SiteEngineer)]
    [InlineData(Roles.SiteOfficer)]
    public async Task Site_role_can_read_a_material_request_raised_by_a_colleague(string role)
    {
        // Regression: the site queue was filtered by RequestedByUserId, so a
        // Site Officer who had never raised a request saw an empty list and MR-58
        // was invisible to them no matter how often the page refreshed. Site roles
        // share one queue, so read scope covers the whole team.
        var requestId = await SeedRequestOwnedByAsync(ownerUserId: 2);
        using var client = _factory.CreateClientForUser(userId: 3, role);

        using var detail = await client.GetAsync($"/api/material-requests/{requestId}");
        using var history = await client.GetAsync($"/api/material-requests/{requestId}/history");

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        // History previously bypassed the scope check entirely, so any site user
        // could read another user's decision trail while the request 404'd.
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
    }

    [Fact]
    public async Task Site_role_list_includes_requests_they_did_not_raise()
    {
        var requestId = await SeedRequestOwnedByAsync(ownerUserId: 2);
        using var client = _factory.CreateClientForUser(userId: 3, Roles.SiteOfficer);

        using var response = await client.GetAsync("/api/material-requests/my");
        var ids = await ReadIdsAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(requestId, ids);
    }

    [Theory]
    [InlineData(Roles.SiteEngineer)]
    [InlineData(Roles.SiteOfficer)]
    public async Task Site_role_cannot_submit_a_colleagues_request(string role)
    {
        // Read visibility must never imply write access: a site user may only
        // change a request they raised themselves.
        var requestId = await SeedRequestOwnedByAsync(ownerUserId: 2);
        using var client = _factory.CreateClientForUser(userId: 3, role);

        using var response = await client.PostAsync($"/api/material-requests/{requestId}/submit", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Site_role_can_submit_their_own_request()
    {
        // The owner must not be locked out by the write guard.
        var requestId = await SeedRequestOwnedByAsync(ownerUserId: 3);
        using var client = _factory.CreateClientForUser(userId: 3, Roles.SiteOfficer);

        using var response = await client.PostAsync($"/api/material-requests/{requestId}/submit", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Inserts one Draft request owned by <paramref name="ownerUserId"/> plus the
    /// project/material it needs, and returns the new request id.
    /// </summary>
    private async Task<int> SeedRequestOwnedByAsync(int ownerUserId)
    {
        var db = await _factory.GetSeededDbAsync();

        var project = new Project
        {
            Name = $"Scope project {ownerUserId}",
            Status = ProjectStatus.Active
        };
        db.Projects.Add(project);

        var material = new Material { Name = "Scope Cement", IsActive = true };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var request = new MaterialRequest
        {
            ProjectId = project.Id,
            RequestedByUserId = ownerUserId,
            RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
            RequiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Reason = "Scope test request",
            Priority = MaterialRequestPriority.Normal,
            Status = MaterialRequestStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        request.Items.Add(new MaterialRequestItem
        {
            MaterialId = material.Id,
            RequestedQuantity = 10,
            Unit = "bags"
        });
        db.MaterialRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private static async Task<List<int>> ReadIdsAsync(HttpResponseMessage response)
    {
        // Read the ids with JsonDocument rather than deserialising into a probe
        // type: the API serialises camelCase, so a POCO would need matching
        // attributes and would silently bind nothing if the DTO ever changes.
        var payload = await response.Content.ReadAsStringAsync();
        using var document = System.Text.Json.JsonDocument.Parse(payload);
        return document.RootElement
            .EnumerateArray()
            .Select(element => element.GetProperty("id").GetInt32())
            .ToList();
    }

    [Fact]
    public async Task Internal_surfaces_are_closed_to_a_supplier_portal_token()
    {
        // Even a correctly-bound supplier token must not reach internal data.
        using var client = _factory.CreateSupplierClient(supplierId: 1);

        foreach (var path in new[]
                 {
                     "/api/suppliers",
                     "/api/purchase-orders",
                     "/api/rfqs",
                     "/api/agent-workflows",
                     "/api/deliveries",
                     "/api/dashboard",
                     "/api/notifications",
                     "/api/materials",
                     "/api/projects",
                     "/api/admin/users"
                 })
        {
            using var response = await client.GetAsync(path);
            Assert.True(
                response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
                $"Supplier token reached {path} with {(int)response.StatusCode}.");
        }
    }

    private static HttpRequestMessage BuildRequest(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StringContent(
                """{"status":"Confirmed"}""",
                System.Text.Encoding.UTF8,
                "application/json");
        }
        return request;
    }
}