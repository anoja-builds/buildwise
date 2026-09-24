import React, { useState } from 'react';
import { materialRequestService } from '../services/materialRequestService';

export default function CreateMaterialRequestModal({ onClose, onSuccess }) {
  const [projectId, setProjectId] = useState('1');
  const [requestedByUserId, setRequestedByUserId] = useState('1');
  const [priority, setPriority] = useState('High');
  const [requiredDate, setRequiredDate] = useState(
    new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString().split('T')[0]
  );
  const [reason, setReason] = useState('');
  const [siteNotes, setSiteNotes] = useState('');

  // Items list
  const [items, setItems] = useState([
    { materialId: 1, quantity: 250, unit: 'bags', notes: 'Grade 42.5 OPC Cement' }
  ]);

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const handleAddItem = () => {
    setItems([...items, { materialId: 1, quantity: 100, unit: 'bags', notes: '' }]);
  };

  const handleItemChange = (index, field, value) => {
    const newItems = [...items];
    newItems[index][field] = value;
    setItems(newItems);
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setLoading(true);
    setError('');

    try {
      const payload = {
        projectId: parseInt(projectId, 10),
        requestedByUserId: parseInt(requestedByUserId, 10),
        priority,
        requiredDate: new Date(requiredDate).toISOString(),
        reason,
        siteNotes,
        submitImmediately: true,
        items: items.map(i => ({
          materialId: parseInt(i.materialId, 10),
          quantity: parseFloat(i.quantity),
          unit: i.unit,
          requiredDate: new Date(requiredDate).toISOString(),
          notes: i.notes
        }))
      };

      await materialRequestService.createRequest(payload);
      if (onSuccess) onSuccess();
    } catch (err) {
      setError(err.message || 'Failed to submit request');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={{
      position: 'fixed', top: 0, left: 0, right: 0, bottom: 0,
      background: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000
    }}>
      <div className="card fade-in" style={{ width: '600px', maxHeight: '90vh', overflowY: 'auto', padding: '24px', background: '#fff' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
          <h2 style={{ margin: 0 }}>Create Material Request</h2>
          <button className="btn btn--secondary" onClick={onClose} style={{ padding: '4px 10px' }}>✕</button>
        </div>

        {error && <div style={{ background: '#f8d7da', color: '#721c24', padding: '10px', borderRadius: '4px', marginBottom: '15px' }}>⚠️ {error}</div>}

        <form onSubmit={handleSubmit}>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginBottom: '16px' }}>
            <div>
              <label style={{ fontWeight: 'bold', fontSize: '13px' }}>Project / Construction Site</label>
              <select className="form-control" value={projectId} onChange={(e) => setProjectId(e.target.value)} style={{ width: '100%', padding: '8px', marginTop: '4px' }}>
                <option value="1">Apartment Development - Colombo</option>
                <option value="2">Kandy Highway Project</option>
              </select>
            </div>
            <div>
              <label style={{ fontWeight: 'bold', fontSize: '13px' }}>Priority Level</label>
              <select className="form-control" value={priority} onChange={(e) => setPriority(e.target.value)} style={{ width: '100%', padding: '8px', marginTop: '4px' }}>
                <option value="Low">Low</option>
                <option value="Medium">Medium</option>
                <option value="High">High</option>
                <option value="Urgent">Urgent</option>
              </select>
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px', marginBottom: '16px' }}>
            <div>
              <label style={{ fontWeight: 'bold', fontSize: '13px' }}>Required By Date</label>
              <input type="date" className="form-control" value={requiredDate} onChange={(e) => setRequiredDate(e.target.value)} style={{ width: '100%', padding: '8px', marginTop: '4px' }} required />
            </div>
            <div>
              <label style={{ fontWeight: 'bold', fontSize: '13px' }}>Requested By Officer</label>
              <select className="form-control" value={requestedByUserId} onChange={(e) => setRequestedByUserId(e.target.value)} style={{ width: '100%', padding: '8px', marginTop: '4px' }}>
                <option value="1">Jordan Doe (Site Engineer)</option>
                <option value="2">Ramya Fernando (Procurement Officer)</option>
              </select>
            </div>
          </div>

          <div style={{ marginBottom: '16px' }}>
            <label style={{ fontWeight: 'bold', fontSize: '13px' }}>Purpose / Reason</label>
            <input type="text" className="form-control" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. Ground floor column concreting" style={{ width: '100%', padding: '8px', marginTop: '4px' }} required />
          </div>

          <div style={{ marginBottom: '20px' }}>
            <label style={{ fontWeight: 'bold', fontSize: '13px' }}>Site Notes / Logistics Details</label>
            <textarea className="form-control" value={siteNotes} onChange={(e) => setSiteNotes(e.target.value)} placeholder="e.g. Deliver to Gate B. Tower crane active until 4 PM." style={{ width: '100%', padding: '8px', marginTop: '4px', height: '60px' }} />
          </div>

          <div style={{ borderTop: '1px solid #ddd', paddingTop: '16px', marginBottom: '20px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
              <h4 style={{ margin: 0 }}>Material Items Required</h4>
              <button type="button" className="btn btn--secondary" onClick={handleAddItem} style={{ fontSize: '12px', padding: '4px 8px' }}>+ Add Item</button>
            </div>

            {items.map((item, idx) => (
              <div key={idx} style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 1fr 2fr', gap: '8px', marginBottom: '8px', alignItems: 'center' }}>
                <select className="form-control" value={item.materialId} onChange={(e) => handleItemChange(idx, 'materialId', e.target.value)} style={{ padding: '6px' }}>
                  <option value="1">OPC Cement</option>
                  <option value="2">Reinforcement Steel</option>
                  <option value="3">River Sand</option>
                </select>
                <input type="number" className="form-control" value={item.quantity} onChange={(e) => handleItemChange(idx, 'quantity', e.target.value)} placeholder="Qty" style={{ padding: '6px' }} min="1" required />
                <input type="text" className="form-control" value={item.unit} onChange={(e) => handleItemChange(idx, 'unit', e.target.value)} placeholder="Unit" style={{ padding: '6px' }} required />
                <input type="text" className="form-control" value={item.notes} onChange={(e) => handleItemChange(idx, 'notes', e.target.value)} placeholder="Item notes..." style={{ padding: '6px' }} />
              </div>
            ))}
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
            <button type="button" className="btn btn--secondary" onClick={onClose}>Cancel</button>
            <button type="submit" className="btn btn--primary" disabled={loading}>
              {loading ? 'Submitting...' : 'Submit Material Request'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
