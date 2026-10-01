import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import AIRecommendationReview from './AIRecommendationReview'

const workflow = {
  status: 'AwaitingApproval', approvalStatus: 'Pending', validation: { isValid: true }, steps: [],
  recommendation: {
    recommendedSupplierId: 1, recommendedSupplierName: 'Deterministic winner', recommendedQuotationId: 11,
    rationale: 'Lowest-cost compliant supplier.', executionMode: 'AgenticAI', warnings: [],
    rankedAlternatives: [{ totalAmount: 525000 }],
    advisory: { summary: 'Confirm delivery timing.', riskLevel: 'Medium', risks: ['Limited order history.'],
      clarificationQuestions: ['Can the required date be met?'], recommendedFollowUps: ['Obtain a delivery commitment.'] },
  },
}

it('shows advisory analysis alongside the fixed winner and human approval notice', () => {
  render(<AIRecommendationReview workflow={workflow} />)
  expect(screen.getByText(/AgenticAI: read-only advisory analysis/)).toBeInTheDocument()
  expect(screen.getByText('Deterministic winner')).toBeInTheDocument()
  expect(screen.getByText(/Lowest-cost compliant supplier/)).toBeInTheDocument()
  expect(screen.getByText('Confirm delivery timing.')).toBeInTheDocument()
  expect(screen.getByText('Limited order history.')).toBeInTheDocument()
  expect(screen.getByText('Can the required date be met?')).toBeInTheDocument()
  expect(screen.getByText('Obtain a delivery commitment.')).toBeInTheDocument()
  expect(screen.getByText(/requires Procurement Manager approval/)).toBeInTheDocument()
})

it('labels fallback and retains the deterministic rationale', () => {
  render(<AIRecommendationReview workflow={{ ...workflow, recommendation: {
    ...workflow.recommendation, executionMode: 'DeterministicFallback', advisory: null,
  } }} />)
  expect(screen.getByText(/DeterministicFallback: deterministic rationale/)).toBeInTheDocument()
  expect(screen.getByText('Deterministic winner')).toBeInTheDocument()
  expect(screen.queryByText('Confirm delivery timing.')).not.toBeInTheDocument()
})
