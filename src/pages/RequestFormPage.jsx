import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { listProjects, listMaterials, createRequest, updateRequest, getRequest } from '../api/materialRequestApi';
import { useAuth } from '../context/AuthContext';
import AgentPanel from '../components/AgentPanel';

const emptyItem = () => ({ key: Math.random().toString(36).slice(2), materialId: '', quantity: '' });

export default function RequestFormPage({ mode = 'create' }) {
  const { id } = useParams();
  const navigate = useNavigate();
  const { currentUser } = useAuth();

  const [projects, setProjects] = useState([]);
  const [materials, setMaterials] = useState([]);
  const [loading, setLoading] = useState(mode === 'edit');
  const [saving, setSaving] = useState(false);
  const [errors, setErrors] = useState({});

  const [projectId, setProjectId] = useState('');
  const [requiredDate, setRequiredDate] = useState('');
  const [reason, setReason] = useState('');
  const [items, setItems] = useState([emptyItem()]);

  useEffect(() => {
    listProjects().then(setProjects);
    listMaterials().then(setMaterials);
  }, []);

  useEffect(() => {
    if (mode !== 'edit' || !id) return;
    getRequest(id).then((req) => {
      setProjectId(req.projectId);
      setRequiredDate(req.requiredDate);
      setReason(req.reason);
      setItems(req.items.map((i) => ({ key: i.id, materialId: i.materialId, quantity: i.quantity })));
      setLoading(false);
    });
  }, [mode, id]);

  const updateItem = (key, patch) =>
    setItems((prev) => prev.map((it) => (it.key === key ? { ...it, ...patch } : it)));

  const addItem = () => setItems((prev) => [...prev, emptyItem()]);
  const removeItem = (key) => setItems((prev) => (prev.length > 1 ? prev.filter((it) => it.key !== key) : prev));

  function validate() {
    const errs = {};
    if (!projectId) errs.projectId = 'Select the project this material is for.';
    if (!requiredDate) errs.requiredDate = 'Required date is mandatory.';
    if (!reason || reason.trim().length < 5) errs.reason = 'Give a short reason (at least 5 characters).';
    const badItem = items.some((it) => !it.materialId || !it.quantity || Number(it.quantity) <= 0);
    if (badItem) errs.items = 'Every line needs a material and a quantity greater than zero.';
    setErrors(errs);
    return Object.keys(errs).length === 0;
  }

  // Live preview of what the Request Validation & Planning Agent will
  // report once this is submitted — computed the same way the mock
  // backend does, so the site engineer gets feedback before submitting.
  const livePreview = useMemo(() => {
    if (!requiredDate) return null;
    const today = new Date();
    const required = new Date(requiredDate);
    const daysUntil = Math.ceil((required - today) / (1000 * 60 * 60 * 24));
    const flags = [];
    let urgency = 'Low';
    if (daysUntil <= 2) {
      urgency = 'High';
      flags.push({ type: 'warn', text: `Only ${Math.max(daysUntil, 0)} day(s) until required date — expect an expedite flag.` });
    } else if (daysUntil <= 5) {
      urgency = 'Medium';
    }
    if (daysUntil < 0) flags.push({ type: 'warn', text: 'Required date is already in the past.' });
    if (!reason || reason.trim().length < 10) flags.push({ type: 'warn', text: 'Reason looks brief — add detail to speed up review.' });
    if (flags.length === 0) flags.push({ type: 'ok', text: 'Looks complete so far.' });
    return { agent: 'Request Validation & Planning Agent', urgency, flags, note: 'Preview only — the full structured summary is generated on submit.' };
  }, [requiredDate, reason]);

  async function handleSubmit(e) {
    e.preventDefault();
    if (!validate()) return;
    setSaving(true);
    try {
      const payload = {
        projectId,
        requestedBy: currentUser.name,
        requiredDate,
        reason,
        items: items.map((it) => ({ materialId: it.materialId, quantity: it.quantity })),
      };
      const result = mode === 'edit' ? await updateRequest(id, payload) : await createRequest(payload);
      navigate(`/requests/${result.id}`, { state: { justSubmitted: true } });
    } catch (err) {
      setErrors({ submit: err.message });
    } finally {
      setSaving(false);
    }
  }

  if (loading) return <p className="muted">Loading request…</p>;

  return (
    <div>
      <div className="page-head">
        <div>
          <h1>{mode === 'edit' ? `Edit ${id}` : 'New material request'}</h1>
          <p>
            Submitted as <strong>{currentUser.name}</strong>. Once submitted, the request moves to{' '}
            <em>Awaiting approval</em> and can no longer be edited unless a manager requests revision.
          </p>
        </div>
      </div>

      <div className="detail-grid">
        <form className="form-card" onSubmit={handleSubmit} noValidate>
          <div className="form-section">
            <div className="form-section__head">
              <h3>Request details</h3>
            </div>
            <div className="field-grid">
              <div className="field">
                <label htmlFor="project">Project</label>
                <select id="project" value={projectId} onChange={(e) => setProjectId(e.target.value)}>
                  <option value="">Select project…</option>
                  {projects.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
                {errors.projectId && <span className="field-error">{errors.projectId}</span>}
              </div>
              <div className="field">
                <label htmlFor="requiredDate">Required by</label>
                <input
                  id="requiredDate"
                  type="date"
                  value={requiredDate}
                  onChange={(e) => setRequiredDate(e.target.value)}
                />
                {errors.requiredDate && <span className="field-error">{errors.requiredDate}</span>}
              </div>
              <div className="field full">
                <label htmlFor="reason">Reason</label>
                <textarea
                  id="reason"
                  placeholder="e.g. Ground-floor column concreting scheduled to start this week."
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                />
                {errors.reason && <span className="field-error">{errors.reason}</span>}
              </div>
            </div>
          </div>

          <div className="form-section">
            <div className="form-section__head">
              <h3>Requested items</h3>
              <button type="button" className="btn btn-outline btn-sm" onClick={addItem}>
                + Add line
              </button>
            </div>

            {items.map((it) => {
              const material = materials.find((m) => m.id === it.materialId);
              return (
                <div className="item-row" key={it.key}>
                  <div className="field" style={{ marginBottom: 0 }}>
                    <label>Material</label>
                    <select value={it.materialId} onChange={(e) => updateItem(it.key, { materialId: e.target.value })}>
                      <option value="">Select material…</option>
                      {materials.map((m) => (
                        <option key={m.id} value={m.id}>
                          {m.name}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div className="field" style={{ marginBottom: 0 }}>
                    <label>Quantity</label>
                    <input
                      type="number"
                      min="0"
                      value={it.quantity}
                      onChange={(e) => updateItem(it.key, { quantity: e.target.value })}
                    />
                  </div>
                  <div className="field" style={{ marginBottom: 0 }}>
                    <label>Unit</label>
                    <input value={material?.unit || '—'} disabled />
                  </div>
                  <button type="button" className="btn btn-ghost" onClick={() => removeItem(it.key)} aria-label="Remove line">
                    ✕
                  </button>
                </div>
              );
            })}
            {errors.items && <span className="field-error">{errors.items}</span>}
          </div>

          {errors.submit && <p className="field-error" style={{ marginTop: 16 }}>{errors.submit}</p>}

          <div className="form-actions">
            <button type="button" className="btn btn-outline" onClick={() => navigate(-1)}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? 'Submitting…' : mode === 'edit' ? 'Resubmit request' : 'Submit request'}
            </button>
          </div>
        </form>

        <div>
          <AgentPanel summary={livePreview} preview />
        </div>
      </div>
    </div>
  );
}
