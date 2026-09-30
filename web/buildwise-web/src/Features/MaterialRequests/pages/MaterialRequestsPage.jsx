import React, { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../../auth/AuthContext';
import { materialRequestService } from '../services/materialRequestService';
import ProcurementPlanningPanel from '../components/ProcurementPlanningPanel';
import CreateMaterialRequestModal from '../components/CreateMaterialRequestModal';
import { Button, Card, PageHeader, StatusBadge } from '../../../components/shared';

export default function MaterialRequestsPage() {
  const { hasRole } = useAuth();
  const canApprove = hasRole('ProcurementManager') || hasRole('Administrator');
  const canCreate = hasRole('SiteEngineer') || hasRole('Administrator');
  const canPlan = canCreate || canApprove;

  const [reviewing, setReviewing] = useState(null);
  const [decision, setDecision] = useState('Approved');
  const [savingDecision, setSavingDecision] = useState(false);
  const [decisionError, setDecisionError] = useState('');
  const [notice, setNotice] = useState('');

  const [requests, setRequests] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [statusFilter, setStatusFilter] = useState('');

  const [showCreateModal, setShowCreateModal] = useState(false);
  const [selectedRequestIdForPlan, setSelectedRequestIdForPlan] = useState(null);

  const loadRequests = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const data = await materialRequestService.getRequests(statusFilter);
      setRequests(data);
    } catch (err) {
      setError(err.message || 'Failed to load material requests');
    } finally {
      setLoading(false);
    }
  }, [statusFilter]);

  useEffect(() => {
    loadRequests();
  }, [loadRequests]);

  const recordDecision = async (event) => {
    event.preventDefault();
    if (!canApprove || savingDecision) return;
    setSavingDecision(true);
    setDecisionError('');
    try {
      await materialRequestService.approveRequest(reviewing.id, { decision });
      setNotice(`Request #${reviewing.id}: ${decision}.`);
      setReviewing(null);
      await loadRequests();
    } catch (err) {
      setDecisionError(err.message || 'Failed to record approval decision');
    } finally {
      setSavingDecision(false);
    }
  };

  const getBadgeTone = (status) => {
    switch (status) {
      case 'Draft': return 'neutral';
      case 'PendingApproval': return 'warning';
      case 'RfqInProgress': return 'info';
      case 'Approved': return 'success';
      case 'Rejected': return 'danger';
      case 'Ordered': return 'success';
      default: return 'neutral';
    }
  };

  if (canCreate && showCreateModal) return <CreateMaterialRequestModal onClose={() => setShowCreateModal(false)} onSuccess={() => { setShowCreateModal(false); loadRequests(); }} />;

  return (
    <div className="stack" style={{ gap: '24px' }}>
      <PageHeader
        eyebrow="REQUISITION & APPROVALS"
        title="Material Requests"
        subtitle="Capture site material demand, enforce approval workflows, and run AI procurement planning."
        actions={
          canCreate ? (
            <Button onClick={() => setShowCreateModal(true)}>
              + Create New Material Request
            </Button>
          ) : null
        }
      />

      {error && (
        <div className="auth-error-banner" role="alert">
          {error}
        </div>
      )}

      {notice && (
        <div
          role="status"
          style={{
            padding: '10px 14px',
            background: 'var(--color-success-100)',
            color: 'var(--color-success-700)',
            borderRadius: 'var(--radius-md)',
            border: '1px solid rgb(22 163 74 / 0.2)',
            fontSize: '13px',
            fontWeight: 500
          }}
        >
          ✓ {notice}
        </div>
      )}

      {reviewing && (
        <Card className="review-card" style={{ borderLeft: '4px solid var(--color-accent-600)', marginBottom: '20px' }}>
          <form onSubmit={recordDecision} aria-label="Review material request">
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '16px' }}>
              <div>
                <span className="page-header__eyebrow">HUMAN APPROVAL REQUIRED</span>
                <h2 style={{ fontSize: '18px', fontWeight: 700, margin: '2px 0 4px' }}>
                  Review request #{reviewing.id}
                </h2>
                <p style={{ color: 'var(--color-text-muted)', fontSize: '13px', margin: 0 }}>
                  {reviewing.projectName}: {reviewing.reason}
                </p>
              </div>
              <button
                type="button"
                className="icon-button"
                onClick={() => setReviewing(null)}
                aria-label="Close review"
              >
                ✕
              </button>
            </div>

            <div style={{ display: 'flex', gap: '16px', alignItems: 'flex-end', flexWrap: 'wrap' }}>
              <label className="field" htmlFor="request-decision" style={{ minWidth: '200px' }}>
                <span className="field__label">Decision</span>
                <select
                  id="request-decision"
                  className="field__control"
                  value={decision}
                  disabled={savingDecision}
                  onChange={(e) => setDecision(e.target.value)}
                >
                  <option value="Approved">Approve</option>
                  <option value="Rejected">Reject</option>
                </select>
              </label>

              <Button
                type="submit"
                disabled={savingDecision}
              >
                {savingDecision ? 'Saving decision...' : 'Confirm decision'}
              </Button>
              <Button
                variant="secondary"
                disabled={savingDecision}
                type="button"
                onClick={() => setReviewing(null)}
              >
                Cancel
              </Button>
            </div>

            {decisionError && (
              <p role="alert" style={{ color: 'var(--color-danger-700)', fontSize: '13px', marginTop: '12px', fontWeight: 500 }}>
                {decisionError}
              </p>
            )}
          </form>
        </Card>
      )}

      {/* Filter and stats toolbar */}
      <div className="toolbar">
        <div className="toolbar__filters">
          <label className="field">
            <span className="field__label">Filter Status:</span>
            <select
              className="field__control"
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="">All Statuses</option>
              <option value="Draft">Draft</option>
              <option value="PendingApproval">Pending Approval</option>
              <option value="RfqInProgress">RFQ In Progress</option>
              <option value="Approved">Approved</option>
              <option value="Rejected">Rejected</option>
              <option value="Ordered">Ordered</option>
            </select>
          </label>
        </div>
      </div>

      {canPlan && selectedRequestIdForPlan && (
        <div>
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '8px' }}>
            <Button
              variant="secondary"
              onClick={() => setSelectedRequestIdForPlan(null)}
              style={{ fontSize: '12px', padding: '4px 10px' }}
            >
              ✕ Close AI Planning Panel
            </Button>
          </div>
          <ProcurementPlanningPanel requestId={selectedRequestIdForPlan} />
        </div>
      )}

      {loading ? (
        <div className="state">
          <div className="state__content">
            <div className="spinner" />
            <p>Loading material requests…</p>
          </div>
        </div>
      ) : (
        <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
          <div className="table-wrap" style={{ border: 0, borderRadius: 0 }}>
            <table className="data-table">
              <thead>
                <tr>
                  <th>ID / Ref</th>
                  <th>Project</th>
                  <th>Priority</th>
                  <th>Required Date</th>
                  <th>Status</th>
                  <th>Requested Items</th>
                  <th style={{ textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {requests.length === 0 ? (
                  <tr>
                    <td colSpan="7" style={{ textAlign: 'center', padding: '32px', color: 'var(--color-text-muted)' }}>
                      No material requests found matching criteria.
                    </td>
                  </tr>
                ) : (
                  requests.map((r) => (
                    <tr key={r.id}>
                      <td style={{ fontWeight: 600 }}>
                        MR-{r.id.toString().padStart(4, '0')}
                        {r.revisionNumber > 1 && (
                          <span style={{ fontSize: '11px', color: 'var(--color-text-muted)', marginLeft: '4px' }}>
                            (Rev {r.revisionNumber})
                          </span>
                        )}
                      </td>
                      <td>{r.projectName}</td>
                      <td>
                        <StatusBadge
                          status={r.priority}
                          tone={r.priority === 'High' || r.priority === 'Urgent' ? 'danger' : 'neutral'}
                        >
                          {r.priority ?? 'Not recorded'}
                        </StatusBadge>
                      </td>
                      <td>
                        {new Date(r.requiredDate).toLocaleDateString()}
                      </td>
                      <td>
                        <StatusBadge
                          status={r.status}
                          tone={getBadgeTone(r.status)}
                        >
                          {r.status}
                        </StatusBadge>
                      </td>
                      <td style={{ fontSize: '13px' }}>
                        {r.items && r.items.map((i, idx) => (
                          <div key={idx}>
                            • <strong>{i.quantity} {i.materialUnit ?? i.unit}</strong> {i.materialName}
                          </div>
                        ))}
                      </td>
                      <td style={{ textAlign: 'right' }}>
                        <div style={{ display: 'inline-flex', gap: '8px', justifyContent: 'flex-end' }}>
                          {canApprove && r.status === 'PendingApproval' && (
                            <Button
                              size="sm"
                              disabled={savingDecision}
                              onClick={() => {
                                setReviewing(r);
                                setDecision('Approved');
                                setDecisionError('');
                                setNotice('');
                              }}
                            >
                              Review request #{r.id}
                            </Button>
                          )}
                          {canPlan && (
                            <Button
                              variant="secondary"
                              size="sm"
                              onClick={() => setSelectedRequestIdForPlan(r.id)}
                            >
                              🤖 Run AI Plan
                            </Button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

    </div>
  );
}
