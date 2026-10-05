import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MaterialRequestsPage from './MaterialRequestsPage'

// Auth is mocked so each test can act as a different signed-in role without a
// real session; qualityApi is mocked so no backend is needed.
const authState = vi.hoisted(() => ({ roles: [] }))

// The project and material catalogues the create form reads. They are plain
// state rather than mock functions because `vi.resetAllMocks()` below wipes
// implementations: a `vi.fn(() => catalogue)` would return undefined in every
// test and the form would crash on `.map`.
const catalogue = vi.hoisted(() => ({
  projects: [],
  materials: [],
}))

const defaultProjects = [{ id: 1, name: 'Riverside Apartments — Block C' }]
const defaultMaterials = [{ id: 1, name: 'OPC Cement', unit: 'bags' }]

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ hasRole: (role) => authState.roles.includes(role) }),
}))

vi.mock('../services/qualityApi', () => ({
  qualityApi: {
    listMyMaterialRequests: vi.fn(),
    listMaterialRequests: vi.fn(),
    getMaterialRequest: vi.fn(),
    decideMaterialRequest: vi.fn(),
    analyzeRequest: vi.fn(),
    createMaterialRequest: vi.fn(),
    projects: () => catalogue.projects,
    materials: () => catalogue.materials,
  },
}))

import { qualityApi } from '../services/qualityApi'

const pendingRequest = {
  id: 1,
  projectId: 1,
  projectName: 'Riverside Apartments — Block C',
  requiredDate: '2026-10-05',
  reason: 'Ground floor column concreting',
  status: 'PendingApproval',
  itemCount: 1,
  quotationCount: 0,
  requestDate: '2026-09-27',
  priority: 'High',
}

const requestDetail = {
  id: 1,
  projectId: 1,
  projectName: 'Riverside Apartments — Block C',
  requiredDate: '2026-10-05',
  reason: 'Ground floor column concreting',
  status: 'PendingApproval',
  priority: 'High',
  items: [
    {
      id: 11,
      materialId: 1,
      materialName: 'OPC Cement',
      unit: 'bags',
      requestedQuantity: 500,
      description: 'OPC 42.5N, 50 kg bag',
    },
  ],
}

// The row the site engineer creates while the manager's page is already open:
// it exists in the database (their own list shows it) but the manager's page
// only picks it up if it asks the API again.
const submittedRequest = {
  id: 57,
  projectId: 1,
  projectName: 'Site Engineer Submission',
  requiredDate: '2026-10-11',
  reason: 'Block C slab concreting',
  status: 'PendingApproval',
  itemCount: 1,
  quotationCount: 0,
  requestDate: '2026-09-28',
  priority: 'High',
}

async function renderAsManager() {
  authState.roles = ['ProcurementManager']
  qualityApi.listMaterialRequests.mockResolvedValue([pendingRequest])
  qualityApi.getMaterialRequest.mockResolvedValue(requestDetail)
  render(<MaterialRequestsPage />)
  fireEvent.click(await screen.findByRole('button', { name: 'Review' }))
  // Wait for the detail fetch so the Decision card is on screen.
  await screen.findByText('OPC Cement')
}

beforeEach(() => {
  vi.resetAllMocks()
  authState.roles = []
  catalogue.projects = defaultProjects
  catalogue.materials = defaultMaterials
})

describe('MaterialRequestsPage (Step 2 — manager reviews request)', () => {
  it('shows a pending request with a warning status badge and a Review action for the Procurement Manager', async () => {
    authState.roles = ['ProcurementManager']
    qualityApi.listMaterialRequests.mockResolvedValue([pendingRequest])

    render(<MaterialRequestsPage />)

    const badge = await screen.findByText('PendingApproval')
    // Regression: the page passed `tone`, which StatusBadge ignores, so every
    // badge rendered with the default neutral colour.
    expect(badge.className).toContain('badge--warning')
    expect(screen.getByRole('button', { name: 'Review' })).toBeInTheDocument()
    // Approvers load every status so a decision (→ Approved) stays visible.
    expect(qualityApi.listMaterialRequests).toHaveBeenCalledWith('all')
    expect(screen.queryByRole('button', { name: '+ New Material Request' })).not.toBeInTheDocument()
  })

  it('keeps a freshly submitted request reachable by the manager, and keeps decided rows listed', async () => {
    // Regression (MR-52/MR-53): the Site Engineer's request showed in their own
    // list but never reached the Procurement Manager, because the approver view
    // asked for a blank status and the API answered with Approved rows only.
    authState.roles = ['ProcurementManager']
    qualityApi.listMaterialRequests.mockResolvedValue([
      pendingRequest,
      { ...pendingRequest, id: 2, status: 'Approved', requiredDate: '2026-10-01' },
    ])

    render(<MaterialRequestsPage />)

    // The approver view must ask for every status, never a blank one.
    await screen.findByText('PendingApproval')
    expect(qualityApi.listMaterialRequests).toHaveBeenCalledWith('all')

    // The undecided row is actionable, the decided one stays visible but frozen.
    expect(screen.getByText('Approved')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Review' })).toHaveLength(1)
  })

  it('lets the manager approve the request, then reports that procurement can begin', async () => {
    await renderAsManager()

    expect(screen.getByText('500')).toBeInTheDocument()
    expect(screen.getByText('High')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Approve' }))

    await waitFor(() =>
      expect(qualityApi.decideMaterialRequest).toHaveBeenCalledWith(1, 'Approved', null)
    )
    expect(await screen.findByText(/procurement can begin/i)).toBeInTheDocument()
  })

  it('requires a comment before rejecting, then sends the comment with the decision', async () => {
    await renderAsManager()

    fireEvent.click(screen.getByRole('button', { name: 'Reject' }))
    expect(await screen.findByText(/comment is required/i)).toBeInTheDocument()
    expect(qualityApi.decideMaterialRequest).not.toHaveBeenCalled()

    fireEvent.change(screen.getByLabelText(/comment/i), {
      target: { value: 'Add delivery window' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Reject' }))

    await waitFor(() =>
      expect(qualityApi.decideMaterialRequest).toHaveBeenCalledWith(1, 'Rejected', 'Add delivery window')
    )
  })

  it('records a Request Revision decision with its comment', async () => {
    await renderAsManager()

    fireEvent.change(screen.getByLabelText(/comment/i), {
      target: { value: 'Quantity does not match the BOQ' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Request Revision' }))

    await waitFor(() =>
      expect(qualityApi.decideMaterialRequest).toHaveBeenCalledWith(
        1,
        'RevisionRequested',
        'Quantity does not match the BOQ'
      )
    )
    expect(await screen.findByText(/sent back to the site team/i)).toBeInTheDocument()
  })

  it('never shows Review or approval actions for a Procurement Officer', async () => {
    authState.roles = ['ProcurementOfficer']
    qualityApi.listMaterialRequests.mockResolvedValue([pendingRequest])

    render(<MaterialRequestsPage />)

    expect(await screen.findByText('PendingApproval')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Review' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Request Revision' })).not.toBeInTheDocument()
    // Read-only, but not a pending-only queue: the Officer's work begins once a
    // request is Approved, so hiding those rows hid the rows they act on. This
    // assertion previously pinned the buggy `undefined` (pending-only) filter.
    expect(qualityApi.listMaterialRequests).toHaveBeenCalledWith('all')
  })

  it('renders the create-flow buttons with the design-system button styles', async () => {
    // Regression: these buttons used `primary-button` / `text-button` classes
    // that exist in no stylesheet, so the browser drew them unstyled.
    authState.roles = ['SiteEngineer']
    qualityApi.listMyMaterialRequests.mockResolvedValue([])

    render(<MaterialRequestsPage />)

    const create = await screen.findByRole('button', { name: '+ New Material Request' })
    expect(create.className).toContain('bw-button--primary')

    fireEvent.click(create)

    expect(screen.getByRole('button', { name: 'Cancel' }).className).toContain('bw-button--secondary')
    expect(screen.getByRole('button', { name: 'Submit Request' }).className).toContain('bw-button--primary')
    expect(qualityApi.listMaterialRequests).not.toHaveBeenCalled()
  })

  it('refreshes the queue when the manager comes back to the tab, so a request the site team just created appears', async () => {
    // Regression: the list was fetched once at mount and never again, so a
    // request the Site Engineer created in their own session (another browser
    // or the Flutter app, same database) stayed invisible to the Procurement
    // Manager until the whole page was reloaded by hand.
    authState.roles = ['ProcurementManager']
    qualityApi.listMaterialRequests
      .mockResolvedValueOnce([pendingRequest])
      .mockResolvedValueOnce([pendingRequest, submittedRequest])

    render(<MaterialRequestsPage />)

    await screen.findByText('PendingApproval')
    expect(qualityApi.listMaterialRequests).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('Site Engineer Submission')).not.toBeInTheDocument()

    fireEvent(window, new Event('focus'))

    expect(await screen.findByText('Site Engineer Submission')).toBeInTheDocument()
    // The background refresh keeps asking for the every-status queue.
    expect(qualityApi.listMaterialRequests).toHaveBeenCalledTimes(2)
    expect(qualityApi.listMaterialRequests).toHaveBeenLastCalledWith('all')
  })

  it('lets the manager force a refresh from the toolbar', async () => {
    authState.roles = ['ProcurementManager']
    qualityApi.listMaterialRequests.mockResolvedValue([pendingRequest])

    render(<MaterialRequestsPage />)

    await screen.findByText('PendingApproval')
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() =>
      expect(qualityApi.listMaterialRequests).toHaveBeenCalledTimes(2)
    )
  })

  it('reloads the list after the site engineer submits, so the new request is not missing from their queue', async () => {
    // Regression: the submit screen returned to the list loaded at mount, so
    // the request that had just been created was absent from the queue.
    authState.roles = ['SiteEngineer']
    qualityApi.listMyMaterialRequests
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([submittedRequest])
    qualityApi.createMaterialRequest.mockResolvedValue({ id: 57, status: 'PendingApproval' })

    render(<MaterialRequestsPage />)

    fireEvent.click(await screen.findByRole('button', { name: '+ New Material Request' }))
    fireEvent.change(screen.getByRole('combobox', { name: /^project/i }), {
      target: { value: 'Riverside Apartments — Block C' },
    })
    fireEvent.change(screen.getByRole('combobox', { name: /^material/i }), {
      target: { value: 'OPC Cement' },
    })
    fireEvent.change(screen.getByLabelText(/request date/i), {
      target: { value: '2026-10-01' },
    })
    fireEvent.change(screen.getByLabelText(/required date/i), {
      target: { value: '2026-10-11' },
    })
    fireEvent.change(screen.getByLabelText(/quantity/i), {
      target: { value: '25' },
    })
    fireEvent.change(screen.getByLabelText(/reason/i), {
      target: { value: 'Block C slab concreting' },
    })
    fireEvent.change(screen.getByLabelText(/site notes/i), {
      target: { value: 'Crane access from Gate 2.' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Submit Request' }))

    expect(await screen.findByText('Request submitted')).toBeInTheDocument()
    expect(qualityApi.createMaterialRequest).toHaveBeenCalledTimes(1)

    fireEvent.click(screen.getByRole('button', { name: /back to list/i }))

    expect(await screen.findByText('Site Engineer Submission')).toBeInTheDocument()
    expect(qualityApi.listMyMaterialRequests).toHaveBeenCalledTimes(2)
  })
})

// Step 3 — Trigger Request Analysis. The RequestAnalysisAgent bridge
// (POST /api/agent/analyze-request/{id}) already existed server-side; nothing on
// this page could reach it, so the manager had no Analyze action at all.
describe('MaterialRequestsPage (Step 3 — trigger request analysis)', () => {
  // MR-52: already Approved, so not "decidable" — the exact request in the
  // reported walkthrough, which had no action of any kind.
  const approvedRequest = {
    id: 52,
    projectId: 1,
    projectName: 'Riverside Apartments — Block C',
    requiredDate: '2026-10-10',
    reason: 'Urgent structural fix',
    status: 'Approved',
    itemCount: 1,
    quotationCount: 0,
    requestDate: '2026-09-30',
    priority: 'Urgent',
  }

  const analysisResult = {
    requestId: 52,
    flags: ['HIGH_URGENCY', 'LARGE_QUANTITY_ORDER'],
    status: 'Analyzed',
  }

  async function openApprovedRequest() {
    authState.roles = ['ProcurementManager']
    qualityApi.listMaterialRequests.mockResolvedValue([approvedRequest])
    qualityApi.getMaterialRequest.mockResolvedValue({
      ...approvedRequest,
      items: [
        { id: 520, materialId: 1, materialName: 'OPC Cement', unit: 'bags', requestedQuantity: 500 },
      ],
    })
    render(<MaterialRequestsPage />)
    // Regression: the row action was gated on isMaterialRequestDecidable, so an
    // Approved request rendered an empty Actions cell and could not be opened.
    fireEvent.click(await screen.findByRole('button', { name: 'View & Analyze' }))
    await screen.findByText('OPC Cement')
  }

  it('lets the manager open an already-approved request, which previously had no action', async () => {
    await openApprovedRequest()

    expect(screen.getByText('Material Request #52')).toBeInTheDocument()
    // An approved request must not offer a decision.
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
  })

  it('offers an Analyze Request action to the Procurement Manager', async () => {
    await openApprovedRequest()

    expect(screen.getByRole('button', { name: 'Analyze Request' })).toBeInTheDocument()
    expect(qualityApi.analyzeRequest).not.toHaveBeenCalled()
  })

  it('runs the agent and renders the HIGH_URGENCY and LARGE_QUANTITY_ORDER flags', async () => {
    qualityApi.analyzeRequest.mockResolvedValue(analysisResult)
    await openApprovedRequest()

    fireEvent.click(screen.getByRole('button', { name: 'Analyze Request' }))

    expect(await screen.findByText('HIGH_URGENCY')).toBeInTheDocument()
    expect(screen.getByText('LARGE_QUANTITY_ORDER')).toBeInTheDocument()
    expect(qualityApi.analyzeRequest).toHaveBeenCalledWith(52)
    // The agent result is attributed and shown as advisory, not as a decision.
    expect(screen.getByText('RequestAnalysisAgent')).toBeInTheDocument()
    expect(screen.getByText('Analyzed')).toBeInTheDocument()
  })

  it('reports a clear message when the agent call fails, without losing the request', async () => {
    qualityApi.analyzeRequest.mockRejectedValue(new Error('Agent service unavailable'))
    await openApprovedRequest()

    fireEvent.click(screen.getByRole('button', { name: 'Analyze Request' }))

    expect(await screen.findByText('Agent service unavailable')).toBeInTheDocument()
    // The manager must still be able to read the request and go back.
    expect(screen.getByText('OPC Cement')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Back to list' })).toBeInTheDocument()
  })

  it('shows no-flags confirmation rather than an empty panel when nothing is flagged', async () => {
    qualityApi.analyzeRequest.mockResolvedValue({ requestId: 52, flags: [], status: 'Analyzed' })
    await openApprovedRequest()

    fireEvent.click(screen.getByRole('button', { name: 'Analyze Request' }))

    expect(await screen.findByText('No flags raised.')).toBeInTheDocument()
  })

  it('does not offer the analysis action to a site role', async () => {
    // The backend restricts the bridge to approvers (MaterialRequestApprovalOnly);
    // the UI must not offer a button that could only ever 403.
    authState.roles = ['SiteEngineer']
    qualityApi.listMyMaterialRequests.mockResolvedValue([approvedRequest])
    render(<MaterialRequestsPage />)

    await screen.findByText(approvedRequest.projectName)
    expect(screen.queryByRole('button', { name: 'Analyze Request' })).not.toBeInTheDocument()
  })
})

// Site roles see the whole site queue read-only. Previously /material-requests/my
// was filtered to rows the caller raised, so a Site Officer who had never
// submitted a request saw an empty list and MR-58 never appeared for them.
describe('MaterialRequestsPage (site roles track the whole site queue)', () => {
  const colleagueRequest = {
    id: 58,
    projectId: 1,
    projectName: 'Riverside Apartments — Block C',
    requiredDate: '2026-10-08',
    reason: 'Urgent requirement for ongoing masonry',
    status: 'Approved',
    itemCount: 1,
    quotationCount: 0,
    requestDate: '2026-09-28',
    priority: 'Urgent',
  }

  async function renderAsSiteOfficer() {
    authState.roles = ['SiteOfficer']
    qualityApi.listMyMaterialRequests.mockResolvedValue([colleagueRequest])
    qualityApi.getMaterialRequest.mockResolvedValue({
      ...colleagueRequest,
      items: [{ id: 580, materialId: 1, materialName: 'OPC Cement', unit: 'bags', requestedQuantity: 500 }],
    })
    render(<MaterialRequestsPage />)
  }

  it('lists a request the site officer did not raise, with its decided status', async () => {
    await renderAsSiteOfficer()

    // The row and its Approved status are both visible without any action click.
    expect(await screen.findByText('Approved')).toBeInTheDocument()
    expect(qualityApi.listMyMaterialRequests).toHaveBeenCalledTimes(1)
  })

  it('lets a site role open the request read-only', async () => {
    await renderAsSiteOfficer()

    fireEvent.click(await screen.findByRole('button', { name: 'View' }))

    expect(await screen.findByText('Material Request #58')).toBeInTheDocument()
    expect(screen.getByText('OPC Cement')).toBeInTheDocument()
    // Read-only: no decision, no agent analysis.
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Analyze Request' })).not.toBeInTheDocument()
  })

  it('explains the read-only state instead of implying a broken request', async () => {
    await renderAsSiteOfficer()

    fireEvent.click(await screen.findByRole('button', { name: 'View' }))
    await screen.findByText('Material Request #58')

    // The manager-only wording made a site user think the request was unusable.
    expect(screen.getByText(/Read-only\./)).toBeInTheDocument()
  })

  it('hides the new material request button for Site Officers', async () => {
    await renderAsSiteOfficer()

    expect(screen.queryByRole('button', { name: '+ New Material Request' })).not.toBeInTheDocument()
  })
})

// Step 4 — the Procurement Officer reads the queue. They were previously served
// the PendingApproval-only queue, which hid every Approved request including
// MR-52, even though moving an approved request through RFQ/quotation is
// precisely their job.
describe('MaterialRequestsPage (Procurement Officer reads the full queue)', () => {
  const approvedRequest = {
    id: 52,
    projectId: 1,
    projectName: 'Riverside Apartments — Block C',
    requiredDate: '2026-10-10',
    reason: 'Urgent structural fix',
    status: 'Approved',
    itemCount: 1,
    quotationCount: 0,
    requestDate: '2026-09-30',
    priority: 'Urgent',
  }

  async function renderAsOfficer() {
    authState.roles = ['ProcurementOfficer']
    qualityApi.listMaterialRequests.mockResolvedValue([approvedRequest])
    qualityApi.getMaterialRequest.mockResolvedValue({
      ...approvedRequest,
      items: [
        { id: 520, materialId: 1, materialName: 'Cement (50kg bag)', unit: 'bags', requestedQuantity: 500 },
      ],
    })
    render(<MaterialRequestsPage />)
  }

  it('requests the every-status queue, not the pending-only default', async () => {
    // Regression: the Officer fell through to the default filter, so the API was
    // asked for PendingApproval and MR-52 (Approved) never arrived.
    await renderAsOfficer()

    expect(await screen.findByText('Approved')).toBeInTheDocument()
    expect(qualityApi.listMaterialRequests).toHaveBeenCalledWith('all')
  })

  it('shows MR-52 with its Approved status and Urgent priority', async () => {
    await renderAsOfficer()

    expect(await screen.findByText('Approved')).toBeInTheDocument()
    expect(screen.getByText('Urgent')).toBeInTheDocument()
    expect(screen.getByText('Riverside Apartments — Block C')).toBeInTheDocument()
  })

  it('lets the Officer open the request and read the 500-bag quantity', async () => {
    await renderAsOfficer()

    // The Officer previously had no Actions column at all, so the quantity was
    // unreachable even when the row was listed.
    fireEvent.click(await screen.findByRole('button', { name: 'View' }))

    expect(await screen.findByText('Material Request #52')).toBeInTheDocument()
    expect(screen.getByText('Cement (50kg bag)')).toBeInTheDocument()
    expect(screen.getByText('500')).toBeInTheDocument()
  })

  it('gives the Officer no approval or agent-analysis controls', async () => {
    await renderAsOfficer()

    fireEvent.click(await screen.findByRole('button', { name: 'View' }))
    await screen.findByText('Material Request #52')

    // MaterialRequestApprovalOnly still governs the backend; the UI must match.
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Analyze Request' })).not.toBeInTheDocument()
  })

  it('points the Officer at their next step instead of implying a broken request', async () => {
    await renderAsOfficer()

    fireEvent.click(await screen.findByRole('button', { name: 'View' }))
    await screen.findByText('Material Request #52')

    expect(screen.getByText(/Read-only\./)).toBeInTheDocument()
    expect(screen.getByText(/RFQ/i)).toBeInTheDocument()
  })
})

// Create-form validation: each field has a type (project/material/justification/
// site notes are strings, quantity is numbers only) and the required date must
// be after the request date. The backend re-validates; this is the fast answer.
describe('MaterialRequestsPage (create form validation)', () => {
  beforeEach(() => {
    authState.roles = ['SiteEngineer']
    qualityApi.listMyMaterialRequests.mockResolvedValue([])
  })

  async function openCreateForm() {
    render(<MaterialRequestsPage />)
    fireEvent.click(await screen.findByRole('button', { name: '+ New Material Request' }))
  }

  function fillValidForm() {
    // Project and Material are typed text now, so they start empty and have to
    // be filled like any other field.
    fireEvent.change(screen.getByRole('combobox', { name: /^project/i }), {
      target: { value: 'Riverside Apartments — Block C' },
    })
    fireEvent.change(screen.getByRole('combobox', { name: /^material/i }), {
      target: { value: 'OPC Cement' },
    })
    fireEvent.change(screen.getByLabelText(/request date/i), { target: { value: '2026-10-01' } })
    fireEvent.change(screen.getByLabelText(/required date/i), { target: { value: '2026-10-11' } })
    fireEvent.change(screen.getByLabelText(/quantity/i), { target: { value: '25' } })
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: 'Block C slab concreting' } })
    fireEvent.change(screen.getByLabelText(/site notes/i), { target: { value: 'Crane access from Gate 2.' } })
  }

  it('refuses an empty project, because the project is typed in as a name', async () => {
    await openCreateForm()
    fillValidForm()
    fireEvent.change(screen.getByRole('combobox', { name: /^project/i }), { target: { value: '  ' } })

    fireEvent.click(screen.getByRole('button', { name: 'Submit Request' }))

    expect(await screen.findAllByText('Enter a project name.')).not.toHaveLength(0)
    expect(qualityApi.createMaterialRequest).not.toHaveBeenCalled()
  })

  it('refuses a required date that is not after the request date', async () => {
    await openCreateForm()
    fillValidForm()
    fireEvent.change(screen.getByLabelText(/required date/i), { target: { value: '2026-10-01' } })

    fireEvent.click(screen.getByRole('button', { name: 'Submit Request' }))

    // The message appears both beside the field and in the summary banner.
    expect(await screen.findAllByText('Required date must be after the request date.')).not.toHaveLength(0)
    expect(qualityApi.createMaterialRequest).not.toHaveBeenCalled()
  })

  it('requires justification and site notes to be non-empty strings', async () => {
    await openCreateForm()
    fireEvent.change(screen.getByLabelText(/request date/i), { target: { value: '2026-10-01' } })
    fireEvent.change(screen.getByLabelText(/required date/i), { target: { value: '2026-10-11' } })
    fireEvent.change(screen.getByLabelText(/quantity/i), { target: { value: '25' } })

    fireEvent.click(screen.getByRole('button', { name: 'Submit Request' }))

    expect(await screen.findAllByText('Enter a justification for this request.')).not.toHaveLength(0)
    expect(screen.getAllByText('Enter site notes for this request.').length).toBeGreaterThan(0)
    expect(qualityApi.createMaterialRequest).not.toHaveBeenCalled()
  })

  it('accepts numbers only for quantity and refuses zero', async () => {
    await openCreateForm()
    fillValidForm()
    fireEvent.change(screen.getByLabelText(/quantity/i), { target: { value: '0' } })

    fireEvent.click(screen.getByRole('button', { name: 'Submit Request' }))

    expect(await screen.findAllByText('Quantity must be a positive number greater than 0.')).not.toHaveLength(0)
    expect(qualityApi.createMaterialRequest).not.toHaveBeenCalled()
  })

  it('pops a success dialog after the request is created', async () => {
    qualityApi.listMyMaterialRequests
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([{ ...submittedRequest, id: 60 }])
    qualityApi.createMaterialRequest.mockResolvedValue({ id: 60, status: 'PendingApproval' })

    await openCreateForm()
    fillValidForm()
    fireEvent.click(screen.getByRole('button', { name: 'Submit Request' }))

    expect(await screen.findByText('Request submitted')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Back to list' })).toBeInTheDocument()
  })

  it('accepts a new typed alphanumeric material without a catalogue suggestion', async () => {
    qualityApi.createMaterialRequest.mockResolvedValue({ id: 61, status: 'PendingApproval' })
    await openCreateForm()
    fillValidForm()
    fireEvent.change(screen.getByRole('combobox', { name: /^material/i }), { target: { value: 'Steel 12mm Grade 500' } })
    fireEvent.click(screen.getByRole('button', { name: 'Submit Request' }))
    await screen.findByText('Request submitted')
    expect(qualityApi.createMaterialRequest).toHaveBeenCalledWith(
      expect.objectContaining({ items: [expect.objectContaining({ materialId: 0, materialName: 'Steel 12mm Grade 500' })] }),
    )
  })

  it('accepts material names containing letters and numbers without selecting a suggestion', async () => {
    catalogue.materials = [{ id: 12, name: 'Steel 12mm Grade 500', unit: 'kg' }]
    qualityApi.createMaterialRequest.mockResolvedValue({ id: 61, status: 'PendingApproval' })
    await openCreateForm()
    fillValidForm()
    fireEvent.change(screen.getByRole('combobox', { name: /^material/i }), {
      target: { value: 'steel 12mm grade 500' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Submit Request' }))
    await screen.findByText('Request submitted')
    expect(qualityApi.createMaterialRequest).toHaveBeenCalledWith(
      expect.objectContaining({ items: [expect.objectContaining({ materialId: 12 })] }),
    )
  })

  it('resolves the project and material from typed text, not a drop-down', async () => {
    catalogue.projects = [
      { id: 1, name: 'Riverside Apartments — Block C' },
      { id: 7, name: 'Hilltop Villa' },
    ]
    catalogue.materials = [
      { id: 1, name: 'OPC Cement', unit: 'bags' },
      { id: 9, name: 'TMT Reinforcement Bar' },
    ]
    qualityApi.createMaterialRequest.mockResolvedValue({ id: 61, status: 'PendingApproval' })

    await openCreateForm()
    fillValidForm()

    // The engineer types ordinary text and picks from what matches.
    const projectField = screen.getByRole('combobox', { name: /^project/i })
    fireEvent.change(projectField, { target: { value: 'hilltop' } })
    fireEvent.click(await screen.findByRole('option', { name: 'Hilltop Villa' }))
    expect(projectField).toHaveValue('Hilltop Villa')

    const materialField = screen.getByRole('combobox', { name: /^material/i })
    fireEvent.change(materialField, { target: { value: 'tmt' } })
    fireEvent.click(await screen.findByRole('option', { name: 'TMT Reinforcement Bar' }))
    expect(materialField).toHaveValue('TMT Reinforcement Bar')

    fireEvent.click(screen.getByRole('button', { name: 'Submit Request' }))

    await screen.findByText('Request submitted')
    // The IDs are still what goes to the API; only the way of choosing changed.
    // The material rides on the request's item line, not at the top level.
    expect(qualityApi.createMaterialRequest).toHaveBeenCalledWith(
      expect.objectContaining({
        projectId: 7,
        items: [expect.objectContaining({ materialId: 9 })],
      }),
    )
  })

  it('accepts a project name that is not in the list and sends it as text', async () => {
    qualityApi.createMaterialRequest.mockResolvedValue({ id: 62, status: 'PendingApproval' })

    await openCreateForm()
    fillValidForm()

    // A site BuildWise has not seen before. The form does not refuse it: the
    // name goes out and the API creates the project.
    const projectField = screen.getByRole('combobox', { name: /^project/i })
    fireEvent.change(projectField, { target: { value: 'Galle Face Promenade' } })
    expect(await screen.findByText(/used as a new project name/i)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Submit Request' }))

    await screen.findByText('Request submitted')
    expect(qualityApi.createMaterialRequest).toHaveBeenCalledWith(
      expect.objectContaining({
        projectName: 'Galle Face Promenade',
        // No id to send, so the API resolves the name itself.
        projectId: 0,
      }),
    )
  })

  it('submits text even when the material matches no catalogue record', async () => {
    await openCreateForm()
    fillValidForm()

    const materialField = screen.getByRole('combobox', { name: /^material/i })
    fireEvent.change(materialField, { target: { value: 'unobtainium' } })

    fireEvent.click(screen.getByRole('button', { name: 'Submit Request' }))

    await screen.findByText('Request submitted')
    expect(qualityApi.createMaterialRequest).toHaveBeenCalledWith(expect.objectContaining({
      items: [expect.objectContaining({ materialId: 0, materialName: 'unobtainium' })],
    }))
  })
})

