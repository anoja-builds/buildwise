import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

const NAV = [
  { to: '/requests', num: '01', label: 'Material Requests' },
  { to: '/requests/new', num: '02', label: 'New Request' },
  { to: '/approvals', num: '03', label: 'Approval Queue' },
];

const PAGE_META = {
  '/requests': { title: 'Material Requests', sub: ' Material Request & Approval Management' },
  '/requests/new': { title: 'New Material Request', sub: 'Component 1 · Material Request & Approval Management' },
  '/approvals': { title: 'Approval Queue', sub: 'Component 1 · Material Request & Approval Management' },
};

export default function AppShell() {
  const { currentUser, users, setCurrentUserId } = useAuth();
  const location = useLocation();

  const meta =
    PAGE_META[location.pathname] ||
    (location.pathname.startsWith('/requests/') ? { title: 'Request Detail', sub: 'Component 1 · Material Request & Approval Management' } : PAGE_META['/requests']);

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar__brand">
          <div className="sidebar__brand-mark">BUILDWISE</div>
          <div className="sidebar__brand-name">BuildWise</div>
          <div className="sidebar__brand-sub">Material Requests</div>
        </div>
        <nav>
          {NAV.filter((item) => item.to !== '/approvals' || currentUser.role === 'ProcurementManager').map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) => `sidebar__link${isActive ? ' active' : ''}`}
              end={item.to === '/requests'}
            >
              <span className="sidebar__link-num">{item.num}</span>
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="sidebar__foot">
          Material Request & Approval Management
        </div>
      </aside>

      <div>
        <header className="topbar">
          <div className="topbar__title">
            {meta.title}
            <small>{meta.sub}</small>
          </div>
          <div className="role-switch">
            <span className="role-pill">{currentUser.role === 'ProcurementManager' ? 'Procurement Manager' : 'Site Engineer'}</span>
            <select value={currentUser.id} onChange={(e) => setCurrentUserId(e.target.value)}>
              {users.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.name} — {u.role === 'ProcurementManager' ? 'Procurement Manager' : 'Site Engineer'}
                </option>
              ))}
            </select>
          </div>
        </header>
        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
