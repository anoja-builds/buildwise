import { useState } from 'react'
import AppLayout from './layouts/AppLayout'
import DashboardBase from './pages/common/DashboardBase'
import ListBase from './pages/common/ListBase'
import FormBase from './pages/common/FormBase'
import DetailBase from './pages/common/DetailBase'
import UIStates from './pages/common/UIStates'
import './pages/common/common.css'

const screens = { Dashboard: DashboardBase, List: ListBase, Form: FormBase, Detail: DetailBase, 'UI States': UIStates }

export default function App() {
  const [screen, setScreen] = useState('Dashboard')
  const Screen = screens[screen]
  return <AppLayout breadcrumb={`Common UI / ${screen}`} activeItem={screen === 'Dashboard' ? 'Dashboard' : ''}><div className="preview-tabs" aria-label="Common screen previews">{Object.keys(screens).map((name) => <button type="button" key={name} className={`preview-tab ${screen === name ? 'preview-tab--active' : ''}`} onClick={() => setScreen(name)}>{name}</button>)}</div><Screen/></AppLayout>
}
