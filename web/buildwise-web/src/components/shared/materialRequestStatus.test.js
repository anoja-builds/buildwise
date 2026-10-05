import { describe, expect, it } from 'vitest'
import {
  MATERIAL_REQUEST_DECIDABLE_STATUSES,
  MATERIAL_REQUEST_TONES,
  REQUEST_ANALYSIS_FLAG_TONES,
  isMaterialRequestDecidable,
  materialRequestTone,
  requestAnalysisFlagLabel,
  requestAnalysisFlagTone,
} from './materialRequestStatus'

// Mirrors backend BuildWise.Api/Models/Enums/MaterialRequestStatus.cs. Every
// surface that renders a material request badge goes through this map, so a
// missing status would silently render a neutral badge everywhere.
const ALL_STATUSES = [
  'Draft',
  'Submitted',
  'UnderReview',
  'PendingApproval',
  'RfqInProgress',
  'AwaitingProcurementApproval',
  'Approved',
  'Rejected',
  'Ordered',
  'Completed',
  'Cancelled',
]

describe('materialRequestTone', () => {
  it('maps every status in the backend enum', () => {
    const unmapped = ALL_STATUSES.filter((status) => !(status in MATERIAL_REQUEST_TONES))
    expect(unmapped).toEqual([])
  })

  it('only marks real successes as green', () => {
    const green = ALL_STATUSES.filter((status) => materialRequestTone(status) === 'success')
    expect(green).toEqual(['Approved', 'Completed'])
  })

  it('maps a pending request to the warning tone the manager reviews', () => {
    expect(materialRequestTone('PendingApproval')).toBe('warning')
  })

  it('maps Rejected to the danger tone', () => {
    // The site engineer's rejected request must not look like a success.
    expect(materialRequestTone('Rejected')).toBe('danger')
  })

  it('falls back to neutral for a status the frontend has not seen yet', () => {
    expect(materialRequestTone('SomethingUnexpected')).toBe('neutral')
    expect(materialRequestTone(undefined)).toBe('neutral')
  })
})

describe('isMaterialRequestDecidable', () => {
  it('allows a decision only while the request is undecided', () => {
    expect(MATERIAL_REQUEST_DECIDABLE_STATUSES).toEqual([
      'PendingApproval',
      'UnderReview',
      'AwaitingProcurementApproval',
    ])
    expect(isMaterialRequestDecidable('PendingApproval')).toBe(true)
    expect(isMaterialRequestDecidable('UnderReview')).toBe(true)
    expect(isMaterialRequestDecidable('AwaitingProcurementApproval')).toBe(true)
  })

  it('freezes requests that already carry a decision', () => {
    expect(isMaterialRequestDecidable('Approved')).toBe(false)
    expect(isMaterialRequestDecidable('Rejected')).toBe(false)
    expect(isMaterialRequestDecidable('Completed')).toBe(false)
    expect(isMaterialRequestDecidable('Cancelled')).toBe(false)
    expect(isMaterialRequestDecidable(undefined)).toBe(false)
  })
})

describe('RequestAnalysisAgent flag presentation', () => {
  // Mirrors every flag in backend/agent_service/request_agent.py.
  const AGENT_FLAGS = ['HIGH_URGENCY', 'LARGE_QUANTITY_ORDER', 'BULK_ORDER']

  it('has a tone for every flag the agent can emit', () => {
    const unmapped = AGENT_FLAGS.filter((flag) => !(flag in REQUEST_ANALYSIS_FLAG_TONES))
    expect(unmapped).toEqual([])
  })

  it('renders HIGH_URGENCY as a danger, not a success', () => {
    // An urgent request must never be dressed up as a green/approved state.
    expect(requestAnalysisFlagTone('HIGH_URGENCY')).toBe('danger')
    expect(requestAnalysisFlagTone('LARGE_QUANTITY_ORDER')).toBe('warning')
  })

  it('explains each flag to the manager', () => {
    AGENT_FLAGS.forEach((flag) => {
      expect(requestAnalysisFlagLabel(flag).length).toBeGreaterThan(0)
      expect(requestAnalysisFlagLabel(flag)).not.toBe(flag)
    })
  })

  it('falls back safely for a flag the frontend has not seen yet', () => {
    // A newer agent must not render an unexplained blank badge.
    expect(requestAnalysisFlagTone('BRAND_NEW_FLAG')).toBe('neutral')
    expect(requestAnalysisFlagLabel('BRAND_NEW_FLAG')).toBe('BRAND_NEW_FLAG')
    expect(requestAnalysisFlagTone(undefined)).toBe('neutral')
  })
})
