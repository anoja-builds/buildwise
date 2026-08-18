export default function StatusBadge({ status = 'neutral', children }) {
  return <span className={`badge badge--${status}`}>{children}</span>
}
