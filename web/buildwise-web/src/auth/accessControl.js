export const ROLE_GROUPS = {
  site: ['SiteEngineer', 'SiteOfficer'],
  procurement: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager'],
  manager: ['ProcurementManager', 'SiteManager'],
  quality: ['QualityInspector'],
  administrator: ['Administrator'],
}

export const hasAnyRole = (roles = [], allowed = []) =>
  allowed.some((role) => roles.includes(role))

export const NAVIGATION = [
  { path: '/dashboard', label: 'Dashboard', screen: 'Procurement', section: 'Dashboard', roles: ['SiteEngineer', 'SiteOfficer', 'ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'QualityInspector', 'Administrator'] },
  { path: '/material-requests', label: 'Material Requests', screen: 'Material Requests', roles: ['SiteEngineer', 'SiteOfficer', 'ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'] },
  { path: '/suppliers', label: 'Suppliers', screen: 'Procurement', section: 'Suppliers', roles: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'] },
  { path: '/quotations', label: 'Quotations', screen: 'Procurement', section: 'Approved Requests', roles: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'] },
  { path: '/rfqs', label: 'RFQs', screen: 'RFQs', roles: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'] },
  { path: '/procurement', label: 'Procurement Workspace', screen: 'Procurement', section: 'Dashboard', roles: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'] },
  { path: '/agent-workflows', label: 'Agent Workflows', screen: 'Agent Workflows', roles: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'] },
  { path: '/purchase-orders', label: 'Purchase Orders', screen: 'Procurement', section: 'Purchase Orders', roles: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'SiteEngineer', 'SiteOfficer', 'QualityInspector', 'Administrator'] },
  { path: '/deliveries', label: 'Deliveries', screen: 'Deliveries', roles: ['SiteEngineer', 'SiteOfficer', 'ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'QualityInspector', 'Administrator'] },
  { path: '/quality-inspections', label: 'Quality & NCRs', screen: 'Quality Inspections', roles: ['SiteEngineer', 'SiteOfficer', 'ProcurementManager', 'SiteManager', 'QualityInspector', 'Administrator'] },
  { path: '/admin', label: 'User Management', screen: 'Administration', roles: ['Administrator'] },
]

export const navigationForRoles = (roles = []) => NAVIGATION.filter((item) => hasAnyRole(roles, item.roles))
export const routeForPath = (path) => NAVIGATION.find((item) => item.path === path)
export const defaultRouteForRoles = (roles = []) => navigationForRoles(roles)[0]?.path ?? '/dashboard'
