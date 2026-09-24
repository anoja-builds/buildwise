import React, { useState } from 'react';
import { deliveryService } from '../services/deliveryService';

export default function RecordDeliveryForm({ delivery, onCancel, onSuccess }) {
  const [notes, setNotes] = useState('');
  const [evidenceUrl, setEvidenceUrl] = useState('');
  const [itemsData, setItemsData] = useState(
    delivery.items.map(item => ({
      purchaseOrderItemId: item.purchaseOrderItemId,
      materialName: item.materialName,
      materialUnit: item.materialUnit,
      orderedQuantity: item.orderedQuantity,
      receivedQuantity: item.orderedQuantity, // Default to full delivery
      damagedQuantity: 0,
      notes: ''
    }))
  );
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const handleItemChange = (idx, field, val) => {
    const updated = [...itemsData];
    updated[idx][field] = val;
    setItemsData(updated);
  };

  const handleSimulatePhoto = () => {
    // Simulate camera snapshot upload url
    const rand = Math.floor(Math.random() * 1000);
    setEvidenceUrl(`http://localhost:5078/uploads/evidence_${delivery.id}_${rand}.jpg`);
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setLoading(true);
    setError('');
    try {
      // Reconcile received quantities
      const payload = {
        receivedByUserId: 2, // Simulate Ramya Fernando (Receiving Officer)
        notes: notes,
        items: itemsData.map(i => ({
          purchaseOrderItemId: i.purchaseOrderItemId,
          receivedQuantity: parseFloat(i.receivedQuantity) || 0,
          damagedQuantity: parseFloat(i.damagedQuantity) || 0,
          notes: i.notes
        }))
      };

      await deliveryService.receiveDelivery(delivery.Id || delivery.id, payload);

      // Upload evidence if simulated
      if (evidenceUrl) {
        await deliveryService.uploadEvidence(delivery.Id || delivery.id, evidenceUrl);
      }

      onSuccess();
    } catch (err) {
      setError(err.message || 'Failed to record delivery.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="card" style={{ padding: '20px', animation: 'slideUp 0.3s ease-out' }}>
      <h3 style={{ marginTop: 0, marginBottom: '8px' }}>
        Record Material Delivery: {delivery.deliveryReference}
      </h3>
      <p style={{ margin: 0, fontSize: '13.5px', color: '#666', marginBottom: '20px' }}>
        Compare physically arrived items against Purchase Order #{delivery.purchaseOrderId} to automatically report shortages/damages.
      </p>

      {error && <div className="error-message" style={{ color: '#dc3545', marginBottom: '15px' }}>⚠️ {error}</div>}

      <form onSubmit={handleSubmit}>
        <table className="table" style={{ width: '100%', marginBottom: '20px', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: '#f1f1f1', textAlign: 'left' }}>
              <th style={{ padding: '10px' }}>Material</th>
              <th style={{ padding: '10px' }}>Ordered</th>
              <th style={{ padding: '10px' }}>Received</th>
              <th style={{ padding: '10px' }}>Damaged</th>
              <th style={{ padding: '10px' }}>Shortage</th>
              <th style={{ padding: '10px' }}>To Inspection</th>
              <th style={{ padding: '10px' }}>Notes</th>
            </tr>
          </thead>
          <tbody>
            {itemsData.map((item, idx) => {
              const shortage = item.orderedQuantity - item.receivedQuantity;
              const inspectionQty = item.receivedQuantity - item.damagedQuantity;

              return (
                <tr key={idx} style={{ borderBottom: '1px solid #ddd' }}>
                  <td style={{ padding: '10px', fontWeight: 'bold' }}>
                    {item.materialName} ({item.materialUnit})
                  </td>
                  <td style={{ padding: '10px' }}>{item.orderedQuantity}</td>
                  <td style={{ padding: '5px' }}>
                    <input
                      type="number"
                      step="any"
                      min="0"
                      value={item.receivedQuantity}
                      onChange={(e) => handleItemChange(idx, 'receivedQuantity', e.target.value)}
                      style={{ width: '80px', padding: '6px' }}
                      required
                    />
                  </td>
                  <td style={{ padding: '5px' }}>
                    <input
                      type="number"
                      step="any"
                      min="0"
                      value={item.damagedQuantity}
                      onChange={(e) => handleItemChange(idx, 'damagedQuantity', e.target.value)}
                      style={{ width: '80px', padding: '6px' }}
                      required
                    />
                  </td>
                  <td style={{ padding: '10px', color: shortage > 0 ? '#d35400' : '#27ae60', fontWeight: 'bold' }}>
                    {shortage > 0 ? shortage : 0}
                  </td>
                  <td style={{ padding: '10px', color: '#2980b9', fontWeight: 'bold' }}>
                    {inspectionQty > 0 ? inspectionQty : 0}
                  </td>
                  <td style={{ padding: '5px' }}>
                    <input
                      type="text"
                      placeholder="e.g. item condition"
                      value={item.notes}
                      onChange={(e) => handleItemChange(idx, 'notes', e.target.value)}
                      style={{ width: '120px', padding: '6px' }}
                    />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '20px', marginBottom: '20px' }}>
          <div>
            <label className="form-label" style={{ fontWeight: 'bold', display: 'block', marginBottom: '6px' }}>
              Photographic Evidence (Required if damaged/shortage)
            </label>
            <div style={{ display: 'flex', gap: '10px' }}>
              <input
                type="text"
                className="form-input"
                placeholder="No evidence attached"
                value={evidenceUrl}
                onChange={(e) => setEvidenceUrl(e.target.value)}
                readOnly
                style={{ flex: 1 }}
              />
              <button 
                type="button" 
                className="btn btn--secondary" 
                onClick={handleSimulatePhoto}
                style={{ padding: '8px 12px' }}
              >
                📸 Simulate Camera
              </button>
            </div>
            {evidenceUrl && (
              <div style={{ marginTop: '8px', padding: '6px', background: '#eaf2f8', borderRadius: '4px', fontSize: '12px', color: '#2980b9' }}>
                ✓ Image simulated & referenced successfully.
              </div>
            )}
          </div>

          <div>
            <label className="form-label" style={{ fontWeight: 'bold', display: 'block', marginBottom: '6px' }}>
              Overall Receiving Remarks / Notes
            </label>
            <textarea
              className="form-input"
              rows="2"
              placeholder="Provide delivery status, driver name, vehicle details, etc."
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              style={{ width: '100%', resize: 'none' }}
            />
          </div>
        </div>

        <div style={{ display: 'flex', gap: '12px', justifyContent: 'flex-end' }}>
          <button type="button" className="btn btn--secondary" onClick={onCancel} disabled={loading}>
            Cancel
          </button>
          <button type="submit" className="btn btn--primary" disabled={loading}>
            {loading ? 'Reconciling Delivery...' : 'Verify & Receive'}
          </button>
        </div>
      </form>
    </div>
  );
}
