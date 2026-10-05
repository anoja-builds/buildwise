import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import ProtectedRoute from './ProtectedRoute'

describe('ProtectedRoute', () => {
  it('renders a protected page for an allowed role', () => {
    render(
      <MemoryRouter initialEntries={['/quality-inspections']}>
        <Routes>
          <Route path="/quality-inspections" element={(
            <ProtectedRoute roles={['QualityInspector']} allowedRoles={['QualityInspector']}>
              <div>Quality workspace</div>
            </ProtectedRoute>
          )} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Quality workspace')).toBeInTheDocument()
  })

  it('renders a 403 page instead of a protected page for a denied role', () => {
    render(
      <MemoryRouter initialEntries={['/quality-inspections']}>
        <Routes>
          <Route path="/quality-inspections" element={(
            <ProtectedRoute roles={['ProcurementOfficer']} allowedRoles={['QualityInspector']}>
              <div>Quality workspace</div>
            </ProtectedRoute>
          )} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('403 — Access denied')
    expect(screen.queryByText('Quality workspace')).not.toBeInTheDocument()
  })
})
