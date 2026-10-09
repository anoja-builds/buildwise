import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import QualityRiskPanel from './QualityRiskPanel'
import { qualityApi } from './services/qualityApi'
vi.mock('./services/qualityApi', () => ({ qualityApi: { analyseInspection: vi.fn(), getWorkflow: vi.fn(), createNcr: vi.fn(), updateCorrectiveAction: vi.fn() } }))
const recommendation = { riskLevel: 'High', evidenceSummary: 'Five damaged bags.', rationaleSummary: 'Rejected material needs review.', ncrRecommended: true, riskFlags: [{ flag: 'Moisture damage', evidenceReferences: ['inspection-item:9'] }], itemRecommendations: [{ inspectionItemId: 9, ncrRecommended: true, suggestedSeverity: 'High', suggestedIssueDescription: 'Wet cement', suggestedCorrectiveAction: 'Replace five bags.', rationale: 'Unusable material', evidenceReferences: ['inspection-item:9'] }] }
const workflow = { workflowId: 20, inspectionId: 1, status: 'Completed', approvalStatus: 'Pending', finalOutcome: 'Advisory analysis completed.', steps: [
  { stepOrder: 2, stepName: 'Run Agentic Quality Analysis', status: 'Completed', structuredResult: { recommendation, trace: [{ action: 'get_current_inspection_evidence', success: true }] } },
  { stepOrder: 3, stepName: 'Validate Quality Recommendation', status: 'Completed', validationResult: { valid: true, advisoryOnly: true } },
] }
beforeEach(() => { vi.resetAllMocks(); qualityApi.analyseInspection.mockResolvedValue(workflow); qualityApi.getWorkflow.mockResolvedValue(workflow) })
const panel = (status = 'Completed') => render(<QualityRiskPanel inspection={{ id: 1, status }} />)

it('runs only on explicit request and displays validated advisory evidence without business mutations', async () => {
  panel(); expect(qualityApi.analyseInspection).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Analyse inspection' }))
  await screen.findByRole('region', { name: 'Validated advisory recommendation' })
  expect(screen.getByText('Moisture damage')).toBeInTheDocument(); expect(screen.getByText('Replace five bags.')).toBeInTheDocument()
  expect(screen.getByText('Validation results')).toBeInTheDocument()
  expect(qualityApi.createNcr).not.toHaveBeenCalled(); expect(qualityApi.updateCorrectiveAction).not.toHaveBeenCalled()
  expect(screen.queryByRole('button', { name: /Approve|Create NCR|Save corrective action/ })).not.toBeInTheDocument()
})
it('cannot start analysis before completion', () => { panel('UnderInspection'); expect(screen.queryByRole('button', { name: 'Analyse inspection' })).not.toBeInTheDocument() })
it('retrieves persisted workflows by ID', async () => {
  panel(); fireEvent.change(screen.getByLabelText(/Workflow ID/), { target: { value: '20' } }); fireEvent.click(screen.getByRole('button', { name: 'Load workflow' }))
  await waitFor(() => expect(qualityApi.getWorkflow).toHaveBeenCalledWith(20)); await screen.findByText('Risk: High')
})
it('rejects results for another inspection', async () => {
  qualityApi.getWorkflow.mockResolvedValue({ ...workflow, inspectionId: 99 }); panel()
  fireEvent.change(screen.getByLabelText(/Workflow ID/), { target: { value: '20' } }); fireEvent.click(screen.getByRole('button', { name: 'Load workflow' }))
  await screen.findByText(/belongs to a different inspection/); expect(screen.queryByText('Risk: High')).not.toBeInTheDocument()
})
it('preserves failed workflow details from HTTP 502 and does not present unvalidated advice as successful', async () => {
  qualityApi.analyseInspection.mockRejectedValue(Object.assign(new Error('502'), { status: 502, data: { ...workflow, status: 'Failed', steps: [workflow.steps[0], { stepOrder: 3, stepName: 'Validate Quality Recommendation', status: 'Failed', error: 'Invalid evidence reference.', validationResult: { valid: false } }] } }))
  panel(); fireEvent.click(screen.getByRole('button', { name: 'Analyse inspection' }))
  await screen.findByText('Analysis failed'); expect(screen.getByText('Invalid evidence reference.')).toBeInTheDocument()
  expect(screen.queryByRole('region', { name: 'Validated advisory recommendation' })).not.toBeInTheDocument()
})
it('shows service failure without inventing workflow results', async () => {
  qualityApi.analyseInspection.mockRejectedValue(new Error('API unavailable')); panel(); fireEvent.click(screen.getByRole('button', { name: 'Analyse inspection' }))
  await screen.findByText('API unavailable'); expect(screen.queryByText('Risk: High')).not.toBeInTheDocument()
})
it('prevents duplicate analysis while a request is pending', async () => {
  let finish; qualityApi.analyseInspection.mockReturnValue(new Promise((resolve) => { finish = resolve }))
  panel(); const button = screen.getByRole('button', { name: 'Analyse inspection' }); fireEvent.click(button); fireEvent.click(button)
  expect(qualityApi.analyseInspection).toHaveBeenCalledOnce(); expect(button).toBeDisabled()
  finish(workflow); await screen.findByText('Risk: High')
})
