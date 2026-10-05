const TONES = {
  Active: 'success', Inactive: 'neutral', Suspended: 'danger',
  Submitted: 'info', UnderReview: 'warning', Selected: 'success', Rejected: 'danger', Expired: 'neutral',
  Created: 'info', Confirmed: 'warning', InProgress: 'warning', Completed: 'success', Cancelled: 'danger',
  Pending: 'neutral', Running: 'info', AwaitingApproval: 'warning', Failed: 'danger',
  Approved: 'success', RevisionRequested: 'warning',
  // RFQ statuses (BuildWise.Api/Models/Enums/RfqStatus.cs). Draft is neutral,
  // Issued is the state that still needs supplier responses, Closed is done.
  Draft: 'neutral', Issued: 'warning', Closed: 'success'
}

export const statusTone = (status) => TONES[status] || 'neutral'
