import { useState } from 'react'
import AppLayout from './layouts/AppLayout'
import ProcurementApp from './Features/procurement/pages/ProcurementApp'
import QualityApp from './Features/quality/QualityApp'
import ComingSoon from './pages/common/ComingSoon'
import LoginPage from './auth/LoginPage'
import { useAuth } from './auth/AuthContext'
// Global table/list/pagination styles (.data-table, .table-action, .toolbar,
// .pagination). Every list screen in the app renders these classes, so the
// stylesheet is loaded at the shell level instead of relying on one page
// (DashboardBase) to happen to pull it in.
import './pages/common/common.css'

/// Sidebar item → what renders. Order follows the BuildWise workflow:
/// material request approved → quotations → AI analysis → manager decision →
/// purchase order → deliveries / quality (later components).
/// Procurement-owned items render real screens; items owned by components
/// that are not merged into this shell yet render a ComingSoon placeholder,
/// so no sidebar entry can ever crash (the old preview registry rendered
/// `undefined` for items like Suppliers).
const PROCUREMENT_ITEMS = new Set(['Dashboard', 'Suppliers', 'Quotations', 'Procurement', 'Purchase Orders'])

// Sidebar item → ProcurementApp section.
const SECTION_FOR_ITEM = {
  Dashboard: 'Dashboard',
  Suppliers: 'Suppliers',
  Quotations: 'Approved Requests',
  Procurement: 'Dashboard',
  'Purchase Orders': 'Purchase Orders',
}

export default function App() {
  const { isAuthenticated, user, logout } = useAuth()
  const [nav, setNav] = useState({ screen: 'Procurement', section: 'Dashboard', supplierId: null, requestId: null, orderId: null })

  if (!isAuthenticated) return <LoginPage />

  // Sidebar navigation: switch screen and reset all in-screen selections.
  const navigate = (item) => setNav({ screen: item, section: SECTION_FOR_ITEM[item] ?? 'Dashboard', supplierId: null, requestId: null, orderId: null })

  // Patch-style navigation used by links inside ProcurementApp, so jumps like
  // workspace → purchase order keep the rest of the state intact.
  const patchNav = (p) => setNav((cur) => ({ ...cur, ...p }))

  return (
    <AppLayout breadcrumb={`BuildWise / ${nav.screen}`} activeItem={nav.screen} user={user} onLogout={logout} onNavigate={navigate}>
      {PROCUREMENT_ITEMS.has(nav.screen)
        ? <ProcurementApp section={nav.section} supplierId={nav.supplierId} requestId={nav.requestId} orderId={nav.orderId} onNavigate={patchNav} />
        : ['Quality Inspections', 'Non-Conformances'].includes(nav.screen)
          ? <QualityApp section={nav.screen} />
          : <ComingSoon title={nav.screen} />}
    </AppLayout>
  )
}
