// Single source of truth for role names, mirroring backend
// BuildWise.Api/Security/Roles.cs. Keeping the vocabulary here stops the route
// guards, the navigation filter and the default-landing logic from drifting
// apart - they previously lived in two duplicated lists that could disagree.

export const ROLES = {
  Administrator: 'Administrator',
  ProjectManager: 'ProjectManager',
  SiteEngineer: 'SiteEngineer',
  SiteOfficer: 'SiteOfficer',
  SiteManager: 'SiteManager',
  ProcurementOfficer: 'ProcurementOfficer',
  ProcurementManager: 'ProcurementManager',
  ReceivingOfficer: 'ReceivingOfficer',
  QualityInspector: 'QualityInspector',
  Supplier: 'Supplier',
}

export const ROLE_GROUPS = {
  site: [ROLES.SiteEngineer, ROLES.SiteOfficer],
  procurement: [ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.SiteManager],
  manager: [ROLES.ProcurementManager, ROLES.SiteManager],
  quality: [ROLES.QualityInspector],
  administrator: [ROLES.Administrator],
  supplier: [ROLES.Supplier],
}

// Roles allowed to see purchase order commercial terms (unit price, order total).
// The API redacts these server-side; the client mirrors the same rule so the UI
// never renders a column the caller is not entitled to.
export const COMMERCIAL_ROLES = [
  ROLES.ProcurementOfficer,
  ROLES.ProcurementManager,
  ROLES.SiteManager,
  ROLES.Administrator,
]

export const INTERNAL_STAFF = [
  ROLES.SiteEngineer, ROLES.SiteOfficer,
  ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.SiteManager,
  ROLES.ReceivingOfficer, ROLES.QualityInspector,
  ROLES.ProjectManager, ROLES.Administrator,
]

export const hasAnyRole = (roles = [], allowed = []) =>
  allowed.some((role) => roles.includes(role))

export const isInternalStaff = (roles = []) => hasAnyRole(roles, INTERNAL_STAFF)

export const isSupplier = (roles = []) => hasAnyRole(roles, [ROLES.Supplier])

export const canSeeCommercialTerms = (roles = []) => hasAnyRole(roles, COMMERCIAL_ROLES)
/**
 * The single navigation table. App.jsx builds its routes from this and
 * AppLayout renders its menu from this, so route guards and the visible menu
 * can never disagree about who can reach what.
 */
export const NAVIGATION = [
  { path: '/dashboard', label: 'Dashboard', screen: 'Dashboard', section: 'Dashboard', roles: INTERNAL_STAFF },
  { path: '/material-requests', label: 'Material Requests', screen: 'Material Requests', roles: [ROLES.SiteEngineer, ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.QualityInspector, ROLES.Administrator] },
  { path: '/suppliers', label: 'Suppliers', screen: 'Procurement', section: 'Suppliers', roles: [ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.Administrator] },
  { path: '/quotations', label: 'Quotations', screen: 'Procurement', section: 'Approved Requests', roles: [ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.Administrator] },
  { path: '/rfqs', label: 'RFQs', screen: 'RFQs', section: 'RFQs', roles: [ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.Administrator] },
  { path: '/procurement', label: 'Procurement Workspace', screen: 'Procurement', section: 'Dashboard', roles: [ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.Administrator] },
  { path: '/agent-workflows', label: 'Agent Workflows', screen: 'Agent Workflows', roles: [ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.SiteManager, ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.QualityInspector, ROLES.Administrator] },
  { path: '/purchase-orders', label: 'Purchase Orders', screen: 'Procurement', section: 'Purchase Orders', roles: [ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.ReceivingOfficer, ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.QualityInspector, ROLES.Administrator] },
  { path: '/deliveries', label: 'Deliveries', screen: 'Deliveries', roles: [ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.ReceivingOfficer, ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.QualityInspector, ROLES.Administrator] },
  { path: '/quality-inspections', label: 'Quality Inspections', screen: 'Quality Inspections', roles: [ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.ReceivingOfficer, ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.QualityInspector, ROLES.Administrator] },
  { path: '/non-conformances', label: 'Non-Conformance Reports', screen: 'Non-Conformances', roles: [ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.ReceivingOfficer, ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.QualityInspector, ROLES.Administrator] },
  { path: '/admin', label: 'User Management', screen: 'Administration', roles: [ROLES.Administrator] },

  // Supplier portal. An external role, mutually exclusive with the internal
  // shell: a Supplier login never sees any of the routes above.
  { path: '/supplier', label: 'My RFQs', screen: 'Supplier', section: 'Rfqs', roles: [ROLES.Supplier] },
  { path: '/supplier/quotations', label: 'My Quotations', screen: 'Supplier', section: 'Quotations', roles: [ROLES.Supplier] },
  { path: '/supplier/purchase-orders', label: 'My Orders', screen: 'Supplier', section: 'PurchaseOrders', roles: [ROLES.Supplier] },
]

export const navigationForRoles = (roles = []) => NAVIGATION.filter((item) => hasAnyRole(roles, item.roles))
export const routeForPath = (path) => NAVIGATION.find((item) => item.path === path)
export const defaultRouteForRoles = (roles = []) => navigationForRoles(roles)[0]?.path ?? '/dashboard'