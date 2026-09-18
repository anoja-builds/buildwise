const TONES = {
  Active: 'success', Inactive: 'neutral', Suspended: 'danger',
  Submitted: 'info', UnderReview: 'warning', Selected: 'success', Rejected: 'danger', Expired: 'neutral',
  Created: 'info', Confirmed: 'warning', InProgress: 'warning', Completed: 'success', Cancelled: 'danger',
  Pending: 'neutral', Running: 'info', AwaitingApproval: 'warning', Failed: 'danger',
  Approved: 'success', RevisionRequested: 'warning'
}

export const statusTone = (status) => TONES[status] || 'neutral'
