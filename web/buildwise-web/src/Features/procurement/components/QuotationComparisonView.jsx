import { Button, Card, EmptyState, StatusBadge } from '../../../components/shared'
import { statusTone } from './statusTone'

export default function QuotationComparisonView({ comparison, onRunAnalysis, running, onDeleteQuotation }) {
  if (!comparison || comparison.rows.length === 0 || comparison.quotations.length === 0) {
    return (
      <Card title="Quotation comparison">
        <EmptyState title="No quotations to compare yet" message="Record at least one quotation to see a side-by-side comparison." />
      </Card>
    )
  }

  return (
    <Card title="Quotation comparison" subtitle="One row per requested item, one column per supplier quotation.">
      <div className="actions" style={{ marginBottom: 'var(--space-4)' }}>
        <Button onClick={onRunAnalysis} disabled={running}>{running ? 'Running AI analysis…' : 'Run AI Analysis'}</Button>
      </div>
      <div className="table-wrap">
        <table className="data-table">
          <thead>
            <tr>
              <th>Requested item</th>
              {comparison.quotations.map((q) => (
                <th key={q.id}>
                  {q.supplierName}
                  <div><StatusBadge status={statusTone(q.supplierStatus)}>{q.supplierStatus}</StatusBadge></div>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {comparison.rows.map((row) => (
              <tr key={row.materialRequestItemId}>
                <td><strong>{row.materialName}</strong><div className="muted">{row.requestedQuantity} {row.unit} requested</div></td>
                {comparison.quotations.map((q) => {
                  const offer = row.offers.find((o) => o.quotationId === q.id)
                  if (!offer) return <td key={q.id} className="offer-cell offer-cell--ineligible">Not quoted</td>
                  const ineligible = offer.supplierStatus !== 'Active'
                  return (
                    <td key={q.id} className={`offer-cell ${ineligible ? 'offer-cell--ineligible' : ''}`}>
                      <div className="offer-cell__price">{offer.unitPrice.toLocaleString()} / {row.unit}</div>
                      <div className="offer-cell__meta">{offer.quantityOffered} {row.unit} · total {offer.lineTotal.toLocaleString()}</div>
                      <span className={`coverage-tag ${offer.coversFullQuantity ? 'coverage-tag--full' : 'coverage-tag--partial'}`}>
                        {offer.coversFullQuantity ? `Covers ${offer.quantityOffered}/${row.requestedQuantity}` : `Partial ${offer.quantityOffered}/${row.requestedQuantity}`}
                      </span>
                    </td>
                  )
                })}
              </tr>
            ))}
            <tr>
              <td><strong>Total</strong></td>
              {comparison.quotations.map((q) => <td key={q.id}><strong>{q.totalAmount.toLocaleString()}</strong></td>)}
            </tr>
            <tr>
              <td><strong>Status / actions</strong></td>
              {comparison.quotations.map((q) => (
                <td key={q.id}>
                  <StatusBadge status={statusTone(q.status)}>{q.status}</StatusBadge>
                  {(q.status === 'Submitted' || q.status === 'UnderReview') && onDeleteQuotation && (
                    <div><button className="table-action" onClick={() => onDeleteQuotation(q.id)}>Remove</button></div>
                  )}
                </td>
              ))}
            </tr>
          </tbody>
        </table>
      </div>
    </Card>
  )
}
