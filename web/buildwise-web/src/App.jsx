import { useState } from 'react'
import AppLayout from './layouts/AppLayout'
import DashboardBase from './pages/common/DashboardBase'
import ListBase from './pages/common/ListBase'
import FormBase from './pages/common/FormBase'
import DetailBase from './pages/common/DetailBase'
import UIStates from './pages/common/UIStates'
import DeliveryDashboard from './Features/deliveries/pages/DeliveryDashboard'
import './pages/common/common.css'

const screens = { Dashboard: DashboardBase, Deliveries: DeliveryDashboard, List: ListBase, Form: FormBase, Detail: DetailBase, 'UI States': UIStates }

export default function App() {
  const [screen, setScreen] = useState('Dashboard')
  const Screen = screens[screen]
  
  const handleNavigate = (item) => {
    if (screens[item]) {
      setScreen(item);
    } else if (item === 'Dashboard') {
      setScreen('Dashboard');
    }
  };

  return <AppLayout breadcrumb={`Common UI / ${screen}`} activeItem={screen} onNavigate={handleNavigate}><div className="preview-tabs" aria-label="Common screen previews">{Object.keys(screens).map((name) => <button type="button" key={name} className={`preview-tab ${screen === name ? 'preview-tab--active' : ''}`} onClick={() => setScreen(name)}>{name}</button>)}</div><Screen/></AppLayout>
}
