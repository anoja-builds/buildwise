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

const SECTIONS = ['Dashboard', 'Suppliers', 'Approved Requests', 'Purchase Orders']

export default function ProcurementApp() {
  const { user, hasRole } = useAuth()
  const role = hasRole('ProcurementManager') ? 'Manager' : hasRole('ProcurementOfficer') ? 'Officer' : 'ReadOnly'
  const [section, setSection] = useState('Dashboard')
  const [selectedSupplierId, setSelectedSupplierId] = useState(null)
  const [selectedRequestId, setSelectedRequestId] = useState(null)
  const [selectedOrderId, setSelectedOrderId] = useState(null)
  const [mockMode, setMockMode] = useState(false)

  useEffect(() => {
    procurementApi.listSuppliers().catch(() => {}).finally(() => setMockMode(isUsingMockData()))
  }, [])

  const goToSection = (next) => {
    setSection(next)
    setSelectedSupplierId(null)
    setSelectedRequestId(null)
    setSelectedOrderId(null)
  }

  const openRequest = (id) => { setSelectedRequestId(id); setSection('Approved Requests') }
  const openOrder = (id) => { setSelectedOrderId(id); setSection('Purchase Orders') }

  let content
  if (section === 'Dashboard') {
    content = <ProcurementDashboard onOpenRequest={openRequest} onOpenOrder={openOrder} />
  } else if (section === 'Suppliers') {
    content = selectedSupplierId
      ? <SupplierDetail supplierId={selectedSupplierId} onBack={() => setSelectedSupplierId(null)} />
      : <SupplierList onOpenSupplier={setSelectedSupplierId} />
  } else if (section === 'Approved Requests') {
    content = selectedRequestId
      ? <RequestWorkspace requestId={selectedRequestId} role={role} onBack={() => setSelectedRequestId(null)} onViewPurchaseOrder={openOrder} />
      : <ApprovedRequestsQueue onOpenRequest={setSelectedRequestId} />
  } else {
    content = selectedOrderId
      ? <PurchaseOrderDetail orderId={selectedOrderId} onBack={() => setSelectedOrderId(null)} />
      : <PurchaseOrderList onOpenOrder={setSelectedOrderId} />
  }

  return (
    <div className="stack">
      <div className="proc-tabs" style={{ justifyContent: 'space-between' }}>
        <div style={{ display: 'flex', gap: 'var(--space-2)', flexWrap: 'wrap' }}>
          {SECTIONS.map((s) => <button key={s} type="button" className={`proc-tab ${section === s ? 'proc-tab--active' : ''}`} onClick={() => goToSection(s)}>{s}</button>)}
        </div>
        <div className="proc-role-switch">
          Signed in as <strong>{user?.fullName}</strong> · {user?.roles?.join(', ') || 'No procurement role'}
        </div>
      </div>
      {role === 'ReadOnly' && <div className="proc-mock-banner">Your account has no Procurement Officer or Procurement Manager role — you can view procurement screens but cannot record quotations, run AI analysis, or approve recommendations.</div>}
      {mockMode && <div className="proc-mock-banner">Backend or agent service not reachable — showing demo data so the UI stays interactive. Start BuildWise.Api to see live data.</div>}
      {content}
    </div>
  )
}
