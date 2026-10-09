import React, { useEffect, useState } from 'react';
import { materialRequestService } from '../services/materialRequestService';
import ProcurementPlanningPanel from '../components/ProcurementPlanningPanel';
import CreateMaterialRequestModal from '../components/CreateMaterialRequestModal';

export default function MaterialRequestsPage() {
  const [requests, setRequests] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [selectedRequestIdForPlan, setSelectedRequestIdForPlan] = useState(null);

  const loadRequests = async () => {
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
  };

  useEffect(() => {
    loadRequests();
  }, [statusFilter]);

  const getBadgeClass = (status) => {
    switch (status) {
      case 'Draft': return 'badge--secondary';
      case 'Submitted': return 'badge--info';
      case 'RfqInProgress': return 'badge--warning';
      case 'Approved': return 'badge--success';
      case 'Rejected': return 'badge--danger';
      case 'Ordered': return 'badge--success';
      default: return 'badge--secondary';
    }
  };

  return (
    <div style={{ padding: '20px' }} className="fade-in">
      <div className="page-header" style={{ marginBottom: '20px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div>
          <h1 className="page-header__title">Material Request & Approval Management</h1>
          <p className="page-header__subtitle">
            Capture site material demand, enforce approval workflows, and run AI procurement planning.
          </p>
        </div>
        <button className="btn btn--primary" onClick={() => setShowCreateModal(true)}>
          + Create New Material Request
        </button>
      </div>

      {error && <div className="error-state" style={{ marginBottom: '20px' }}>⚠️ {error}</div>}

      {/* Filter and stats */}
      <div style={{ display: 'flex', gap: '16px', marginBottom: '20px', alignItems: 'center' }}>
        <label style={{ fontWeight: 'bold', fontSize: '13.5px' }}>Filter Status:</label>
        <select 
          className="form-control" 
          value={statusFilter} 
          onChange={(e) => setStatusFilter(e.target.value)}
          style={{ width: '220px', padding: '8px' }}
        >
          <option value="">All Statuses</option>
          <option value="Draft">Draft</option>
          <option value="Submitted">Submitted</option>
          <option value="RfqInProgress">RFQ In Progress</option>
          <option value="Approved">Approved</option>
          <option value="Rejected">Rejected</option>
          <option value="Ordered">Ordered</option>
        </select>
      </div>

      {selectedRequestIdForPlan && (
        <div style={{ marginBottom: '24px' }}>
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '-10px' }}>
            <button 
              className="btn btn--secondary" 
              onClick={() => setSelectedRequestIdForPlan(null)}
              style={{ padding: '2px 8px', fontSize: '11px', zIndex: 10 }}
            >
              ✕ Close AI Planning Panel
            </button>
          </div>
          <ProcurementPlanningPanel requestId={selectedRequestIdForPlan} />
        </div>
      )}

      {loading ? (
        <div style={{ textAlign: 'center', padding: '40px' }}>
          <div className="spinner"></div>
          <p>Loading material requests...</p>
        </div>
      ) : (
        <div className="card" style={{ padding: '20px' }}>
          <table className="table" style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ background: '#f8f9fa', textAlign: 'left' }}>
                <th style={{ padding: '10px' }}>ID / Ref</th>
                <th style={{ padding: '10px' }}>Project</th>
                <th style={{ padding: '10px' }}>Priority</th>
                <th style={{ padding: '10px' }}>Required Date</th>
                <th style={{ padding: '10px' }}>Status</th>
                <th style={{ padding: '10px' }}>Requested Items</th>
                <th style={{ padding: '10px' }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {requests.length === 0 ? (
                <tr>
                  <td colSpan="7" style={{ textAlign: 'center', padding: '20px', color: '#888' }}>
                    No material requests found matching criteria.
                  </td>
                </tr>
              ) : (
                requests.map((r) => (
                  <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                    <td style={{ padding: '10px', fontWeight: 'bold' }}>
                      MR-{r.id.toString().padStart(4, '0')}
                      {r.revisionNumber > 1 && <span style={{ fontSize: '11px', color: '#888', marginLeft: '4px' }}>(Rev {r.revisionNumber})</span>}
                    </td>
                    <td style={{ padding: '10px' }}>{r.projectName}</td>
                    <td style={{ padding: '10px' }}>
                      <span className={`badge ${r.priority === 'High' || r.priority === 'Urgent' ? 'badge--danger' : 'badge--secondary'}`}>
                        {r.priority}
                      </span>
                    </td>
                    <td style={{ padding: '10px' }}>
                      {new Date(r.requiredDate).toLocaleDateString()}
                    </td>
                    <td style={{ padding: '10px' }}>
                      <span className={`badge ${getBadgeClass(r.status)}`}>{r.status}</span>
                    </td>
                    <td style={{ padding: '10px', fontSize: '12.5px' }}>
                      {r.items && r.items.map((i, idx) => (
                        <div key={idx}>
                          • <strong>{i.quantity} {i.unit}</strong> {i.materialName}
                        </div>
                      ))}
                    </td>
                    <td style={{ padding: '10px' }}>
                      <button 
                        className="btn btn--secondary" 
                        style={{ padding: '4px 10px', fontSize: '12px' }}
                        onClick={() => setSelectedRequestIdForPlan(r.id)}
                      >
                        🤖 Run AI Plan
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}

      {showCreateModal && (
        <CreateMaterialRequestModal 
          onClose={() => setShowCreateModal(false)}
          onSuccess={() => {
            setShowCreateModal(false);
            loadRequests();
          }}
        />
      )}
    </div>
  );
}
