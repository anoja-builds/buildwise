import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { listRequests, listProjects, deleteRequest } from '../api/materialRequestApi';
import { STATUS } from '../data/mockData';
import StatusBadge from '../components/StatusBadge';
import Pagination from '../components/Pagination';
import Toast from '../components/Toast';
import { useAuth } from '../context/AuthContext';

const PAGE_SIZE = 6;

export default function RequestListPage() {
  const { currentUser, isSiteEngineer } = useAuth();
  const [projects, setProjects] = useState([]);
  const [rows, setRows] = useState([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [toast, setToast] = useState('');
  const [refreshKey, setRefreshKey] = useState(0);

  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [projectId, setProjectId] = useState('');
  const [mineOnly, setMineOnly] = useState(false);

  useEffect(() => {
    listProjects().then(setProjects);
  }, []);

  useEffect(() => {
    setLoading(true);
    listRequests({
      search,
      status: status || undefined,
      projectId: projectId || undefined,
      requestedBy: mineOnly ? currentUser.name : undefined,
      page,
      pageSize: PAGE_SIZE,
    }).then(({ items, total: t }) => {
      setRows(items);
      setTotal(t);
      setLoading(false);
    });
  }, [search, status, projectId, mineOnly, page, currentUser.name, refreshKey]);

  useEffect(() => setPage(1), [search, status, projectId, mineOnly]);

  const projectName = (id) => projects.find((p) => p.id === id)?.name || id;

  async function handleDelete(id) {
    if (!window.confirm(`Delete draft request ${id}? This cannot be undone.`)) return;
    try {
      await deleteRequest(id, { requestedBy: currentUser.name });
      setToast(`${id} deleted.`);
      setRefreshKey((k) => k + 1);
    } catch (err) {
      alert(err.message);
    }
  }

  return (
    <div>
      <div className="page-head">
        <div>
          <h1>Material Requests</h1>
          <p>Every request raised from site, its current status, and where it stands in the approval workflow.</p>
        </div>
        {isSiteEngineer && (
          <Link to="/requests/new" className="btn btn-primary">
            + New material request
          </Link>
        )}
      </div>

      <div className="filter-bar">
        <input
          type="search"
          placeholder="Search by ID, material or reason…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <select value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">All statuses</option>
          {Object.values(STATUS).map((s) => (
            <option key={s} value={s}>
              {s.replace(/([A-Z])/g, ' $1').trim()}
            </option>
          ))}
        </select>
        <select value={projectId} onChange={(e) => setProjectId(e.target.value)}>
          <option value="">All projects</option>
          {projects.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
        <div className="filter-bar__spacer" />
        {isSiteEngineer && (
          <label className="row" style={{ fontSize: 13 }}>
            <input type="checkbox" checked={mineOnly} onChange={(e) => setMineOnly(e.target.checked)} />
            My requests only
          </label>
        )}
      </div>

      <div className="sheet">
        <table className="reg">
          <thead>
            <tr>
              <th>Request</th>
              <th>Project</th>
              <th>Required by</th>
              <th>Items</th>
              <th>Requested by</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => {
              const isOwnDraft = r.status === STATUS.DRAFT && r.requestedBy === currentUser.name;
              return (
                <tr key={r.id}>
                  <td>
                    <Link className="reg-link" to={`/requests/${r.id}`}>
                      {r.id}
                    </Link>
                    <div className="reg-id">{r.createdAt}</div>
                  </td>
                  <td>{projectName(r.projectId)}</td>
                  <td className="mono">{r.requiredDate}</td>
                  <td>
                    {r.items.map((i) => (
                      <div key={i.id} className="qty">
                        {i.quantity} {i.unit} · {i.materialName}
                      </div>
                    ))}
                  </td>
                  <td>{r.requestedBy}</td>
                  <td>
                    <StatusBadge status={r.status} />
                  </td>
                  <td>
                    <div className="row">
                      {[STATUS.DRAFT, STATUS.REVISION_REQUESTED].includes(r.status) && r.requestedBy === currentUser.name && (
                        <Link to={`/requests/${r.id}/edit`} className="btn btn-outline btn-sm">
                          Edit
                        </Link>
                      )}
                      {isOwnDraft && (
                        <button className="btn btn-danger btn-sm" onClick={() => handleDelete(r.id)}>
                          Delete
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              );
            })}
            {!loading && rows.length === 0 && (
              <tr>
                <td colSpan={7}>
                  <div className="empty-state">
                    <h3>No requests match these filters</h3>
                    <p>Try clearing the search or status filter.</p>
                  </div>
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
      <Pagination page={page} pageSize={PAGE_SIZE} total={total} onPageChange={setPage} />
      <Toast message={toast} onDismiss={() => setToast('')} />
    </div>
  );
}
