import '../procurement.css'
import ProcurementDashboard from './ProcurementDashboard'
import SupplierList from './SupplierList'
import SupplierDetail from './SupplierDetail'
import ApprovedRequestsQueue from './ApprovedRequestsQueue'
import RequestWorkspace from './RequestWorkspace'
import PurchaseOrderList from './PurchaseOrderList'
import PurchaseOrderDetail from './PurchaseOrderDetail'
import { useAuth } from '../../../auth/AuthContext'

// Route adapters supply IDs and translate existing workflow callbacks to URLs.
export default function ProcurementApp({
  section = 'Dashboard',
  supplierId = null,
  requestId = null,
  orderId = null,
  onNavigate,
}) {
  const { hasRole } = useAuth()
  const role = (hasRole('ProcurementManager') || hasRole('Administrator')) ? 'Manager' : hasRole('ProcurementOfficer') ? 'Officer' : 'ReadOnly'
  const canRead = role !== 'ReadOnly' || (section === 'Purchase Orders' && hasRole('ReceivingOfficer'))
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
      ? <PurchaseOrderDetail orderId={orderId} canUpdate={role !== 'ReadOnly'} onBack={() => onNavigate({ orderId: null })} />
      : <PurchaseOrderList onOpenOrder={(id) => onNavigate({ orderId: id })} />
  } else {
    content = <ProcurementDashboard onOpenRequest={(id) => onNavigate({ section: 'Approved Requests', requestId: id })} onOpenOrder={(id) => onNavigate({ section: 'Purchase Orders', orderId: id })} />
  }

  return (
    <div className="stack">
      {!canRead && <div className="proc-mock-banner">Your account does not have access to this procurement page.</div>}
      {canRead && content}
    </div>
  )
}
