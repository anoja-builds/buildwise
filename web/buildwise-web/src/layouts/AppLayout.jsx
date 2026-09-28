import { useEffect, useRef } from 'react'
import { matchPath, NavLink, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { routeConfig, visibleRoutes } from '../routes/routeConfig'
import './AppLayout.css'

const initialsOf = (name) => name ? name.split(' ').filter(Boolean).slice(0, 2).map((p) => p[0].toUpperCase()).join('') : 'BW'
const PowerIcon = () => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M18.4 6.6a9 9 0 1 1-12.8 0" /><path d="M12 2v10" /></svg>

export default function AppLayout({ children, user, onLogout }) {
  const { roles } = useAuth()
  const location = useLocation()
  const main = useRef(null)
  const navigation = visibleRoutes(roles)
  const current = routeConfig.find((route) => matchPath(route.path, location.pathname))
  const title = current?.label || (location.pathname === '/access-denied' ? 'Access Denied' : 'Workspace')
  useEffect(() => {
    document.title = `${title} | BuildWise`
    main.current?.focus({ preventScroll: true })
    window.scrollTo?.(0, 0)
  }, [location.pathname, title])
  return <div className="app-shell">
    <a className="skip-link" href="#main-content">Skip to content</a>
    <aside className="sidebar">
      <div className="brand"><div className="brand__mark">BW</div><div><div className="brand__name">BuildWise</div><div className="brand__tagline">Project operations</div></div></div>
      <nav className="nav" aria-label="Main navigation">
        <div className="nav-label" aria-hidden="true">Workspace</div>
        {navigation.map((item) => <NavLink key={item.path} to={item.path} className={({ isActive }) => `nav-item ${isActive ? 'nav-item--active' : ''}`}>{item.label}</NavLink>)}
      </nav>
    </aside>
    <div className="app-main">
      <header className="topbar">
        <div><span className="mobile-brand">BuildWise</span><span className="breadcrumb">BuildWise / {title}</span></div>
        <div className="topbar__actions">
          <div className="user"><div className="avatar">{initialsOf(user?.fullName)}</div><div className="user__meta"><div className="user__name">{user?.fullName || 'Team member'}</div><div className="user__role">{roles.join(', ') || 'Team member'}</div></div></div>
          <button type="button" className="icon-button" aria-label="Sign out" title="Sign out" onClick={onLogout}><PowerIcon /></button>
        </div>
      </header>
      <main id="main-content" ref={main} tabIndex={-1} className="content">
        <div key={location.pathname} className="page-enter">{children}</div>
      </main>
    </div>
  </div>
}
