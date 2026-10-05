import { useEffect, useState } from 'react'
import { Button, Card, EmptyState, ErrorState, LoadingState, PageHeader, Pagination, SearchInput, SelectInput, StatusBadge } from '../../../components/shared'
import { procurementApi } from '../services/procurementApi'
import { statusTone } from '../components/statusTone'
import SupplierFormModal from '../components/SupplierFormModal'

const PAGE_SIZE = 10

export default function SupplierList({ onOpenSupplier }) {
  const [suppliers, setSuppliers] = useState([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('all')
  const [modalOpen, setModalOpen] = useState(false)
  const [submitting, setSubmitting] = useState(false)

  const load = async (targetPage = page) => {
    setLoading(true)
    setError(null)
    try {
      const data = await procurementApi.listSuppliers({ search: search || undefined, status: status === 'all' ? undefined : status, page: targetPage, pageSize: PAGE_SIZE })
      setSuppliers(data.items)
      setTotal(data.total)
      setPage(data.page)
    } catch (err) {
      setError(err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load(1) }, [status])

  const handleSearchSubmit = (event) => {
    event.preventDefault()
    load(1)
  }

  const handleCreate = async (form) => {
    setSubmitting(true)
    try {
      await procurementApi.createSupplier(form)
      setModalOpen(false)
      await load(1)
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="stack">
      <PageHeader title="Suppliers" description="Manage the supplier directory used across quotations and procurement." actions={<Button onClick={() => setModalOpen(true)}>+ Add supplier</Button>} />
      <div className="toolbar">
        <form className="toolbar__filters" onSubmit={handleSearchSubmit}>
          <SearchInput placeholder="Search by name, contact, or email…" value={search} onChange={(e) => setSearch(e.target.value)} />
          <SelectInput label="Status" value={status} onChange={(e) => setStatus(e.target.value)} options={[{ value: 'all', label: 'All statuses' }, { value: 'Active', label: 'Active' }, { value: 'Inactive', label: 'Inactive' }, { value: 'Suspended', label: 'Suspended' }]} />
        </form>
      </div>
      <Card>
        {loading ? <LoadingState message="Loading suppliers…" /> : error ? <ErrorState message={error} onRetry={() => load()} /> : suppliers.length === 0 ? (
          <EmptyState title="No suppliers yet" message="Add your first supplier to start recording quotations." actionLabel="+ Add supplier" onAction={() => setModalOpen(true)} />
        ) : (
          <>
            <div className="table-wrap">
              <table className="data-table">
                <thead><tr><th>Name</th><th>Contact</th><th>Email</th><th>Phone</th><th>Status</th><th>Actions</th></tr></thead>
                <tbody>
                  {suppliers.map((s) => (
                    <tr key={s.id}>
                      <td><strong>{s.name}</strong></td>
                      <td>{s.contactPerson || '—'}</td>
                      <td>{s.email || '—'}</td>
                      <td>{s.phone || '—'}</td>
                      <td><StatusBadge status={statusTone(s.status)}>{s.status}</StatusBadge></td>
                      <td><button className="table-action" onClick={() => onOpenSupplier(s.id)}>View</button></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination page={page} pageSize={PAGE_SIZE} total={total} onPageChange={(next) => load(next)} />
          </>
        )}
      </Card>
      <SupplierFormModal open={modalOpen} onCancel={() => setModalOpen(false)} onSubmit={handleCreate} submitting={submitting} />
    </div>
  )
}
