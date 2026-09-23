export default function Pagination({ page, pageSize, total, onPageChange }) {
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(total, page * pageSize);

  return (
    <div className="pagination">
      <span>
        Showing {from}–{to} of {total} request{total === 1 ? '' : 's'}
      </span>
      <div className="pagination__controls">
        <button disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          Previous
        </button>
        {Array.from({ length: totalPages }, (_, i) => i + 1).map((p) => (
          <button
            key={p}
            className={p === page ? 'current' : ''}
            onClick={() => onPageChange(p)}
          >
            {p}
          </button>
        ))}
        <button disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
          Next
        </button>
      </div>
    </div>
  );
}
