import Button from './Button'
export default function EmptyState({ title = 'Nothing here yet', message = 'New items will appear here.', actionLabel, onAction }) {
  return <div className="state"><div className="state__content"><div className="state__icon" aria-hidden="true">□</div><h3>{title}</h3><p>{message}</p>{actionLabel && <Button variant="secondary" onClick={onAction}>{actionLabel}</Button>}</div></div>
}
