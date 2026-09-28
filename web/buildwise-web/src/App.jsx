import { useState } from 'react'
import AppLayout from './layouts/AppLayout'
import MaterialRequestsPage from './Features/MaterialRequests/pages/MaterialRequestsPage'
import DeliveryDashboard from './Features/deliveries/pages/DeliveryDashboard'
import ProcurementApp from './Features/procurement/pages/ProcurementApp'
import QualityApp from './Features/quality/QualityApp'
import ComingSoon from './pages/common/ComingSoon'
import LoginPage from './auth/LoginPage'
import { useAuth } from './auth/AuthContext'
import './pages/common/common.css'

const PROCUREMENT_ITEMS = new Set(['Suppliers', 'Quotations', 'Procurement', 'Purchase Orders'])

const SECTION_FOR_ITEM = {
  Suppliers: 'Suppliers',
  Quotations: 'Approved Requests',
  Procurement: 'Dashboard',
  'Purchase Orders': 'Purchase Orders',
}

export default function App() {
  const { isAuthenticated, user, logout } = useAuth()
  const [nav, setNav] = useState({ screen: 'Dashboard', section: 'Dashboard', supplierId: null, requestId: null, orderId: null })

  if (!isAuthenticated) return <LoginPage />

  const navigate = (item) => setNav({ screen: item, section: SECTION_FOR_ITEM[item] ?? 'Dashboard', supplierId: null, requestId: null, orderId: null })
  const patchNav = (p) => setNav((cur) => ({ ...cur, ...p }))

  const renderScreen = () => {
    switch (nav.screen) {
      case 'Material Requests':
        return <MaterialRequestsPage />
      case 'Deliveries':
        return <DeliveryDashboard />
      case 'Quality Inspections':
      case 'Non-Conformances':
        return <QualityApp section={nav.screen} />
      default:
        if (PROCUREMENT_ITEMS.has(nav.screen) || nav.screen === 'Dashboard') {
          return <ProcurementApp section={nav.section} supplierId={nav.supplierId} requestId={nav.requestId} orderId={nav.orderId} onNavigate={patchNav} />
        }
        return <ComingSoon title={nav.screen} />
    }
  }

  return (
    <AppLayout breadcrumb={`BuildWise / ${nav.screen}`} activeItem={nav.screen} user={user} onLogout={logout} onNavigate={navigate}>
      {renderScreen()}
    </AppLayout>
  )
}
