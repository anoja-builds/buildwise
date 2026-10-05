namespace BuildWise.Api.Security;

/// <summary>
/// Single source of truth for role and policy names.
/// Every <c>[Authorize]</c> attribute and every policy registration references
/// these constants, so a role rename can never silently drift out of sync with
/// the policies that grant it (Phase 1 RBAC hardening).
/// </summary>
public static class Roles
{
    public const string Administrator = "Administrator";
    public const string ProjectManager = "ProjectManager";
    public const string SiteEngineer = "SiteEngineer";
    public const string SiteOfficer = "SiteOfficer";
    public const string SiteManager = "SiteManager";
    public const string ProcurementOfficer = "ProcurementOfficer";
    public const string ProcurementManager = "ProcurementManager";
    public const string ReceivingOfficer = "ReceivingOfficer";
    public const string QualityInspector = "QualityInspector";

    /// <summary>External supplier portal user. Bound to exactly one supplier
    /// record through <c>User.SupplierId</c> and surfaced as a JWT claim.</summary>
    public const string Supplier = "Supplier";

    // --- Composite role sets -------------------------------------------------
    // SiteOfficer is the receiving-facing alias of SiteEngineer; SiteManager is
    // the approval-facing alias of ProcurementManager.

    public const string SiteOperations = SiteEngineer + "," + SiteOfficer;
    public const string SiteOperationsAndAdmin = SiteOperations + "," + Administrator;
    public const string ProcurementStaff = ProcurementOfficer + "," + ProcurementManager;
    public const string ProcurementStaffAndAdmin = ProcurementStaff + "," + SiteManager + "," + Administrator;
    public const string ProcurementManagers = ProcurementManager + "," + SiteManager;
    public const string ProcurementDecisionMakers = ProcurementManagers + "," + Administrator;

    /// <summary>Roles that create/own RFQs and supplier master data.</summary>
    public const string SupplierAdministration = ProcurementOfficer + "," + Administrator;

    /// <summary>Roles allowed to read quality records. Delivery and site roles are
    /// included because they consume inspection outcomes; pricing is never
    /// exposed to any of them (see <c>PurchaseOrderProjection</c>).</summary>
    public const string QualityReaders = SiteOperations + "," + ProcurementManagers
        + "," + QualityInspector + "," + Administrator;

    /// <summary>Roles permitted to act on the delivery / receiving surface.
    /// Procurement Officers are included so they can track PO fulfilment.</summary>
    public const string DeliveryParticipants = SiteOperations + "," + ReceivingOfficer
        + "," + QualityInspector + "," + ProcurementOfficer + "," + ProcurementManagers + "," + Administrator;
}

/// <summary>Authorization policy names registered in <c>Program.cs</c>.</summary>
public static class Policies
{
    /// <summary>Site Engineer / Site Officer — may raise and revise material requests.</summary>
    public const string SiteOperationsOnly = "SiteOperationsOnly";

    /// <summary>Procurement desk — may maintain suppliers, RFQs and quotations.</summary>
    public const string ProcurementStaffOnly = "ProcurementStaffOnly";

    /// <summary>Procurement desk — may create and maintain suppliers, RFQs, quotations.</summary>
    public const string SupplierAdministrationOnly = "SupplierAdministrationOnly";

    /// <summary>
    /// Procurement readers and managers. Grants read access to suppliers, RFQs,
    /// quotations, agent workflows and purchase orders. This is the only policy
    /// that can reach commercial data.
    /// </summary>
    public const string ProcurementStaffAndAdmin = "ProcurementStaffAndAdmin";

    /// <summary>
    /// Roles permitted to read quality inspections and non-conformance records.
    /// </summary>
    public const string QualityReaders = "QualityReaders";

    /// <summary>Quality control — may complete inspections and raise NCRs.</summary>
    public const string QualityControlOnly = "QualityControlOnly";

    /// <summary>Approvers — may approve material requests and procurement recommendations.</summary>
    public const string MaterialRequestApprovalOnly = "MaterialRequestApprovalOnly";

    /// <summary>Approvers — may accept or reject a procurement recommendation.</summary>
    public const string ProcurementDecisionOnly = "ProcurementDecisionOnly";

    /// <summary>
    /// Roles that may read material requests. Site roles are additionally
    /// narrowed to their own rows by <c>MaterialRequestsController</c>; this
    /// policy only establishes which functions may read the resource at all.
    /// </summary>
    public const string MaterialRequestReaders = "MaterialRequestReaders";

    /// <summary>Every authenticated internal staff member (excludes Supplier portal users).</summary>
    public const string InternalStaffOnly = "InternalStaffOnly";

    /// <summary>
    /// Roles permitted to read the delivery / receiving surface: site, receiving,
    /// quality and procurement management. Excludes Supplier portal users, who use
    /// their own supplier-scoped endpoints instead.
    /// </summary>
    public const string DeliveryParticipantsOnly = "DeliveryParticipantsOnly";

    /// <summary>External supplier portal users only.</summary>
    public const string SupplierPortalOnly = "SupplierPortalOnly";
}

/// <summary>
/// Helpers for reading the caller's roles and their bound supplier identity out
/// of the validated JWT. Centralised so every controller scopes data the same way.
/// Named <c>CallerScope</c> rather than <c>PrincipalExtensions</c> to avoid
/// colliding with <c>System.Security.Claims.PrincipalExtensions</c>.
/// </summary>
public static class CallerScope
{
    public const string SupplierIdClaim = "supplier_id";

    public static IReadOnlyList<string> GetRoleNames(this System.Security.Claims.ClaimsPrincipal principal) =>
        principal.FindAll(System.Security.Claims.ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static bool IsInAnyRole(this System.Security.Claims.ClaimsPrincipal principal, params string[] roles) =>
        principal.GetRoleNames().Any(roles.Contains);

    /// <summary>
    /// Returns the supplier this caller is bound to, or <c>null</c> when the caller
    /// is not a supplier portal user. Supplier-scoped endpoints must call this and
    /// reject <c>null</c> — never accept a supplier id from the request body.
    /// </summary>
    public static int? GetBoundSupplierId(this System.Security.Claims.ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst(SupplierIdClaim)?.Value
                  ?? principal.FindFirst("supplierId")?.Value;
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }
}