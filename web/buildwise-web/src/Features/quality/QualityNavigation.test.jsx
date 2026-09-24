import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import App from '../../App'
import { useAuth } from '../../auth/AuthContext'
import { qualityApi } from './services/qualityApi'
vi.mock('../../auth/AuthContext', () => ({ useAuth: vi.fn() }))
vi.mock('../procurement/pages/ProcurementApp', () => ({ default: () => <p>Procurement workspace</p> }))
vi.mock('./services/qualityApi', () => ({ qualityApi: { listInspections: vi.fn(), listNcrs: vi.fn() } }))
beforeEach(() => {
  vi.resetAllMocks()
  useAuth.mockReturnValue({ isAuthenticated: true, roles: ['QualityInspector'], user: { fullName: 'Inspector Silva', roles: ['QualityInspector'] }, logout: vi.fn() })
  qualityApi.listInspections.mockResolvedValue([]); qualityApi.listNcrs.mockResolvedValue([])
})
it('opens both quality sidebar entries in the existing shell and can return to procurement', async () => {
  render(<App />)
  fireEvent.click(screen.getByRole('button', { name: 'Quality Inspections' }))
  await screen.findByText('No inspections yet')
  expect(screen.getByText('Inspector Silva')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Non-Conformances' }))
  await screen.findByText('No non-conformances yet')
  fireEvent.click(screen.getByRole('button', { name: 'Procurement' }))
  expect(screen.getByText('Procurement workspace')).toBeInTheDocument()
})
it('keeps unauthenticated users at shared login without fetching quality records', () => {
  useAuth.mockReturnValue({ isAuthenticated: false, roles: [], login: vi.fn() }); render(<App />)
  expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Quality Inspections' })).not.toBeInTheDocument()
  expect(qualityApi.listInspections).not.toHaveBeenCalled()
})
