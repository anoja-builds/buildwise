import { createContext, useContext, useEffect, useMemo, useState } from 'react'
import { authApi } from '../services/authApi'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [session, setSession] = useState(() => authApi.loadSession())
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    const handleUnauthorized = () => setSession(null)
    window.addEventListener('buildwise:unauthorized', handleUnauthorized)
    return () => window.removeEventListener('buildwise:unauthorized', handleUnauthorized)
  }, [])

  const login = async (email, password) => {
    setLoading(true)
    setError('')
    try {
      const response = await authApi.login(email, password)
      const nextSession = { token: response.token, expiresAtUtc: response.expiresAtUtc, user: response.user }
      authApi.saveSession(nextSession)
      setSession(nextSession)
      return true
    } catch (err) {
      setError(err.message)
      return false
    } finally {
      setLoading(false)
    }
  }

  const register = async (fullName, email, password, roleName) => {
    setLoading(true)
    setError('')
    try {
      const response = await authApi.register(fullName, email, password, roleName)
      const nextSession = { token: response.token, expiresAtUtc: response.expiresAtUtc, user: response.user }
      authApi.saveSession(nextSession)
      setSession(nextSession)
      return true
    } catch (err) {
      setError(err.message)
      return false
    } finally {
      setLoading(false)
    }
  }

  const logout = () => {
    authApi.clearSession()
    setSession(null)
  }

  const value = useMemo(() => ({
    token: session?.token ?? null,
    user: session?.user ?? null,
    roles: session?.user?.roles ?? [],
    isAuthenticated: Boolean(session?.token),
    hasRole: (role) => (session?.user?.roles ?? []).includes(role),
    login,
    register,
    logout,
    loading,
    error
  }), [session, loading, error])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider')
  return ctx
}
