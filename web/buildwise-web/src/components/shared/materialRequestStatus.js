// Single source of truth for how a MaterialRequestStatus is presented.
//
// Mirrors backend BuildWise.Api/Models/Enums/MaterialRequestStatus.cs. Every
// surface that reads material requests renders its badge through here - the
// Material Requests page itself plus the procurement areas that consume the
// same rows (Approved Requests Queue, Procurement Dashboard, procurement
// workspace, RFQ page). Previously each screen carried its own copy of this
// map, and the procurement screens bypassed it entirely with a hardcoded
// `status="success"`, so the same request could read green on one screen and
// be undecided on another.
//
// Tones are design-system status tones consumed by
// <StatusBadge status={...}> (badge--neutral / info / warning / success /
// danger). Keep this map in step with the enum above.

export const MATERIAL_REQUEST_TONES = {
  Draft: 'neutral',
  Submitted: 'info',
  UnderReview: 'info',
  PendingApproval: 'warning',
  RfqInProgress: 'info',
  AwaitingProcurementApproval: 'warning',
  Approved: 'success',
  Rejected: 'danger',
  Ordered: 'info',
  Completed: 'success',
  Cancelled: 'neutral',
}

// Statuses that carry no decision yet are never highlighted as a success, and
// an unknown status falls back to the neutral tone rather than throwing.
export const materialRequestTone = (status) => MATERIAL_REQUEST_TONES[status] ?? 'neutral'

// Statuses an approver may still decide on. Mirrors the backend rule in
// MaterialRequestService.RecordApprovalAsync: Approved is only accepted from
// PendingApproval / UnderReview / AwaitingProcurementApproval, and a decided
// request (Approved / Rejected / Cancelled / Completed) is frozen.
export const MATERIAL_REQUEST_DECIDABLE_STATUSES = [
  'PendingApproval',
  'UnderReview',
  'AwaitingProcurementApproval',
]

export const isMaterialRequestDecidable = (status) =>
  MATERIAL_REQUEST_DECIDABLE_STATUSES.includes(status)

// ---------------------------------------------------------------------
// Presentation rules for the RequestAnalysisAgent's output flags.
//
// These live in the same module as the status map for the same reason: the
// agent contract can grow a flag in backend/agent_service/request_agent.py,
// and every surface that renders those flags should read one map instead of
// each inventing its own colours.

/** Flags emitted by backend/agent_service/request_agent.py and by the
 *  deterministic fallback in OperationalAgentClient.RequestAgentResult. */
export const REQUEST_ANALYSIS_FLAG_TONES = {
  HIGH_URGENCY: 'danger',
  LARGE_QUANTITY_ORDER: 'warning',
  BULK_ORDER: 'warning',
}

/** Terse, human-readable meaning of each flag. An unknown flag falls back to
 *  the raw token, so a newer agent can never render as an unexplained badge. */
export const REQUEST_ANALYSIS_FLAG_LABELS = {
  HIGH_URGENCY: 'Time-critical — expedite procurement',
  LARGE_QUANTITY_ORDER: 'Large total quantity — check supply and budget',
  BULK_ORDER: 'Many line items — plan a consolidated order',
}

export function requestAnalysisFlagTone(flag) {
  return REQUEST_ANALYSIS_FLAG_TONES[flag] || 'neutral'
}

export function requestAnalysisFlagLabel(flag) {
  return REQUEST_ANALYSIS_FLAG_LABELS[flag] || flag
}
