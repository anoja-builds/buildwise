import { useEffect, useState } from 'react'
import { Card } from '../../../components/shared'
import { procurementApi } from '../../procurement/services/procurementApi'
import { qualityApi } from '../../../services/qualityApi'
import { useAuth } from '../../../auth/AuthContext'
import { ROLES } from '../../../auth/accessControl'
import './qualityLifecycleFlow.css'

/**
 * The connected material lifecycle, as a chain of stages:
 *
 *   Request → Approval → RFQ → Quotation → Purchase Order → Delivery
 *           → Inspection → NCR → Resolution
 *
 * Why this exists: Inspections and Non-Conformances used to be a single page,
 * and neither made it obvious that an inspection is *caused* by a delivery, or
 * that an NCR is *caused* by a rejected inspection line. This bar states those
 * links explicitly and shows how many records sit at each stage right now.
 *
 * Access: stage counts come from endpoints governed by different role policies,
 * so a caller sees counts only for stages they may read. Anything else renders
 * as an em dash rather than a misleading 0.
 */
const STAGES = [
  {
    key: 'request',
    label: 'Material Request',
    short: 'Request',
    to: '/material-requests',
    groups: [ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.Administrator],
    load: async (requests) => requests.length,
  },
  {
    key: 'approval',
    label: 'Manager Approval',
    short: 'Approval',
    to: '/material-requests',
    groups: [ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.Administrator],
    // Requests sitting on a manager's desk right now.
    load: async (requests) => requests.filter((r) => r.status === 'PendingApproval').length,
  },
  {
    key: 'rfq',
    label: 'RFQ Issued',
    short: 'RFQ',
    to: '/rfqs',
    groups: [ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.Administrator],
    load: async () => (await procurementApi.listRfqs()).length,
  },
  {
    key: 'quotation',
    label: 'Supplier Quotations',
    short: 'Quotes',
    to: '/quotations',
    groups: [ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.Administrator],
    // Each request summary already carries a quotation count, so the total needs
    // no extra round trips.
    load: async (requests) => requests.reduce((sum, r) => sum + (r.quotationCount ?? 0), 0),
  },
  {
    key: 'po',
    label: 'Purchase Orders',
    short: 'PO',
    to: '/purchase-orders',
    groups: [ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.ReceivingOfficer, ROLES.ProcurementOfficer, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.QualityInspector, ROLES.Administrator],
    load: async () => (await procurementApi.listPurchaseOrders({ pageSize: 1 })).total,
  },
  {
    key: 'delivery',
    label: 'Deliveries',
    short: 'Delivery',
    to: '/deliveries',
    groups: [ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.ReceivingOfficer, ROLES.QualityInspector, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.Administrator],
    load: async () => (await qualityApi.listDeliveries()).length,
  },
  {
    key: 'inspection',
    label: 'Quality Inspections',
    short: 'Inspection',
    to: '/quality-inspections',
    groups: [ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.ReceivingOfficer, ROLES.QualityInspector, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.Administrator],
    load: async () => (await qualityApi.listInspections()).length,
  },
  {
    key: 'ncr',
    label: 'Non-Conformances',
    short: 'NCR',
    to: '/non-conformances',
    groups: [ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.ReceivingOfficer, ROLES.QualityInspector, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.Administrator],
    load: async () => (await qualityApi.listNonConformances()).length,
  },
  {
    key: 'resolution',
    label: 'Resolutions',
    short: 'Resolved',
    to: '/non-conformances',
    groups: [ROLES.SiteEngineer, ROLES.SiteOfficer, ROLES.ReceivingOfficer, ROLES.QualityInspector, ROLES.ProcurementManager, ROLES.SiteManager, ROLES.Administrator],
    // Resolved + closed + accepted-exception = reports that reached an outcome.
    load: async () => {
      const ncrs = await qualityApi.listNonConformances()
      return ncrs.filter((n) => ['Resolved', 'Closed', 'AcceptedException'].includes(n.status)).length
    },
  },
]

/**
 * @param activeKey  stage key to highlight, e.g. 'inspection'
 * @param counts     pre-loaded counts, so a page that has already fetched a list
 *                   does not refetch it purely to render this bar
 * @param onNavigate called with a stage's route path when a node is clicked
 */
export default function QualityLifecycleFlow({ activeKey, counts: providedCounts = {}, onNavigate }) {
  const { roles } = useAuth()
  const [counts, setCounts] = useState(providedCounts)
  const [loading, setLoading] = useState(true)

  const canRead = (groups) => groups.some((role) => roles.includes(role))

  useEffect(() => {
    let cancelled = false

    const load = async () => {
      const results = {}
      let requests = []

      // Material requests back several stages, so they are fetched once and
      // shared rather than requested per stage.
      if (canRead(STAGES[0].groups)) {
        try { requests = await qualityApi.listMaterialRequests() } catch { requests = [] }
      }
      if (cancelled) return

      for (const stage of STAGES) {
        if (providedCounts[stage.key] !== undefined) {
          results[stage.key] = providedCounts[stage.key]
          continue
        }
        if (!canRead(stage.groups)) {
          // Not entitled to read this stage: show "—" instead of a false 0.
          results[stage.key] = null
          continue
        }
        try {
          results[stage.key] = await stage.load(requests)
        } catch {
          // One failing stage (403/500) must not break the rest of the chain.
          results[stage.key] = null
        }
        if (cancelled) return
      }

      if (!cancelled) {
        setCounts(results)
        setLoading(false)
      }
    }

    load()
    return () => { cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const display = { ...counts, ...providedCounts }

  return (
    <Card
      title="Material lifecycle"
      subtitle="How a request becomes a delivered, inspected and accepted material. Counts are live; stages you cannot read show —."
      className="lifecycle-card"
    >
      <nav className="lifecycle" aria-label="Material lifecycle stages" data-testid="lifecycle-flow">
        {STAGES.map((stage, index) => {
          const value = display[stage.key]
          const isActive = stage.key === activeKey
          return (
            <div className="lifecycle__step" key={stage.key}>
              {index > 0 && <span className="lifecycle__connector" aria-hidden="true" />}
              <button
                type="button"
                className={[
                  'lifecycle__node',
                  isActive ? 'lifecycle__node--active' : '',
                  value === null ? 'lifecycle__node--restricted' : '',
                ].filter(Boolean).join(' ')}
                onClick={() => onNavigate?.(stage.to)}
                aria-current={isActive ? 'step' : undefined}
                title={stage.label}
                data-testid={`lifecycle-stage-${stage.key}`}
              >
                <span className="lifecycle__label">{stage.short}</span>
                <span className="lifecycle__count">
                  {loading && value === undefined ? '…' : value === null ? '—' : value}
                </span>
              </button>
            </div>
          )
        })}
      </nav>
      <p className="lifecycle__legend">
        A delivery is recorded against a confirmed purchase order. An inspection is raised from that
        delivery. Every rejected inspection line automatically produces a non-conformance, which is
        then reviewed through to resolution.
      </p>
    </Card>
  )
}
