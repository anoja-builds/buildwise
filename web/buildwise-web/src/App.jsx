import { Navigate, Outlet, Route, Routes, useLocation, useNavigate, useParams } from 'react-router-dom'
import AppLayout from './layouts/AppLayout'
import MaterialRequestsPage from './Features/MaterialRequests/pages/MaterialRequestsPage'
import DeliveryDashboard from './Features/deliveries/pages/DeliveryDashboard'
import ProcurementApp from './Features/procurement/pages/ProcurementApp'
import { RoutedQualityApp } from './Features/quality/QualityApp'
import LoginPage from './auth/LoginPage'
import { useAuth } from './auth/AuthContext'
import { canAccess, landingPath, routeConfig } from './routes/routeConfig'
import { AccessDeniedView, ErrorBoundary, NotFoundView, SessionExpiredView } from './components/system/SystemStates'
import './pages/common/common.css'

function AccessDenied() {
  const { roles } = useAuth()
  const landing = landingPath(roles)
  return <AccessDeniedView landingPath={landing} />
}

function AuthenticatedLayout() {
  const { isAuthenticated, user, logout } = useAuth()
  const location = useLocation()
  if (!isAuthenticated) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  return <AppLayout user={user} onLogout={logout}><Outlet /></AppLayout>
}

function LoginRoute() {
  const { isAuthenticated, roles } = useAuth()
  const location = useLocation()
  // Internal deep links still pass through the role guard after login.
  const from = location.state?.from
  const target = typeof from === 'string' && from.startsWith('/') && !from.startsWith('//') && from !== '/login' ? from : landingPath(roles)
  return isAuthenticated ? <Navigate to={target} replace /> : <LoginPage />
}

function ProcurementRoute({ route }) {
  const { id } = useParams()
  const navigate = useNavigate()
  const section = route.section
  const onNavigate = (patch) => {
    const nextSection = patch.section || section
    const base = nextSection === 'Suppliers' ? '/suppliers' : nextSection === 'Approved Requests' ? '/quotations' : nextSection === 'Purchase Orders' ? '/purchase-orders' : '/procurement'
    const nextId = nextSection === 'Suppliers' ? patch.supplierId : nextSection === 'Approved Requests' ? patch.requestId : patch.orderId
    navigate(nextId == null ? base : `${base}/${nextId}`)
  }
  return <ProcurementApp section={section} supplierId={section === 'Suppliers' ? id : null} requestId={section === 'Approved Requests' ? id : null} orderId={section === 'Purchase Orders' ? id : null} onNavigate={onNavigate} />
}

function ProtectedPage({ route }) {
  const { roles } = useAuth()
  if (!canAccess(route, roles)) return <AccessDenied />
  switch (route.screen) {
    case 'materials': return <MaterialRequestsPage />
    case 'deliveries': return <DeliveryDashboard />
    case 'procurement': return <ProcurementRoute route={route} />
    case 'quality': return <RoutedQualityApp kind={route.kind} />
    default: return <AccessDenied />
  }
}

export default function App() {
  const { roles } = useAuth()
  return (
    <ErrorBoundary>
      <Routes>
        <Route path="/login" element={<LoginRoute />} />
        <Route path="/session-expired" element={<SessionExpiredView />} />
        <Route element={<AuthenticatedLayout />}>
          <Route index element={<Navigate to={landingPath(roles)} replace />} />
          {routeConfig.map((route) => <Route key={route.path} path={route.path} element={<ProtectedPage route={route} />} />)}
          <Route path="/access-denied" element={<AccessDenied />} />
          <Route path="*" element={<NotFoundView landingPath={landingPath(roles)} />} />
        </Route>
      </Routes>
    </ErrorBoundary>
  )
}
