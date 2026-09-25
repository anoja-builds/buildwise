import { useEffect, useState } from 'react'
import { Button, Card, EmptyState, ErrorState, LoadingState, PageHeader, SelectInput, StatusBadge, TextInput } from '../components/shared'
import { procurementApi } from '../Features/procurement/services/procurementApi'
import './common/common.css'

export default function RfqPage() {
  const [rfqs, setRfqs] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [status] = useState('all')
  const [selected, setSelected] = useState(null)
  const [creating, setCreating] = useState(false)
  const [requests, setRequests] = useState([])
  const [suppliers, setSuppliers] = useState([])
  const [form, setForm] = useState({ materialRequestId: '', requiredResponseDate: '', notes: '', supplierIds: [] })
  async function load() { setLoading(true); setError(''); try { setRfqs(await procurementApi.listRfqs(status === 'all' ? undefined : status)) } catch (err) { setError(err.message) } finally { setLoading(false) } }
  useEffect(() => { load() }, [status])
  async function openCreate() { setCreating(true); setError(''); try { const [rs, ss] = await Promise.all([procurementApi.listApprovedMaterialRequests(), procurementApi.listSuppliers({ pageSize: 100 })]); setRequests(rs); setSuppliers(ss.items ?? ss); setForm({ materialRequestId: rs[0]?.id ?? '', requiredResponseDate: new Date(Date.now() + 7 * 86400000).toISOString().slice(0, 10), notes: '', supplierIds: [] }) } catch (err) { setError(err.message) } }
  function toggleSupplier(id) { setForm((current) => ({ ...current, supplierIds: current.supplierIds.includes(id) ? current.supplierIds.filter((value) => value !== id) : [...current.supplierIds, id] })) }
  async function create(event) { event.preventDefault(); setError(''); try { await procurementApi.createRfq({ ...form, materialRequestId: Number(form.materialRequestId) }); setCreating(false); await load() } catch (err) { setError(err.message) } }
  async function close(id) { try { await procurementApi.closeRfq(id, 'Closed from RFQ workspace.'); setSelected(null); await load() } catch (err) { setError(err.message) } }
  if (loading) return <LoadingState message="Loading RFQs…" />
  if (error && !creating) return <ErrorState message={error} onRetry={load} />
  return <div className="stack">
    <PageHeader eyebrow="Procurement office" title="RFQs" description="Issue supplier requests for quotation, track invitations, and close the quotation window." actions={<Button onClick={openCreate}>+ Issue RFQ</Button>} />
    {error && <ErrorState message={error} />}
    {creating && <Card title="Issue RFQ" subtitle="RFQs can only be created for Approved material requests"><form className="stack" onSubmit={create}><SelectInput label="Approved material request" value={form.materialRequestId} onChange={(e) => setForm((c) => ({ ...c, materialRequestId: e.target.value }))} options={requests.map((request) => ({ value: request.id, label: `Request #${request.id} · ${request.projectName}` }))} /><TextInput label="Required response date" type="date" required value={form.requiredResponseDate} onChange={(e) => setForm((c) => ({ ...c, requiredResponseDate: e.target.value }))} /><TextInput label="Notes" multiline value={form.notes} onChange={(e) => setForm((c) => ({ ...c, notes: e.target.value }))} /><div><strong>Invite Active suppliers</strong><div className="toolbar__filters">{suppliers.filter((s) => s.status === 'Active').map((supplier) => <label key={supplier.id} className="checkbox-field"><input type="checkbox" checked={form.supplierIds.includes(supplier.id)} onChange={() => toggleSupplier(supplier.id)} /> {supplier.name}</label>)}</div></div><div className="form-actions"><Button variant="secondary" onClick={() => setCreating(false)}>Cancel</Button><Button type="submit" disabled={!form.materialRequestId || form.supplierIds.length === 0}>Issue RFQ</Button></div></form></Card>}
    <Card title="RFQ register" subtitle={`${rfqs.length} RFQs`}>{rfqs.length === 0 ? <EmptyState title="No RFQs yet" message="Issue an RFQ from an approved material request." /> : <div className="table-wrap"><table className="data-table"><thead><tr><th>ID</th><th>Project</th><th>Response date</th><th>Suppliers</th><th>Status</th><th /></tr></thead><tbody>{rfqs.map((rfq) => <tr key={rfq.id}><td>#{rfq.id}</td><td>{rfq.projectName}</td><td>{rfq.requiredResponseDate}</td><td>{rfq.suppliers.length}</td><td><StatusBadge tone={rfq.status === 'Issued' ? 'warning' : 'success'}>{rfq.status}</StatusBadge></td><td><button className="table-action" onClick={() => setSelected(rfq)}>View</button></td></tr>)}</tbody></table></div>}</Card>
    {selected && <Card title={`RFQ #${selected.id}`} subtitle={selected.projectName}><div className="stack"><div className="detail-row"><span className="detail-row__label">Status</span><span className="detail-row__value">{selected.status}</span></div><div className="detail-row"><span className="detail-row__label">Invited suppliers</span><span className="detail-row__value">{selected.suppliers.map((supplier) => `${supplier.supplierName} (${supplier.invitationStatus})`).join(', ') || 'None'}</span></div><Button onClick={() => close(selected.id)} disabled={selected.status !== 'Issued'}>Close RFQ</Button></div></Card>}
  </div>
}
