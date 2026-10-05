import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import WorkflowPipeline from './WorkflowPipeline'

// Mirrors the viva scenario: 500 bags of cement, budget LKR 1,100,000,
// Supplier B's quotation totals LKR 1,090,000 (LKR 10,000 remaining).
const awaitingWorkflow = {
  id: 9001,
  status: 'AwaitingApproval',
  approvalStatus: 'Pending',
  purchaseOrderId: null,
  recommendation: {
    recommendedQuotationId: 502,
    recommendedSupplierId: 2,
    recommendedSupplierName: 'Supplier B Traders',
    rationale: 'Lowest landed cost among the compliant quotations.',
    rankedAlternatives: [
      { quotationId: 501, supplierId: 1, supplierName: 'Supplier A', rank: 1, totalAmount: 1125000, reason: 'Full coverage.' },
      { quotationId: 502, supplierId: 2, supplierName: 'Supplier B Traders', rank: 2, totalAmount: 1090000, reason: 'Lowest compliant total.' },
    ],
  },
  validation: { isValid: true, errors: [], warnings: [] },
  steps: [],
}

describe('WorkflowPipeline (scenario approval flow)', () => {
  it('renders the six narrative stages in order', () => {
    const { container } = render(<WorkflowPipeline workflow={awaitingWorkflow} />)

    const labels = [...container.querySelectorAll('.pipeline__label')].map((el) => el.textContent)
    expect(labels).toEqual([
      'AI Recommendation',
      'Deterministic Validation',
      'Pending Manager Approval',
      'Procurement Manager',
      'Approve / Reject / Request Revision',
      'Purchase Order',
    ])
  })

  it('shows validation passed and the manager stages as current while approval is pending', () => {
    const { container } = render(<WorkflowPipeline workflow={awaitingWorkflow} />)

    expect(screen.getByText(/Deterministic validation passed/)).toBeInTheDocument()
    expect(screen.getByText('Supplier B Traders — LKR 1,090,000 (lowest compliant landed cost).')).toBeInTheDocument()

    const active = [...container.querySelectorAll('.pipeline__step--active')].map(
      (el) => el.querySelector('.pipeline__label')?.textContent
    )
    expect(active).toContain('Pending Manager Approval')
    expect(active).toContain('Procurement Manager')
  })

  it('keeps the purchase order locked until the manager approves', () => {
    render(<WorkflowPipeline workflow={awaitingWorkflow} />)

    expect(screen.getByText(/Locked until the manager approves/)).toBeInTheDocument()
    expect(screen.getByText(/officer cannot create the PO/)).toBeInTheDocument()
    expect(screen.queryByText(/Purchase Order #/)).not.toBeInTheDocument()
  })

  it('shows the budget figures from the deck: 1,100,000 budget, 10,000 remaining', () => {
    render(<WorkflowPipeline workflow={awaitingWorkflow} budget={1100000} />)

    expect(screen.getByText('LKR 1,100,000')).toBeInTheDocument()
    expect(screen.getByText('LKR 1,090,000')).toBeInTheDocument()
    expect(screen.getByText('Remaining LKR 10,000')).toBeInTheDocument()
    expect(screen.queryByText(/Over budget/)).not.toBeInTheDocument()
  })

  it('flags an over-budget recommendation', () => {
    render(<WorkflowPipeline workflow={awaitingWorkflow} budget={1050000} />)

    expect(screen.getByText('Over budget by LKR 40,000')).toBeInTheDocument()
  })

  it('after approval, unlocks and names the created purchase order', () => {
    const approved = {
      ...awaitingWorkflow,
      status: 'Completed',
      approvalStatus: 'Approved',
      purchaseOrderId: 701,
    }
    const { container } = render(<WorkflowPipeline workflow={approved} />)

    expect(screen.getByText('Purchase Order #701 created.')).toBeInTheDocument()
    expect(screen.getByText('Recorded decision: Approved.')).toBeInTheDocument()
    expect(screen.queryByText(/Locked until the manager approves/)).not.toBeInTheDocument()

    const poStage = [...container.querySelectorAll('.pipeline__step')].find(
      (el) => el.querySelector('.pipeline__label')?.textContent === 'Purchase Order'
    )
    expect(poStage.className).toContain('pipeline__step--done')
  })

  it('blocks the manager stages when deterministic validation fails', () => {
    const failed = {
      ...awaitingWorkflow,
      validation: { isValid: false, errors: ['Quotation is over the project materials budget.'], warnings: [] },
    }
    const { container } = render(<WorkflowPipeline workflow={failed} />)

    expect(screen.getByText(/Deterministic validation failed/)).toBeInTheDocument()
    expect(screen.getByText('Unlocks once deterministic validation passes.')).toBeInTheDocument()
    expect(screen.queryByText(/Deterministic validation passed/)).not.toBeInTheDocument()

    const pendingStage = [...container.querySelectorAll('.pipeline__step')].find(
      (el) => el.querySelector('.pipeline__label')?.textContent === 'Pending Manager Approval'
    )
    expect(pendingStage.className).toContain('pipeline__step--locked')
  })

  it('renders nothing without a workflow', () => {
    const { container } = render(<WorkflowPipeline workflow={null} />)
    expect(container).toBeEmptyDOMElement()
  })
})
