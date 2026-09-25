import { useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { Button, Card, TextInput } from '../components/shared'
import { useAuth } from './AuthContext'
import { defaultRouteForRoles } from './accessControl'
import './auth.css'

const DEMO_ACCOUNTS = [
  { label: 'Procurement Officer', email: 'procurement.officer@buildwise.demo' },
  { label: 'Procurement Manager', email: 'procurement.manager@buildwise.demo' },
  { label: 'Site Engineer', email: 'site.engineer@buildwise.demo' },
  { label: 'Site Officer', email: 'site.officer@buildwise.demo' },
  { label: 'Site Manager', email: 'site.manager@buildwise.demo' },
  { label: 'Quality Inspector', email: 'quality.inspector@buildwise.demo' },
  { label: 'Administrator', email: 'admin@buildwise.demo' }
]
const DEMO_PASSWORD = 'Passw0rd!'

export default function LoginPage() {
  const { login, roles, loading, error } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  const completeLogin = async (loginEmail, loginPassword) => {
    if (await login(loginEmail, loginPassword)) {
      const requestedPath = location.state?.from
      navigate(requestedPath && requestedPath !== '/login' ? requestedPath : defaultRouteForRoles(roles), { replace: true })
    }
  }

  const handleSubmit = (event) => {
    event.preventDefault()
    completeLogin(email, password)
  }

  const handleDemoLogin = (demoEmail) => {
    setEmail(demoEmail)
    setPassword(DEMO_PASSWORD)
    completeLogin(demoEmail, DEMO_PASSWORD)
  }

  return (
    <div className="auth-shell">
      <div className="auth-card-wrap">
        <div className="auth-brand">
          <div className="auth-brand__mark">BW</div>
          <div>
            <div className="auth-brand__name">BuildSupply LK</div>
            <div className="auth-brand__tagline">Construction materials procurement, delivery & quality</div>
          </div>
        </div>

        <Card title="Sign in" subtitle="Use your BuildSupply LK account to continue.">
          <form className="stack" onSubmit={handleSubmit}>
            <TextInput label="Email" name="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
            <TextInput label="Password" name="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
            {error && <span className="field__error">{error}</span>}
            <Button type="submit" disabled={loading}>{loading ? 'Signing in…' : 'Sign in'}</Button>
          </form>
        </Card>

        <Card title="Quick demo login" subtitle="Seeded accounts for evaluation — one per role.">
          <div className="auth-demo-grid">
            {DEMO_ACCOUNTS.map((account) => (
              <button key={account.email} type="button" className="auth-demo-button" onClick={() => handleDemoLogin(account.email)} disabled={loading}>
                <strong>{account.label}</strong>
                <span>{account.email}</span>
              </button>
            ))}
          </div>
          <p className="auth-demo-hint">Demo password for every seeded account: <code>{DEMO_PASSWORD}</code></p>
        </Card>
      </div>
    </div>
  )
}
