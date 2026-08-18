import Button from './Button'
export default function ErrorState({ title = 'Something went wrong', message = 'Please try again.', onRetry }) {
  return <div className="state state--error" role="alert"><div className="state__content"><div className="state__icon" aria-hidden="true">!</div><h3>{title}</h3><p>{message}</p>{onRetry && <Button variant="secondary" onClick={onRetry}>Try again</Button>}</div></div>
}
