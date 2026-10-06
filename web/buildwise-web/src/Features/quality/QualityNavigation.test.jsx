import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, expect, it, vi } from 'vitest'
import App from '../../App'
import { useAuth } from '../../auth/AuthContext'

vi.mock('../../auth/AuthContext', () => ({ useAuth: vi.fn() }))
vi.mock('../procurement/pages/ProcurementApp', () => ({ default: () => <p>Procurement workspace</p> }))
vi.mock('../../pages/QualityInspectionsPage', () => ({ default: () => <p>Inspection workspace</p> }))
vi.mock('../../pages/NonConformancesPage', () => ({ default: () => <p>NCR workspace</p> }))

function session(roles) {
  return { isAuthenticated: true, roles, hasRole: (role) => roles.includes(role),
    user: { fullName: 'Inspector Silva', roles }, logout: vi.fn() }
}
function renderApp() {
  return render(<MemoryRouter initialEntries={['/quality-inspections']}><App /></MemoryRouter>)
}
beforeEach(() => {
  vi.resetAllMocks()
  useAuth.mockReturnValue(session(['Administrator']))
})
it('opens both quality routes in the shared shell and returns to authorized procurement', async () => {
  renderApp()
  expect(await screen.findByText('Inspection workspace')).toBeInTheDocument()
  expect(screen.getByText('Inspector Silva')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Non-Conformance Reports' }))
  expect(await screen.findByText('NCR workspace')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Procurement Workspace' }))
  expect(await screen.findByText('Procurement workspace')).toBeInTheDocument()
})
it('does not offer commercial procurement routes to a quality inspector', async () => {
  useAuth.mockReturnValue(session(['QualityInspector']))
  renderApp()
  expect(await screen.findByText('Inspection workspace')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Procurement Workspace' })).not.toBeInTheDocument()
})
it('redirects unauthenticated users to the shared login', async () => {
  useAuth.mockReturnValue({ isAuthenticated: false, roles: [], login: vi.fn() })
  renderApp()
  expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Quality Inspections' })).not.toBeInTheDocument()
})
