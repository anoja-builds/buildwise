// Shared formatting helpers for the quality domain.
//
// Extracted when the Quality page was split into two screens (Inspections and
// Non-Conformances). Both screens, the lifecycle flow and their tests need the
// same severity/status mapping; duplicating it per page is how the two drift.

/** Severity -> badge tone, matching the shared badge scale. */
export const SEVERITY_TONE = {
  Critical: 'danger',
  High: 'danger',
  Medium: 'warning',
  Low: 'neutral',
}

/** NCR status -> badge tone. */
export const STATUS_TONE = {
  Open: 'info',
  UnderReview: 'info',
  CorrectiveActionRequired: 'warning',
  Resolved: 'success',
  Closed: 'neutral',
  AcceptedException: 'warning',
}

/** Quality risk level -> badge tone. */
export const RISK_TONE = {
  High: 'danger',
  Medium: 'warning',
  Low: 'success',
}

/**
 * The NCR statuses a reviewer may move a report to, in the order the
 * workflow progresses. Mirrors the backend's allowed-transition table.
 */
export const NCR_TRANSITIONS = [
  'UnderReview',
  'CorrectiveActionRequired',
  'Resolved',
  'AcceptedException',
  'Closed',
]

/** Statuses that require a resolution before the transition is accepted. */
export const RESOLUTION_REQUIRED = ['Resolved', 'Closed', 'AcceptedException']

/** Splits PascalCase enum values for display. */
export const humanize = (value) => value?.replace(/([A-Z])/g, ' $1').trim() ?? ''

export const dateTime = (value) => (value ? new Date(value).toLocaleString() : '—')

export const qty = (value) =>
  value === null || value === undefined ? '—' : `${value} ${value === 1 ? 'unit' : 'units'}`

/**
 * Suggests the next status for an NCR. The backend enforces the legal
 * transitions; this only pre-selects the most likely next step so a reviewer
 * does not start from a blank dropdown.
 */
export function suggestNextNcrStatus(status) {
  if (status === 'Open') return 'UnderReview'
  if (status === 'CorrectiveActionRequired') return 'Resolved'
  return 'Closed'
}