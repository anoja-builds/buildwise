import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { BrowserRouter, MemoryRouter, useLocation, useNavigate } from 'react-router-dom'
import App from './App'
import { AuthProvider } from './auth/AuthContext'
import { authApi } from './services/authApi'

vi.mock('./Features/MaterialRequests/pages/MaterialRequestsPage', () => ({ default: () => <h1>Material request page</h1> }))
vi.mock('./Features/deliveries/pages/DeliveryDashboard', () => ({ default: () => <h1>Delivery page</h1> }))
vi.mock('./Features/procurement/pages/ProcurementApp', () => ({ default: ({ section, orderId, supplierId, requestId, onNavigate }) => <>
  <h1>{section} workspace {orderId || supplierId || requestId}</h1>
  <button onClick={() => onNavigate({ section: 'Purchase Orders', orderId: 42 })}>Open order 42</button>
</> }))
vi.mock('./Features/quality/QualityApp', () => ({ RoutedQualityApp: ({ kind }) => <h1>Quality {kind}</h1> }))

function HistoryControls() {
  const location = useLocation()
  const navigate = useNavigate()
  return <><output aria-label="Current URL">{location.pathname}</output><button onClick={() => navigate(-1)}>History back</button><button onClick={() => navigate(1)}>History forward</button></>
}
function session(roles) {
  authApi.saveSession({ token: 'test-session', user: { fullName: 'Test User', roles } })
}
function mount(path = '/') {
  return render(<AuthProvider><MemoryRouter initialEntries={[path]}><App /><HistoryControls /></MemoryRouter></AuthProvider>)
}
const navLabels = () => within(screen.getByRole('navigation')).getAllByRole('link').map((link) => link.textContent)
beforeEach(() => { localStorage.clear(); vi.restoreAllMocks() })

describe('route authentication and navigation', () => {
  it('redirects unauthenticated deep links to login without showing feature UI', () => {
    mount('/purchase-orders/42')
    expect(screen.getByRole('button', { name: 'Sign in', exact: true })).toBeInTheDocument()
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('/login')
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })

  it.each([
    ['Administrator', '/dashboard', ['Dashboard', 'Material Requests', 'Suppliers', 'Quotations', 'Procurement', 'Purchase Orders', 'Deliveries', 'Quality Inspections', 'Non-Conformances']],
    ['SiteEngineer', '/material-requests', ['Material Requests']],
    ['ReceivingOfficer', '/deliveries', ['Purchase Orders', 'Deliveries']],
    ['QualityInspector', '/quality-inspections', ['Deliveries', 'Quality Inspections', 'Non-Conformances']],
    ['ProcurementOfficer', '/procurement', ['Dashboard', 'Material Requests', 'Suppliers', 'Quotations', 'Procurement', 'Purchase Orders', 'Deliveries']],
    ['ProcurementManager', '/procurement', ['Dashboard', 'Material Requests', 'Suppliers', 'Quotations', 'Procurement', 'Purchase Orders', 'Deliveries']],
    ['ProjectManager', '/material-requests', ['Material Requests', 'Deliveries']],
  ])('%s receives the permitted navigation and landing page', (role, path, labels) => {
    session([role]); mount()
    expect(navLabels()).toEqual(labels)
    expect(screen.getByLabelText('Current URL')).toHaveTextContent(path)
  })

  it('unions permissions for multiple roles', () => {
    session(['SiteEngineer', 'QualityInspector']); mount()
    expect(navLabels()).toEqual(['Material Requests', 'Deliveries', 'Quality Inspections', 'Non-Conformances'])
  })

  it.each(['/procurement', '/purchase-orders/42', '/quality-inspections/1', '/non-conformances/5', '/users', '/reports', '/ai-workflows'])('blocks unauthorized direct access to %s', (path) => {
    session(['SiteEngineer']); mount(path)
    expect(screen.getByRole('heading', { name: 'Access Denied' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open order 42' })).not.toBeInTheDocument()
  })

  it('updates URLs and retains the actual AuthProvider session across back/forward', () => {
    session(['Administrator']); mount('/dashboard')
    fireEvent.click(screen.getByRole('link', { name: 'Suppliers', exact: true }))
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('/suppliers')
    fireEvent.click(screen.getByRole('button', { name: 'Open order 42' }))
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('/purchase-orders/42')
    expect(screen.getByRole('heading', { name: 'Purchase Orders workspace 42' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'History back' }))
    expect(screen.getByRole('heading', { name: 'Suppliers workspace' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'History forward' }))
    expect(screen.getByRole('heading', { name: 'Purchase Orders workspace 42' })).toBeInTheDocument()
    expect(screen.getByText('Test User')).toBeInTheDocument()
    expect(authApi.loadSession().token).toBe('test-session')
  })

  it.each([
    ['/purchase-orders/42', 'Purchase Orders workspace 42'],
    ['/suppliers/7', 'Suppliers workspace 7'],
    ['/quotations/3', 'Approved Requests workspace 3'],
    ['/quality-inspections/9', 'Quality inspection'],
    ['/non-conformances/2', 'Quality ncr'],
  ])('renders %s directly from a stored session', (path, heading) => {
    session(['Administrator']); mount(path)
    expect(screen.getByRole('heading', { name: heading })).toBeInTheDocument()
  })

  it('login returns to an authorized deep link', async () => {
    vi.spyOn(authApi, 'login').mockResolvedValue({ token: 'new-token', user: { fullName: 'New User', roles: ['ReceivingOfficer'] } })
    mount('/purchase-orders/42')
    fireEvent.change(screen.getByLabelText(/Email/), { target: { value: 'test@example.test' } })
    fireEvent.change(screen.getByLabelText(/Password/), { target: { value: 'password' } })
    fireEvent.click(screen.getByRole('button', { name: 'Sign in', exact: true }))
    expect(await screen.findByRole('heading', { name: 'Purchase Orders workspace 42' })).toBeInTheDocument()
  })

  it('handles unknown roles without a redirect loop', () => {
    session(['Unknown']); mount()
    expect(screen.getByRole('heading', { name: 'Access Denied' })).toBeInTheDocument()
    expect(within(screen.getByRole('navigation')).queryAllByRole('link')).toHaveLength(0)
  })

  it('sign out removes access even after history navigation', () => {
    session(['Administrator']); mount('/dashboard')
    fireEvent.click(screen.getByRole('link', { name: 'Suppliers', exact: true }))
    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }))
    fireEvent.click(screen.getByRole('button', { name: 'History back' }))
    expect(screen.getByRole('button', { name: 'Sign in', exact: true })).toBeInTheDocument()
    expect(authApi.loadSession()).toBeNull()
  })

  it('BrowserRouter updates the address bar and survives remounting on a deep URL', async () => {
    session(['Administrator'])
    window.history.replaceState(null, '', '/dashboard')
    const mounted = render(<AuthProvider><BrowserRouter><App /></BrowserRouter></AuthProvider>)
    fireEvent.click(screen.getByRole('button', { name: 'Open order 42' }))
    expect(window.location.pathname).toBe('/purchase-orders/42')
    await act(async () => { window.history.back() })
    await waitFor(() => expect(window.location.pathname).toBe('/dashboard'))
    await act(async () => { window.history.forward() })
    await waitFor(() => expect(window.location.pathname).toBe('/purchase-orders/42'))
    mounted.unmount()
    render(<AuthProvider><BrowserRouter><App /></BrowserRouter></AuthProvider>)
    expect(screen.getByRole('heading', { name: 'Purchase Orders workspace 42' })).toBeInTheDocument()
    expect(screen.getByText('Test User')).toBeInTheDocument()
    window.history.replaceState(null, '', '/')
  })
})
