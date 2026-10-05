import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { listRequests, listProjects } from '../api/materialRequestApi';
import { STATUS } from '../data/mockData';
import StatusBadge from '../components/StatusBadge';

export default function ApprovalQueuePage() {
  const [rows, setRows] = useState([]);
  const [projects, setProjects] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    listProjects().then(setProjects);
    listRequests({ status: STATUS.AWAITING_APPROVAL, page: 1, pageSize: 50 }).then(({ items }) => {
      setRows(items);
      setLoading(false);
    });
  }, []);

  const projectName = (id) => projects.find((p) => p.id === id)?.name || id;

  return (
    <div>
      <div className="page-head">
        <div>
          <h1>Approval queue</h1>
          <p>Requests awaiting a Procurement Manager decision, ranked by the planning agent's urgency flag.</p>
        </div>
      </div>

      <div className="sheet">
        <table className="reg">
          <thead>
            <tr>
              <th>Request</th>
              <th>Project</th>
              <th>Required by</th>
              <th>Items</th>
              <th>Urgency</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {rows
              .slice()
              .sort((a, b) => {
                const order = { High: 0, Medium: 1, Low: 2 };
                return (order[a.aiSummary?.urgency] ?? 3) - (order[b.aiSummary?.urgency] ?? 3);
              })
              .map((r) => (
                <tr key={r.id}>
                  <td>
                    <Link className="reg-link" to={`/requests/${r.id}`}>
                      {r.id}
                    </Link>
                    <div className="reg-id">by {r.requestedBy}</div>
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
                  <td>
                    {r.aiSummary && (
                      <span className={`flag ${r.aiSummary.urgency === 'High' ? 'flag-warn' : 'flag-info'}`}>
                        {r.aiSummary.urgency}
                      </span>
                    )}
                  </td>
                  <td>
                    <StatusBadge status={r.status} />
                  </td>
                </tr>
              ))}
            {!loading && rows.length === 0 && (
              <tr>
                <td colSpan={6}>
                  <div className="empty-state">
                    <h3>Nothing waiting on you</h3>
                    <p>Every submitted request has already received a decision.</p>
                  </div>
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
