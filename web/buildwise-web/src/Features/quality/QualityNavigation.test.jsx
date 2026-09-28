import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import App from '../../App'
import { MemoryRouter } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { qualityApi } from './services/qualityApi'
vi.mock('../../auth/AuthContext', () => ({ useAuth: vi.fn() }))
vi.mock('../procurement/pages/ProcurementApp', () => ({ default: () => <p>Procurement workspace</p> }))
vi.mock('./services/qualityApi', () => ({ qualityApi: { listInspections: vi.fn(), listNcrs: vi.fn(), pendingDeliveries: vi.fn() } }))
beforeEach(() => {
  vi.resetAllMocks()
  useAuth.mockReturnValue({ isAuthenticated: true, roles: ['QualityInspector'], user: { fullName: 'Inspector Silva', roles: ['QualityInspector'] }, logout: vi.fn() })
  qualityApi.listInspections.mockResolvedValue([]); qualityApi.listNcrs.mockResolvedValue([]); qualityApi.pendingDeliveries.mockResolvedValue([])
})
it('opens both permitted quality entries and hides procurement', async () => {
  render(<MemoryRouter><App /></MemoryRouter>)
  fireEvent.click(screen.getByRole('link', { name: 'Quality Inspections' }))
  await screen.findByText('No inspections yet')
  expect(screen.getByText('Inspector Silva')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('link', { name: 'Non-Conformances' }))
  await screen.findByText('No non-conformances yet')
  expect(screen.queryByRole('link', { name: 'Procurement' })).not.toBeInTheDocument()
})
it('keeps unauthenticated users at shared login without fetching quality records', () => {
  useAuth.mockReturnValue({ isAuthenticated: false, roles: [], login: vi.fn() }); render(<MemoryRouter><App /></MemoryRouter>)
  expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument()
  expect(screen.queryByRole('link', { name: 'Quality Inspections' })).not.toBeInTheDocument()
  expect(qualityApi.listInspections).not.toHaveBeenCalled()
})
