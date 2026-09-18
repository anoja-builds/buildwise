import { useState } from 'react'
import AppLayout from './layouts/AppLayout'
import DashboardBase from './pages/common/DashboardBase'
import ListBase from './pages/common/ListBase'
import FormBase from './pages/common/FormBase'
import DetailBase from './pages/common/DetailBase'
import UIStates from './pages/common/UIStates'
import ProcurementApp from './Features/procurement/pages/ProcurementApp'
import LoginPage from './auth/LoginPage'
import { useAuth } from './auth/AuthContext'
import './pages/common/common.css'

const screens = { Dashboard: DashboardBase, List: ListBase, Form: FormBase, Detail: DetailBase, 'UI States': UIStates, Procurement: ProcurementApp }

export default function App() {
  const { isAuthenticated, user, logout } = useAuth()
  const [screen, setScreen] = useState('Procurement')

  if (!isAuthenticated) return <LoginPage />

  const Screen = screens[screen]
  return <AppLayout breadcrumb={`BuildWise / ${screen}`} activeItem={screen === 'Procurement' ? 'Procurement' : screen === 'Dashboard' ? 'Dashboard' : ''} user={user} onLogout={logout}><div className="preview-tabs" aria-label="Screen previews">{Object.keys(screens).map((name) => <button type="button" key={name} className={`preview-tab ${screen === name ? 'preview-tab--active' : ''}`} onClick={() => setScreen(name)}>{name}</button>)}</div><Screen/></AppLayout>
}
