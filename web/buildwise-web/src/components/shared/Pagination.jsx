export default function Pagination({ page, pageSize, total, onPageChange }) {
  const totalPages = Math.max(1, Math.ceil(total / pageSize))
  const start = total === 0 ? 0 : (page - 1) * pageSize + 1
  const end = Math.min(page * pageSize, total)

  const pageNumbers = []
  const windowSize = 5
  let from = Math.max(1, page - Math.floor(windowSize / 2))
  const to = Math.min(totalPages, from + windowSize - 1)
  from = Math.max(1, to - windowSize + 1)
  for (let n = from; n <= to; n++) pageNumbers.push(n)

  return (
    <div className="pagination">
      <span>Showing {start}–{end} of {total}</span>
      <div className="pagination__buttons">
        <button type="button" className="page-button" aria-label="Previous page" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>‹</button>
        {from > 1 && <span className="muted" style={{ padding: '0 4px' }}>…</span>}
        {pageNumbers.map((n) => (
          <button key={n} type="button" className={`page-button ${n === page ? 'page-button--active' : ''}`} aria-current={n === page ? 'page' : undefined} onClick={() => onPageChange(n)}>{n}</button>
        ))}
        {to < totalPages && <span className="muted" style={{ padding: '0 4px' }}>…</span>}
        <button type="button" className="page-button" aria-label="Next page" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>›</button>
      </div>
    </div>
  )
}
