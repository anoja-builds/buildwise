import { fireEvent, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import LoginPage from './LoginPage'
import { useAuth } from './AuthContext'

vi.mock('./AuthContext', () => ({ useAuth: vi.fn() }))

it('demo account selection requires the user to enter a password', () => {
  const login = vi.fn()
  useAuth.mockReturnValue({ login, loading: false, error: null })
  render(<LoginPage />)
  fireEvent.click(screen.getByRole('button', { name: /Administrator/ }))
  expect(screen.getByLabelText('Enterprise Email Address')).toHaveValue('admin@buildwise.demo')
  expect(screen.getByLabelText('Password')).toHaveValue('')
  expect(login).not.toHaveBeenCalled()
})
