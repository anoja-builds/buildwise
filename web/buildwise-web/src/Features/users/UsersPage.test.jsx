import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import App from '../../App'
import { useAuth } from '../../auth/AuthContext'
import { usersApi } from './usersApi'

vi.mock('../../auth/AuthContext', () => ({ useAuth: vi.fn() }))
vi.mock('./usersApi', () => ({ usersApi: { list: vi.fn(), create: vi.fn(), setStatus: vi.fn() } }))
const user = { id: 2, fullName: 'Sam Engineer', email: 'sam@example.test', roles: ['SiteEngineer'], isActive: true, createdAt: '2026-10-01T00:00:00Z' }
function mount() { render(<MemoryRouter initialEntries={['/users']}><App /></MemoryRouter>) }
beforeEach(() => {
  vi.resetAllMocks()
  useAuth.mockReturnValue({ isAuthenticated: true, roles: ['Administrator'], user: { id: 1, fullName: 'Admin' }, logout: vi.fn() })
  usersApi.list.mockResolvedValue([user])
})

it('shows the Administrator route and users page', async () => {
  mount()
  expect(await screen.findByText('sam@example.test')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Users & Roles' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Users & Roles' })).toHaveAttribute('href', '/users')
})

it.each(['SiteEngineer', 'ProcurementOfficer', 'ProcurementManager', 'QualityInspector'])('blocks direct access and hides navigation for %s', (role) => {
  useAuth.mockReturnValue({ isAuthenticated: true, roles: [role], user: { id: 2 }, logout: vi.fn() })
  mount()
  expect(screen.queryByRole('link', { name: 'Users & Roles' })).not.toBeInTheDocument()
  expect(screen.queryByRole('heading', { name: 'Users & Roles' })).not.toBeInTheDocument()
  expect(usersApi.list).not.toHaveBeenCalled()
})

async function fillForm() {
  fireEvent.click(await screen.findByRole('button', { name: /Add User/ }))
  fireEvent.change(screen.getByLabelText(/Full Name/), { target: { value: 'New Inspector' } })
  fireEvent.change(screen.getByLabelText(/Email/), { target: { value: 'new@example.test' } })
  fireEvent.change(screen.getByLabelText(/Temporary Password/), { target: { value: 'Password123!' } })
  fireEvent.change(screen.getByLabelText('Role'), { target: { value: 'QualityInspector' } })
  fireEvent.click(screen.getByRole('button', { name: 'Create User' }))
}

it('creates a user with the selected role and clears the password form', async () => {
  usersApi.create.mockResolvedValue({ ...user, id: 3, fullName: 'New Inspector', email: 'new@example.test', roles: ['QualityInspector'] })
  mount()
  await screen.findByText('sam@example.test')
  await fillForm()
  expect(await screen.findByText('new@example.test')).toBeInTheDocument()
  expect(usersApi.create).toHaveBeenCalledWith({ fullName: 'New Inspector', email: 'new@example.test', password: 'Password123!', roleName: 'QualityInspector' })
  expect(screen.queryByLabelText(/Temporary Password/)).not.toBeInTheDocument()
  expect(screen.queryByText('Password123!')).not.toBeInTheDocument()
})

it('shows API validation errors and keeps the form available', async () => {
  usersApi.create.mockRejectedValue(new Error('An account with this email already exists.'))
  mount()
  await screen.findByText('sam@example.test')
  await fillForm()
  expect(await screen.findByRole('alert')).toHaveTextContent('An account with this email already exists.')
  expect(screen.getByLabelText(/Full Name/)).toHaveValue('New Inspector')
})

it('deactivates and reactivates a user', async () => {
  usersApi.setStatus.mockResolvedValueOnce({ ...user, isActive: false }).mockResolvedValueOnce(user)
  mount()
  fireEvent.click(await screen.findByRole('button', { name: 'Deactivate' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Activate' }))
  await waitFor(() => expect(usersApi.setStatus).toHaveBeenLastCalledWith(2, true))
  expect(await screen.findByRole('button', { name: 'Deactivate' })).toBeEnabled()
})

it.each(['Full Name', 'Temporary Password'])('rejects whitespace-only %s before submitting', async (label) => {
  mount()
  await screen.findByText('sam@example.test')
  fireEvent.click(screen.getByRole('button', { name: /Add User/ }))
  fireEvent.change(screen.getByLabelText(/Full Name/), { target: { value: 'New User' } })
  fireEvent.change(screen.getByLabelText(/Email/), { target: { value: 'new@example.test' } })
  fireEvent.change(screen.getByLabelText(/Temporary Password/), { target: { value: 'long-enough' } })
  fireEvent.change(screen.getByLabelText(new RegExp(label)), { target: { value: '        ' } })
  fireEvent.submit(screen.getByRole('button', { name: 'Create User' }).closest('form'))
  expect(await screen.findByRole('alert')).toHaveTextContent('at least 8 characters')
  expect(usersApi.create).not.toHaveBeenCalled()
})
