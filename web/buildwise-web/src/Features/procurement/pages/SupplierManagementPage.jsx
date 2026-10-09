import React, { useEffect, useState } from 'react';
import { procurementService } from '../services/procurementService';

export default function SupplierManagementPage() {
  const [suppliers, setSuppliers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showAddModal, setShowAddModal] = useState(false);

  const [name, setName] = useState('');
  const [contactPerson, setContactPerson] = useState('');
  const [email, setEmail] = useState('');
  const [phone, setPhone] = useState('');
  const [address, setAddress] = useState('');

  const loadSuppliers = async () => {
    setLoading(true);
    setError('');
    try {
      const data = await procurementService.getSuppliers();
      setSuppliers(data);
    } catch (err) {
      setError(err.message || 'Failed to load suppliers');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadSuppliers();
  }, []);

  const handleAddSupplier = async (e) => {
    e.preventDefault();
    try {
      await procurementService.createSupplier({
        name,
        contactPerson,
        email,
        phone,
        address,
        status: 'Active'
      });
      setShowAddModal(false);
      setName('');
      setContactPerson('');
      setEmail('');
      setPhone('');
      setAddress('');
      loadSuppliers();
    } catch (err) {
      alert(err.message);
    }
  };

  return (
    <div style={{ padding: '20px' }} className="fade-in">
      <div className="page-header" style={{ marginBottom: '20px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div>
          <h1 className="page-header__title">Supplier Directory & Performance</h1>
          <p className="page-header__subtitle">
            Manage approved Sri Lankan construction suppliers, contact details, and status.
          </p>
        </div>
        <button className="btn btn--primary" onClick={() => setShowAddModal(true)}>
          + Register New Supplier
        </button>
      </div>

      {error && <div className="error-state" style={{ marginBottom: '20px' }}>⚠️ {error}</div>}

      {loading ? (
        <div style={{ textAlign: 'center', padding: '40px' }}>
          <div className="spinner"></div>
          <p>Loading suppliers...</p>
        </div>
      ) : (
        <div className="card" style={{ padding: '20px' }}>
          <table className="table" style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ background: '#f8f9fa', textAlign: 'left' }}>
                <th style={{ padding: '10px' }}>Supplier Name</th>
                <th style={{ padding: '10px' }}>Contact Person</th>
                <th style={{ padding: '10px' }}>Phone / Email</th>
                <th style={{ padding: '10px' }}>Address</th>
                <th style={{ padding: '10px' }}>Status</th>
              </tr>
            </thead>
            <tbody>
              {suppliers.map((s) => (
                <tr key={s.id} style={{ borderBottom: '1px solid #eee' }}>
                  <td style={{ padding: '10px', fontWeight: 'bold' }}>{s.name}</td>
                  <td style={{ padding: '10px' }}>{s.contactPerson}</td>
                  <td style={{ padding: '10px', fontSize: '13px' }}>
                    <div>{s.phone}</div>
                    <div style={{ color: '#666' }}>{s.email}</div>
                  </td>
                  <td style={{ padding: '10px', fontSize: '13px' }}>{s.address}</td>
                  <td style={{ padding: '10px' }}>
                    <span className={`badge ${s.status === 'Active' ? 'badge--success' : s.status === 'Suspended' ? 'badge--danger' : 'badge--secondary'}`}>
                      {s.status}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {showAddModal && (
        <div style={{ position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, background: 'rgba(0,0,0,0.5)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000 }}>
          <div className="card fade-in" style={{ width: '500px', padding: '24px', background: '#fff' }}>
            <h3 style={{ marginTop: 0 }}>Register New Supplier</h3>
            <form onSubmit={handleAddSupplier}>
              <div style={{ marginBottom: '12px' }}>
                <label style={{ fontWeight: 'bold', fontSize: '13px' }}>Business Name</label>
                <input type="text" className="form-control" value={name} onChange={(e) => setName(e.target.value)} required style={{ width: '100%', padding: '8px' }} />
              </div>
              <div style={{ marginBottom: '12px' }}>
                <label style={{ fontWeight: 'bold', fontSize: '13px' }}>Contact Person</label>
                <input type="text" className="form-control" value={contactPerson} onChange={(e) => setContactPerson(e.target.value)} required style={{ width: '100%', padding: '8px' }} />
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', marginBottom: '12px' }}>
                <div>
                  <label style={{ fontWeight: 'bold', fontSize: '13px' }}>Email</label>
                  <input type="email" className="form-control" value={email} onChange={(e) => setEmail(e.target.value)} required style={{ width: '100%', padding: '8px' }} />
                </div>
                <div>
                  <label style={{ fontWeight: 'bold', fontSize: '13px' }}>Phone Number</label>
                  <input type="text" className="form-control" value={phone} onChange={(e) => setPhone(e.target.value)} required style={{ width: '100%', padding: '8px' }} />
                </div>
              </div>
              <div style={{ marginBottom: '20px' }}>
                <label style={{ fontWeight: 'bold', fontSize: '13px' }}>Address (Sri Lanka)</label>
                <textarea className="form-control" value={address} onChange={(e) => setAddress(e.target.value)} required style={{ width: '100%', padding: '8px', height: '60px' }} />
              </div>
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                <button type="button" className="btn btn--secondary" onClick={() => setShowAddModal(false)}>Cancel</button>
                <button type="submit" className="btn btn--primary">Register Supplier</button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
