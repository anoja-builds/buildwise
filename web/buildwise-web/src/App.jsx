import { useState } from 'react'
import { Navigate, Outlet, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import AppLayout from './layouts/AppLayout'
import ProcurementApp from './Features/procurement/pages/ProcurementApp'
import ComingSoon from './pages/common/ComingSoon'
import QualityInspectionsPage from './pages/QualityInspectionsPage'
import NonConformancesPage from './pages/NonConformancesPage'
import MaterialRequestsPage from './pages/MaterialRequestsPage'
import DeliveriesPage from './pages/DeliveriesPage'
import AgentWorkflowsPage from './pages/AgentWorkflowsPage'
import AdministrationPage from './pages/AdministrationPage'
import DashboardPage from './pages/DashboardPage'
import RfqPage from './pages/RfqPage'
import SupplierPortalPage from './pages/SupplierPortalPage'
import LoginPage from './auth/LoginPage'
import ProtectedRoute from './auth/ProtectedRoute'
import { useAuth } from './auth/AuthContext'
import { NAVIGATION, defaultRouteForRoles, routeForPath } from './auth/accessControl'
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

/**
 * Route guard. The allowed roles come from the single NAVIGATION table rather
 * than a second, hand-maintained list, so a route can never be reachable in the
 * UI while absent from the menu, or vice versa.
 */
function RoleRoute({ allowedRoles, children }) {
  const { roles } = useAuth()
  return <ProtectedRoute roles={roles} allowedRoles={allowedRoles}>{children}</ProtectedRoute>
}

function renderScreen({ screen, section }) {
  switch (screen) {
    case 'Material Requests': return <MaterialRequestsPage />
    case 'Deliveries': return <DeliveriesPage />
    case 'Quality Inspections': return <QualityInspectionsPage />
    case 'Non-Conformances': return <NonConformancesPage />
    case 'Agent Workflows': return <AgentWorkflowsPage />
    case 'Administration': return <AdministrationPage />
    case 'RFQs': return <RfqPage />
    case 'Dashboard': return <DashboardPage />
    case 'Supplier': return <SupplierPortalPage section={section} />
    default: return <ProcurementWorkstation section={section} />
  }
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
        {NAVIGATION.map((item) => (
          <Route
            key={item.path}
            path={item.path}
            element={(
              <RoleRoute allowedRoles={item.roles}>
                {renderScreen(item)}
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
  const [nav, setNav] = useState({ section: section ?? 'Dashboard', supplierId: null, requestId: null, orderId: null })
  const patchNav = (patch) => setNav((current) => ({ ...current, ...patch }))

  return (
    <ProcurementApp
      section={nav.section}
      supplierId={nav.supplierId}
      requestId={nav.requestId}
      orderId={nav.orderId}
      onNavigate={patchNav}
    />
  )
}