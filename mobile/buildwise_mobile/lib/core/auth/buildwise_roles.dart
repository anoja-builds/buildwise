/// Role vocabulary and screen gating for the Flutter shell.
///
/// Mirrors backend `BuildWise.Api/Security/Roles.cs` and the React
/// `auth/accessControl.js` table. All three must agree: the API enforces
/// access, these tables decide what to *show*, and a mismatch means a user
/// either sees a screen that 403s or is missing one they are entitled to.
class BuildWiseRoles {
  const BuildWiseRoles._();

  static const String administrator = 'Administrator';
  static const String projectManager = 'ProjectManager';
  static const String siteEngineer = 'SiteEngineer';
  static const String siteOfficer = 'SiteOfficer';
  static const String siteManager = 'SiteManager';
  static const String procurementOfficer = 'ProcurementOfficer';
  static const String procurementManager = 'ProcurementManager';
  static const String receivingOfficer = 'ReceivingOfficer';
  static const String qualityInspector = 'QualityInspector';
  static const String supplier = 'Supplier';

  /// Roles that raise and revise material requests.
  static const Set<String> siteOperations = {siteEngineer, siteOfficer};

  /// Roles allowed to see purchase order commercial terms. The API redacts
  /// these server-side; the shell mirrors the rule so a receiving or quality
  /// user is never shown a money column.
  static const Set<String> commercial = {
    procurementOfficer,
    procurementManager,
    siteManager,
    administrator,
  };

  static const Set<String> internalStaff = {
    siteEngineer,
    siteOfficer,
    procurementOfficer,
    procurementManager,
    siteManager,
    receivingOfficer,
    qualityInspector,
    projectManager,
    administrator,
  };

  static bool hasAny(Iterable<String> roles, Set<String> allowed) =>
      allowed.any(roles.contains);

  static bool isSite(Iterable<String> roles) => hasAny(roles, siteOperations);

  static bool isSupplier(Iterable<String> roles) => roles.contains(supplier);

  static bool isInternalStaff(Iterable<String> roles) =>
      hasAny(roles, internalStaff);

  static bool canSeeCommercialTerms(Iterable<String> roles) =>
      hasAny(roles, commercial);
}