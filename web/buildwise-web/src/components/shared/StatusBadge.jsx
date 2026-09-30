export default function StatusBadge({ status = 'neutral', tone, children }) {
  return <span className={`badge badge--${tone ?? status}`}>{children ?? status}</span>
}
