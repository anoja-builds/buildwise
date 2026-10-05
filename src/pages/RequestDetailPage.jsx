import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { getRequest, listProjects, decideRequest, deleteRequest } from '../api/materialRequestApi';
import { STATUS } from '../data/mockData';
import StatusBadge from '../components/StatusBadge';
import AgentPanel from '../components/AgentPanel';
import Toast from '../components/Toast';
import { useAuth } from '../context/AuthContext';

export default function RequestDetailPage() {
  const { id } = useParams();
  const { currentUser, isProcurementManager } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const [request, setRequest] = useState(null);
  const [projects, setProjects] = useState([]);
  const [loading, setLoading] = useState(true);
  const [comment, setComment] = useState('');
  const [deciding, setDeciding] = useState(false);
  const [toast, setToast] = useState(location.state?.justSubmitted ? 'Request submitted for approval.' : '');

  useEffect(() => {
    listProjects().then(setProjects);
  }, []);

  useEffect(() => {
    setLoading(true);
    getRequest(id).then((r) => {
      setRequest(r);
      setLoading(false);
    });
  }, [id]);

  async function handleDecision(decision) {
    if (decision !== 'Approved' && !comment.trim()) {
      alert('Please add a short comment explaining the rejection or requested revision.');
      return;
    }
    setDeciding(true);
    try {
      const updated = await decideRequest(id, { decision, comment: comment || 'Approved — proceed to procurement.', decidedBy: currentUser.name });
      setRequest(updated);
      setComment('');
      setToast(
        decision === 'Approved'
          ? 'Request approved and released to procurement.'
          : decision === 'Rejected'
          ? 'Request rejected.'
          : 'Revision requested — sent back to the site engineer.'
      );
    } catch (err) {
      alert(err.message);
    } finally {
      setDeciding(false);
    }
  }

  if (loading) return <p className="muted">Loading request…</p>;
  if (!request) return <p className="muted">Request not found.</p>;

  const projectName = projects.find((p) => p.id === request.projectId)?.name || request.projectId;
  const canEdit = [STATUS.DRAFT, STATUS.REVISION_REQUESTED].includes(request.status) && request.requestedBy === currentUser.name;
  const canDelete = request.status === STATUS.DRAFT && request.requestedBy === currentUser.name;
  const canDecide = isProcurementManager && request.status === STATUS.AWAITING_APPROVAL;

  async function handleDelete() {
    if (!window.confirm(`Delete draft request ${request.id}? This cannot be undone.`)) return;
    try {
      await deleteRequest(request.id, { requestedBy: currentUser.name });
      navigate('/requests', { state: { justSubmitted: false } });
    } catch (err) {
      alert(err.message);
    }
  }

  return (
    <div>
      <div className="page-head">
        <div>
          <h1>
            {request.id} <span style={{ marginLeft: 10 }}><StatusBadge status={request.status} /></span>
          </h1>
          <p>{projectName}</p>
        </div>
        <div className="row">
          <Link to="/requests" className="btn btn-outline">
            ← Back to list
          </Link>
          {canEdit && (
            <Link to={`/requests/${request.id}/edit`} className="btn btn-primary">
              Edit &amp; resubmit
            </Link>
          )}
          {canDelete && (
            <button className="btn btn-danger" onClick={handleDelete}>
              Delete draft
            </button>
          )}
        </div>
      </div>

      <div className="detail-grid">
        <div>
          <div className="panel">
            <h3>Request details</h3>
            <dl className="kv-grid">
              <div className="kv">
                <dt>Requested by</dt>
                <dd>{request.requestedBy}</dd>
              </div>
              <div className="kv">
                <dt>Required by</dt>
                <dd className="mono">{request.requiredDate}</dd>
              </div>
              <div className="kv">
                <dt>Submitted on</dt>
                <dd className="mono">{request.createdAt}</dd>
              </div>
              <div className="kv">
                <dt>Project</dt>
                <dd>{projectName}</dd>
              </div>
              <div className="kv" style={{ gridColumn: '1 / -1' }}>
                <dt>Reason</dt>
                <dd style={{ fontWeight: 400 }}>{request.reason}</dd>
              </div>
            </dl>
          </div>

          <div className="panel">
            <h3>Requested items</h3>
            <table className="reg">
              <thead>
                <tr>
                  <th>Material</th>
                  <th>Quantity</th>
                </tr>
              </thead>
              <tbody>
                {request.items.map((item) => (
                  <tr key={item.id}>
                    <td>{item.materialName}</td>
                    <td className="qty">
                      {item.quantity} {item.unit}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="panel">
            <h3>Approval history</h3>
            {request.approvals.filter((a) => a.decision).length === 0 ? (
              <p className="muted" style={{ fontSize: 13.5 }}>No decision has been recorded yet.</p>
            ) : (
              <div className="timeline">
                {request.approvals
                  .filter((a) => a.decision)
                  .map((a) => (
                    <div
                      key={a.id}
                      className={`timeline__item ${
                        a.decision === 'Approved' ? 'is-approved' : a.decision === 'Rejected' ? 'is-rejected' : 'is-revision'
                      }`}
                    >
                      <div className="timeline__head">
                        <span>{a.decision.replace(/([A-Z])/g, ' $1').trim()}</span>
                        <span className="mono" style={{ fontWeight: 400, fontSize: 12 }}>
                          {a.date}
                        </span>
                      </div>
                      <div className="timeline__meta">{a.decidedBy} · {a.stage}</div>
                      {a.comment && <div className="timeline__body">{a.comment}</div>}
                    </div>
                  ))}
              </div>
            )}
          </div>

          {canDecide && (
            <div className="panel decision-panel">
              <h3>Procurement Manager decision</h3>
              <div className="field">
                <label htmlFor="comment">Comment</label>
                <textarea
                  id="comment"
                  placeholder="Required for reject / request revision — optional for approve."
                  value={comment}
                  onChange={(e) => setComment(e.target.value)}
                />
              </div>
              <div className="decision-actions">
                <button className="btn btn-success" disabled={deciding} onClick={() => handleDecision('Approved')}>
                  Approve
                </button>
                <button className="btn btn-outline" disabled={deciding} onClick={() => handleDecision('RevisionRequested')}>
                  Request revision
                </button>
                <button className="btn btn-danger" disabled={deciding} onClick={() => handleDecision('Rejected')}>
                  Reject
                </button>
              </div>
            </div>
          )}
        </div>

        <div>
          <AgentPanel summary={request.aiSummary} />
        </div>
      </div>

      <Toast message={toast} onDismiss={() => setToast('')} />
    </div>
  );
}
