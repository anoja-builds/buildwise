import { useEffect, useState } from 'react'
import '../procurement.css'
import ProcurementDashboard from './ProcurementDashboard'
import SupplierList from './SupplierList'
import SupplierDetail from './SupplierDetail'
import ApprovedRequestsQueue from './ApprovedRequestsQueue'
import RequestWorkspace from './RequestWorkspace'
import PurchaseOrderList from './PurchaseOrderList'
import PurchaseOrderDetail from './PurchaseOrderDetail'
import { procurementApi, isUsingMockData } from '../services/procurementApi'
import { useAuth } from '../../../auth/AuthContext'

/// One procurement screen. The sidebar (AppLayout) decides which section is
/// shown and owns the navigation state, so there is no separate tab strip in
/// here — the workflow reads: approved requests → quotations → AI analysis →
/// manager decision → purchase order.
///
/// `onNavigate(patch)` merges a navigation patch into the shell state, e.g.
/// { section: 'Purchase Orders', orderId: 3 }. Sidebar navigation resets all
/// selections; in-screen links only set what they need.
export default function ProcurementApp({
  section = 'Dashboard',
  supplierId = null,
  requestId = null,
  orderId = null,
  onNavigate,
}) {
  const { hasRole } = useAuth()
  const role = hasRole('ProcurementManager') ? 'Manager' : hasRole('ProcurementOfficer') ? 'Officer' : 'ReadOnly'
  const [mockMode, setMockMode] = useState(false)

  useEffect(() => {
    procurementApi.listSuppliers().catch(() => {}).finally(() => setMockMode(isUsingMockData()))
  }, [])

  let content
  if (section === 'Suppliers') {
    content = supplierId
      ? <SupplierDetail supplierId={supplierId} onBack={() => onNavigate({ supplierId: null })} />
      : <SupplierList onOpenSupplier={(id) => onNavigate({ supplierId: id })} />
  } else if (section === 'Approved Requests') {
    content = requestId
      ? <RequestWorkspace requestId={requestId} role={role} onBack={() => onNavigate({ requestId: null })} onViewPurchaseOrder={(id) => onNavigate({ section: 'Purchase Orders', orderId: id })} />
      : <ApprovedRequestsQueue onOpenRequest={(id) => onNavigate({ section: 'Approved Requests', requestId: id })} />
  } else if (section === 'Purchase Orders') {
    content = orderId
      ? <PurchaseOrderDetail orderId={orderId} onBack={() => onNavigate({ orderId: null })} />
      : <PurchaseOrderList onOpenOrder={(id) => onNavigate({ orderId: id })} />
  } else {
    content = <ProcurementDashboard onOpenRequest={(id) => onNavigate({ section: 'Approved Requests', requestId: id })} onOpenOrder={(id) => onNavigate({ section: 'Purchase Orders', orderId: id })} />
  }

  return (
    <div className="stack">
      {role === 'ReadOnly' && <div className="proc-mock-banner">Your account has no Procurement Officer or Procurement Manager role — you can view procurement screens but cannot record quotations, run AI analysis, or approve recommendations.</div>}
      {mockMode && <div className="proc-mock-banner">Backend or agent service not reachable — showing demo data so the UI stays interactive. Start BuildWise.Api to see live data.</div>}
      {content}
    </div>
  )
}
