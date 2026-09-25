import { useState } from 'react'
import { Navigate, Outlet, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import AppLayout from './layouts/AppLayout'
import ProcurementApp from './Features/procurement/pages/ProcurementApp'
import ComingSoon from './pages/common/ComingSoon'
import QualityInspectionsPage from './pages/QualityInspectionsPage'
import MaterialRequestsPage from './pages/MaterialRequestsPage'
import DeliveriesPage from './pages/DeliveriesPage'
import AgentWorkflowsPage from './pages/AgentWorkflowsPage'
import AdministrationPage from './pages/AdministrationPage'
import RfqPage from './pages/RfqPage'
import LoginPage from './auth/LoginPage'
import ProtectedRoute from './auth/ProtectedRoute'
import { useAuth } from './auth/AuthContext'
import { defaultRouteForRoles, routeForPath } from './auth/accessControl'
import './pages/common/common.css'

function AuthenticatedLayout() {
  const { user, logout } = useAuth()
  const location = useLocation()
  const navigate = useNavigate()
  const route = routeForPath(location.pathname)

  const navigateTo = (path) => navigate(path)

  return (
    <AppLayout
      breadcrumb={`BuildWise / ${route?.label ?? 'Not found'}`}
      activeItem={route?.label}
      user={user}
      onLogout={() => { logout(); navigate('/login', { replace: true }) }}
      onNavigate={navigateTo}
    >
      <Outlet />
    </AppLayout>
  )
}

function RoleRoute({ allowedRoles, children }) {
  const { roles } = useAuth()
  return <ProtectedRoute roles={roles} allowedRoles={allowedRoles}>{children}</ProtectedRoute>
}

export default function App() {
  const { isAuthenticated, roles } = useAuth()
  const home = defaultRouteForRoles(roles)

  return (
    <Routes>
      <Route
        path="/login"
        element={isAuthenticated ? <Navigate to={home} replace /> : <LoginPage />}
      />
      <Route element={isAuthenticated ? <AuthenticatedLayout /> : <Navigate to="/login" replace state={{ from: window.location.pathname }} />}>
        {NAVIGATION_ROUTES.map(({ path, screen, section, allowedRoles }) => (
          <Route
            key={path}
            path={path}
            element={(
              <RoleRoute allowedRoles={allowedRoles}>
                {screen === 'Material Requests' ? <MaterialRequestsPage />
                  : screen === 'Deliveries' ? <DeliveriesPage />
                  : screen === 'Quality Inspections' ? <QualityInspectionsPage />
                  : screen === 'Agent Workflows' ? <AgentWorkflowsPage />
                  : screen === 'Administration' ? <AdministrationPage />
                  : screen === 'RFQs' ? <RfqPage />
                  : <ProcurementWorkstation section={section} />}
              </RoleRoute>
            )}
          />
        ))}
        <Route index element={<Navigate to={home} replace />} />
        <Route path="*" element={<ComingSoon title="Page not found" />} />
      </Route>
    </Routes>
  )
}

function ProcurementWorkstation({ section }) {
  const [nav, setNav] = useState({ section, supplierId: null, requestId: null, orderId: null })
  const patchNav = (patch) => setNav((current) => ({ ...current, ...patch }))

  return (
    <ProcurementApp
      section={section ?? nav.section}
      supplierId={nav.supplierId}
      requestId={nav.requestId}
      orderId={nav.orderId}
      onNavigate={patchNav}
    />
  )
}

const NAVIGATION_ROUTES = [
  { path: '/dashboard', label: 'Dashboard', screen: 'Procurement', section: 'Dashboard', allowedRoles: ['SiteEngineer', 'SiteOfficer', 'ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'QualityInspector', 'Administrator'] },
  { path: '/material-requests', label: 'Material Requests', screen: 'Material Requests', allowedRoles: ['SiteEngineer', 'SiteOfficer', 'ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'] },
  { path: '/suppliers', label: 'Suppliers', screen: 'Procurement', section: 'Suppliers', allowedRoles: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'] },
  { path: '/quotations', label: 'Quotations', screen: 'Procurement', section: 'Approved Requests', allowedRoles: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'] },
  { path: '/rfqs', label: 'RFQs', screen: 'RFQs', section: 'RFQs', allowedRoles: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'] },
  { path: '/procurement', label: 'Procurement Workspace', screen: 'Procurement', section: 'Dashboard', allowedRoles: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'] },
  { path: '/agent-workflows', label: 'Agent Workflows', screen: 'Agent Workflows', allowedRoles: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'Administrator'] },
  { path: '/purchase-orders', label: 'Purchase Orders', screen: 'Procurement', section: 'Purchase Orders', allowedRoles: ['ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'SiteEngineer', 'SiteOfficer', 'QualityInspector', 'Administrator'] },
  { path: '/deliveries', label: 'Deliveries', screen: 'Deliveries', allowedRoles: ['SiteEngineer', 'SiteOfficer', 'ProcurementOfficer', 'ProcurementManager', 'SiteManager', 'QualityInspector', 'Administrator'] },
  { path: '/quality-inspections', label: 'Quality & NCRs', screen: 'Quality Inspections', allowedRoles: ['SiteEngineer', 'SiteOfficer', 'ProcurementManager', 'SiteManager', 'QualityInspector', 'Administrator'] },
  { path: '/admin', label: 'User Management', screen: 'Administration', allowedRoles: ['Administrator'] },
]
