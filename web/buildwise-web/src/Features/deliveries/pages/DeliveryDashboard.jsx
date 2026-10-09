import React, { useEffect, useState } from 'react';
import { deliveryService } from '../services/deliveryService';
import DeliveryRiskPanel from '../components/DeliveryRiskPanel';
import RecordDeliveryForm from './RecordDeliveryForm';

export default function DeliveryDashboard() {
  const [expected, setExpected] = useState([]);
  const [history, setHistory] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  
  // Selection for recording delivery
  const [activeDelivery, setActiveDelivery] = useState(null);
  // Selection for showing AI risk assessment on a specific Purchase Order
  const [activePoForRisk, setActivePoForRisk] = useState(null);

  const loadData = async () => {
    setLoading(true);
    setError('');
    try {
      const [expectedList, historyList] = await Promise.all([
        deliveryService.getExpectedDeliveries(),
        deliveryService.getDeliveryHistory()
      ]);
      setExpected(expectedList);
      setHistory(historyList);
    } catch (err) {
      setError(err.message || 'Failed to fetch deliveries.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, []);

  const getStatusBadgeClass = (status) => {
    switch (status) {
      case 'Scheduled': return 'badge--secondary'; // Gray/blue
      case 'InTransit': return 'badge--info'; // Teal/blue
      case 'Received': return 'badge--success'; // Green
      case 'PartiallyReceived': return 'badge--warning'; // Yellow
      case 'DiscrepancyReported': return 'badge--danger'; // Red
      default: return 'badge--secondary';
    }
  };

  if (loading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', padding: '50px' }}>
        <div className="spinner"></div>
        <span style={{ marginLeft: '12px' }}>Loading deliveries data...</span>
      </div>
    );
  }

  return (
    <div style={{ padding: '20px' }} className="fade-in">
      <div className="page-header" style={{ marginBottom: '20px' }}>
        <div className="page-header__title-section">
          <h1 className="page-header__title">Delivery & Material Receiving</h1>
          <p className="page-header__subtitle">
            Verify deliveries, reconcile quantities, and evaluate supplier risks (Component Ownership: Ramya)
          </p>
        </div>
      </div>

      {error && <div className="error-state" style={{ marginBottom: '20px' }}>⚠️ {error}</div>}

      {/* Grid of stats */}
      <div className="dashboard-stats" style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '16px', marginBottom: '24px' }}>
        <div className="card stats-card" style={{ padding: '16px', textAlign: 'center', background: '#eaf2f8' }}>
          <h4 style={{ margin: '0 0 8px 0', color: '#2980b9' }}>Expected Shipments</h4>
          <span style={{ fontSize: '28px', fontWeight: 'bold', color: '#1f618d' }}>{expected.length}</span>
        </div>
        <div className="card stats-card" style={{ padding: '16px', textAlign: 'center', background: '#eafaf1' }}>
          <h4 style={{ margin: '0 0 8px 0', color: '#27ae60' }}>Successful Deliveries</h4>
          <span style={{ fontSize: '28px', fontWeight: 'bold', color: '#1e8449' }}>
            {history.filter(h => h.status === 'Received').length}
          </span>
        </div>
        <div className="card stats-card" style={{ padding: '16px', textAlign: 'center', background: '#fef9e7' }}>
          <h4 style={{ margin: '0 0 8px 0', color: '#f39c12' }}>Partial Shipments</h4>
          <span style={{ fontSize: '28px', fontWeight: 'bold', color: '#b7950b' }}>
            {history.filter(h => h.status === 'PartiallyReceived').length}
          </span>
        </div>
        <div className="card stats-card" style={{ padding: '16px', textAlign: 'center', background: '#fdedd0' }}>
          <h4 style={{ margin: '0 0 8px 0', color: '#e74c3c' }}>Discrepancies / Damage</h4>
          <span style={{ fontSize: '28px', fontWeight: 'bold', color: '#922b21' }}>
            {history.filter(h => h.status === 'DiscrepancyReported').length}
          </span>
        </div>
      </div>

      {/* active record form */}
      {activeDelivery && (
        <div style={{ marginBottom: '30px' }}>
          <RecordDeliveryForm
            delivery={activeDelivery}
            onCancel={() => setActiveDelivery(null)}
            onSuccess={() => {
              setActiveDelivery(null);
              loadData();
            }}
          />
        </div>
      )}

      {/* active AI assessment panel */}
      {activePoForRisk && (
        <div style={{ marginBottom: '30px' }}>
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '-10px' }}>
            <button 
              className="btn btn--secondary" 
              onClick={() => setActivePoForRisk(null)}
              style={{ padding: '2px 8px', fontSize: '11px', zIndex: 10 }}
            >
              ✕ Close AI Panel
            </button>
          </div>
          <DeliveryRiskPanel purchaseOrderId={activePoForRisk} />
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: '1.2fr 0.8fr', gap: '20px' }}>
        {/* Left Side: Expected and History Lists */}
        <div>
          {/* Expected Deliveries */}
          <div className="card" style={{ padding: '20px', marginBottom: '24px' }}>
            <h3 style={{ marginTop: 0, marginBottom: '15px' }}>Scheduled & Incoming Deliveries</h3>
            {expected.length === 0 ? (
              <p className="text-muted">No scheduled deliveries pending receiving actions.</p>
            ) : (
              <table className="table" style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead>
                  <tr style={{ background: '#f8f9fa', textAlign: 'left' }}>
                    <th style={{ padding: '10px' }}>Reference</th>
                    <th style={{ padding: '10px' }}>Supplier</th>
                    <th style={{ padding: '10px' }}>Project</th>
                    <th style={{ padding: '10px' }}>Status</th>
                    <th style={{ padding: '10px' }}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {expected.map((del) => (
                    <tr key={del.id} style={{ borderBottom: '1px solid #eee' }}>
                      <td style={{ padding: '10px', fontWeight: 'bold' }}>{del.deliveryReference}</td>
                      <td style={{ padding: '10px' }}>{del.supplierName}</td>
                      <td style={{ padding: '10px' }}>{del.projectName}</td>
                      <td style={{ padding: '10px' }}>
                        <span className={`badge ${getStatusBadgeClass(del.status)}`}>{del.status}</span>
                      </td>
                      <td style={{ padding: '10px', display: 'flex', gap: '8px' }}>
                        <button 
                          className="btn btn--primary" 
                          style={{ padding: '6px 12px', fontSize: '12px' }}
                          onClick={() => setActiveDelivery(del)}
                        >
                          Receive
                        </button>
                        <button 
                          className="btn btn--secondary" 
                          style={{ padding: '6px 12px', fontSize: '12px' }}
                          onClick={() => setActivePoForRisk(del.purchaseOrderId)}
                        >
                          AI Risk Check
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          {/* Historical Deliveries */}
          <div className="card" style={{ padding: '20px' }}>
            <h3 style={{ marginTop: 0, marginBottom: '15px' }}>Delivery History & Audit Log</h3>
            {history.length === 0 ? (
              <p className="text-muted">No historical shipments found.</p>
            ) : (
              <table className="table" style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead>
                  <tr style={{ background: '#f8f9fa', textAlign: 'left' }}>
                    <th style={{ padding: '10px' }}>Ref / Date</th>
                    <th style={{ padding: '10px' }}>Supplier & PO</th>
                    <th style={{ padding: '10px' }}>Reconciled By</th>
                    <th style={{ padding: '10px' }}>Status</th>
                    <th style={{ padding: '10px' }}>Details / Remarks</th>
                  </tr>
                </thead>
                <tbody>
                  {history.map((del) => (
                    <tr key={del.id} style={{ borderBottom: '1px solid #eee' }}>
                      <td style={{ padding: '10px' }}>
                        <div style={{ fontWeight: 'bold' }}>{del.deliveryReference}</div>
                        <div style={{ fontSize: '11px', color: '#888' }}>
                          {del.deliveryDate ? new Date(del.deliveryDate).toLocaleDateString() : 'N/A'}
                        </div>
                      </td>
                      <td style={{ padding: '10px' }}>
                        <div>{del.supplierName}</div>
                        <div style={{ fontSize: '11px', color: '#888' }}>PO #{del.purchaseOrderId}</div>
                      </td>
                      <td style={{ padding: '10px' }}>{del.receivedBy || 'System Seeded'}</td>
                      <td style={{ padding: '10px' }}>
                        <span className={`badge ${getStatusBadgeClass(del.status)}`}>{del.status}</span>
                      </td>
                      <td style={{ padding: '10px', fontSize: '12px', color: '#555' }}>
                        <div>{del.notes || 'No remarks recorded.'}</div>
                        {del.photographicEvidenceUrl && (
                          <div style={{ marginTop: '4px', fontSize: '11px' }}>
                            📸 <a href={del.photographicEvidenceUrl} target="_blank" rel="noreferrer" style={{ color: '#2980b9' }}>
                              Photo Evidence Attached
                            </a>
                          </div>
                        )}
                        {/* Render items breakdown */}
                        <div style={{ marginTop: '6px', fontSize: '10.5px', background: '#f9f9f9', padding: '4px 8px', borderRadius: '4px' }}>
                          {del.items.map((i, idx) => (
                            <div key={idx}>
                              • {i.materialName}: Received {i.receivedQuantity} / Ordered {i.orderedQuantity} 
                              {i.damagedQuantity > 0 && <span style={{ color: '#c0392b' }}> (Damaged: {i.damagedQuantity})</span>}
                            </div>
                          ))}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>

        {/* Right Side: Informative and Instructions Panel */}
        <div>
          <div className="card" style={{ padding: '20px', background: '#fdfefe', border: '1px solid #e2e8f0' }}>
            <h3 style={{ marginTop: 0, borderBottom: '2px solid #ddd', paddingBottom: '8px' }}>Site Delivery Rules</h3>
            <ul style={{ paddingLeft: '20px', fontSize: '13.5px', lineHeight: '1.6' }}>
              <li style={{ marginBottom: '10px' }}>
                <strong>Reconciliation Rule</strong>: Always reconcile quantities upon delivery. Shortages and damaged materials are automatically logged to alert the procurement department.
              </li>
              <li style={{ marginBottom: '10px' }}>
                <strong>Photographic Evidence</strong>: If materials arrive damaged, use the 📸 camera simulation to attach pictures. This is required for audit trails and returns.
              </li>
              <li style={{ marginBottom: '10px' }}>
                <strong>Inspection Boundary</strong>: Receiving materials logs their physical quantities. They must subsequently pass Quality Inspection (Component 4) before being released for site construction.
              </li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}
