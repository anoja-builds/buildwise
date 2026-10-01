// UX visibility mirrors controller authorization; the API remains authoritative.
// Sources: backend/BuildWise.Api/Controllers/{name}Controller.cs.
export const procurementRoles = ['ProcurementOfficer', 'ProcurementManager', 'Administrator']
export const qualityRoles = ['QualityInspector', 'Administrator']
export const purchaseOrderRoles = procurementRoles
export const deliveryRoles = ['SiteEngineer', ...procurementRoles, 'QualityInspector']
const materialRoles = ['SiteEngineer', ...procurementRoles]

export const routeConfig = [
  { label: 'Dashboard', path: '/dashboard', allowedRoles: procurementRoles, screen: 'procurement', section: 'Dashboard' },
  { label: 'Material Requests', path: '/material-requests', allowedRoles: materialRoles, screen: 'materials' },
  { label: 'Suppliers', path: '/suppliers', allowedRoles: procurementRoles, screen: 'procurement', section: 'Suppliers' },
  { label: 'Supplier details', path: '/suppliers/:id', allowedRoles: procurementRoles, screen: 'procurement', section: 'Suppliers', nav: false },
  { label: 'Quotations', path: '/quotations', allowedRoles: procurementRoles, screen: 'procurement', section: 'Approved Requests' },
  { label: 'Request workspace', path: '/quotations/:id', allowedRoles: procurementRoles, screen: 'procurement', section: 'Approved Requests', nav: false },
  { label: 'Procurement', path: '/procurement', allowedRoles: procurementRoles, screen: 'procurement', section: 'Dashboard' },
  { label: 'Purchase Orders', path: '/purchase-orders', allowedRoles: purchaseOrderRoles, screen: 'procurement', section: 'Purchase Orders' },
  { label: 'Purchase order details', path: '/purchase-orders/:id', allowedRoles: purchaseOrderRoles, screen: 'procurement', section: 'Purchase Orders', nav: false },
  { label: 'Deliveries', path: '/deliveries', allowedRoles: deliveryRoles, screen: 'deliveries' },
  { label: 'Quality Inspections', path: '/quality-inspections', allowedRoles: qualityRoles, screen: 'quality', kind: 'inspectionList' },
  { label: 'Inspection details', path: '/quality-inspections/:id', allowedRoles: qualityRoles, screen: 'quality', kind: 'inspection', nav: false },
  { label: 'Start inspection', path: '/quality-inspections/new/:deliveryId', allowedRoles: qualityRoles, screen: 'quality', kind: 'startInspection', nav: false },
  { label: 'Complete inspection', path: '/quality-inspections/:id/complete', allowedRoles: qualityRoles, screen: 'quality', kind: 'completeInspection', nav: false },
  { label: 'Create non-conformance', path: '/quality-inspections/:id/non-conformances/new/:itemId', allowedRoles: qualityRoles, screen: 'quality', kind: 'create', nav: false },
  { label: 'Non-Conformances', path: '/non-conformances', allowedRoles: qualityRoles, screen: 'quality', kind: 'ncrList' },
  { label: 'Non-conformance details', path: '/non-conformances/:id', allowedRoles: qualityRoles, screen: 'quality', kind: 'ncr', nav: false },
  // Placeholders have no implemented page/API permission contract. Grant nobody
  // access until a contract exists, rather than inventing admin privileges.
  { label: 'AI Workflows', path: '/ai-workflows', allowedRoles: [], screen: 'placeholder' },
  { label: 'Reports', path: '/reports', allowedRoles: [], screen: 'placeholder' },
  { label: 'Users & Roles', path: '/users', allowedRoles: ['Administrator'], screen: 'users' },
]

export const canAccess = (route, roles) => route.allowedRoles.some((role) => roles.includes(role))
export const visibleRoutes = (roles) => routeConfig.filter((route) => route.nav !== false && canAccess(route, roles))

export function landingPath(roles) {
  if (roles.includes('Administrator')) return '/dashboard'
  if (roles.includes('SiteEngineer')) return '/material-requests'
  if (roles.includes('QualityInspector')) return '/quality-inspections'
  if (roles.some((role) => procurementRoles.includes(role))) return '/procurement'
  return visibleRoutes(roles)[0]?.path || '/access-denied'
}
