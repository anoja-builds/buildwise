export default function LoadingState({ message = 'Loading information…' }) {
  return <div className="state" role="status"><div className="state__content"><div className="spinner" aria-hidden="true"/><strong>{message}</strong></div></div>
}
