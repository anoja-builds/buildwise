import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import ProcurementApprovalPanel from './ProcurementApprovalPanel'

const awaitingWorkflow = { id: 9001, status: 'AwaitingApproval', approvalStatus: 'Pending' }

describe('ProcurementApprovalPanel', () => {
  it('renders Approve/Reject/Request Revision for a Procurement Manager', () => {
    render(<ProcurementApprovalPanel workflow={awaitingWorkflow} role="Manager" onDecide={() => {}} deciding={false} />)

    expect(screen.getByRole('button', { name: 'Approve' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reject' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Request Revision' })).toBeInTheDocument()
  })

  it('never renders action buttons for a Procurement Officer', () => {
    render(<ProcurementApprovalPanel workflow={awaitingWorkflow} role="Officer" onDecide={() => {}} deciding={false} />)

    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Request Revision' })).not.toBeInTheDocument()
    expect(screen.getByText(/Sign in as Procurement Manager/i)).toBeInTheDocument()
  })

  it('never renders action buttons for a signed-in user with no procurement role', () => {
    render(<ProcurementApprovalPanel workflow={awaitingWorkflow} role="ReadOnly" onDecide={() => {}} deciding={false} />)

    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
  })

  it('does not render action buttons once the workflow has already been decided, even for a Manager', () => {
    const decided = { id: 9001, status: 'Completed', approvalStatus: 'Approved' }
    render(<ProcurementApprovalPanel workflow={decided} role="Manager" onDecide={() => {}} deciding={false} />)

    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.getByText(/already been decided/i)).toBeInTheDocument()
  })
})
