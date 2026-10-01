import { useState } from 'react'
import { Button, Card } from '../components/shared'
import { useAuth } from './AuthContext'
import hardHat from '../assets/figma/hard-hat.svg'
import pattern from '../assets/figma/login-pattern.svg'
import mail from '../assets/figma/mail.svg'
import lock from '../assets/figma/lock.svg'
import eye from '../assets/figma/eye.svg'
import alertIcon from '../assets/figma/alert.svg'
import './auth.css'
const DEMO_ACCOUNTS = [
  { label: 'Site Engineer', email: 'site.engineer@buildwise.demo' },
  { label: 'Procurement Officer', email: 'procurement.officer@buildwise.demo' },
  { label: 'Procurement Manager', email: 'procurement.manager@buildwise.demo' },
  { label: 'Quality Inspector', email: 'quality.inspector@buildwise.demo' },
  { label: 'Administrator', email: 'admin@buildwise.demo' }
]
export default function LoginPage() {
  const { login, loading, error } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [visible, setVisible] = useState(false)
  return <div className="auth-shell">
    <aside className="auth-product">
      <div className="auth-pattern" aria-hidden="true"><img src={pattern} alt="" /></div>
      <div className="auth-brand"><div className="auth-brand__mark"><img src={hardHat} alt="" /></div><strong>BuildWise</strong></div>
      <div className="auth-product__message"><h1>Construction Materials Procurement &amp; Quality Management</h1><p>Streamlining B2B vendor sourcing, request routing, field quality reports, and real-time operations inside a single secure ecosystem.</p></div>
      <small>© {new Date().getFullYear()} BuildWise Operations Hub. All rights reserved.</small>
    </aside>
    <main className="auth-main"><div className="auth-card-wrap">
      <Card title="Sign In to Your Account" subtitle="Enter your credentials to access the console">
        <form className="stack" onSubmit={event => { event.preventDefault(); login(email, password) }}>
          <label className="field"><span className="field__label">Enterprise Email Address</span><span className="auth-input"><img src={mail} alt="" /><input name="email" type="email" autoComplete="username" required value={email} onChange={e => setEmail(e.target.value)} /></span></label>
          <label className="field"><span className="field__label">Password</span><span className="auth-input"><img src={lock} alt="" /><input name="password" type={visible ? 'text' : 'password'} autoComplete="current-password" required value={password} onChange={e => setPassword(e.target.value)} /><button type="button" aria-label={visible ? 'Hide password' : 'Show password'} onClick={() => setVisible(v => !v)}><img src={eye} alt="" /></button></span></label>
          {error && <p className="field__error" role="alert">{error}</p>}
          <Button type="submit" disabled={loading}>{loading ? 'Signing in…' : 'Sign in'}</Button>
        </form>
        <div className="auth-audit-notice"><img src={alertIcon} alt="" /><span>Authorized personnel only. Access, procurement decisions, and quality actions are recorded for audit.</span></div>
      </Card>
      <details className="auth-demo"><summary>Quick demo accounts</summary><div className="auth-demo-grid">{DEMO_ACCOUNTS.map(account => <button key={account.email} type="button" className="auth-demo-button" disabled={loading} onClick={() => { setEmail(account.email); setPassword('' ) }}><strong>{account.label}</strong><span>{account.email}</span></button>)}</div></details>
    </div></main>
  </div>
}
