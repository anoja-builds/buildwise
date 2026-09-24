import { describe, expect, it } from 'vitest'
import { statusTone } from './statusTone'

describe('statusTone', () => {
  it('maps a Suspended supplier to the danger tone', () => {
    expect(statusTone('Suspended')).toBe('danger')
  })

  it('maps an Active supplier to the success tone', () => {
    expect(statusTone('Active')).toBe('success')
  })

  it('maps AwaitingApproval to the warning tone', () => {
    expect(statusTone('AwaitingApproval')).toBe('warning')
  })

  it('falls back to neutral for an unrecognized status', () => {
    expect(statusTone('SomethingUnexpected')).toBe('neutral')
  })
})
