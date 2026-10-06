/**
 * Summary banner for a failed submit.
 *
 * A form shows per-field messages beside their inputs, but a business rule such
 * as "cumulative received quantity cannot exceed the ordered quantity" spans
 * several fields, so the same message is repeated here as a readable list. Both
 * come from `describeApiFailure` in services/validationErrors.js, which never
 * passes a raw exception or stack trace through.
 */
export function FormErrorSummary({ general, fieldErrors, title = 'Please fix the following' }) {
  const fieldMessages = Object.values(fieldErrors ?? {})
  if (!general && fieldMessages.length === 0) return null

  return (
    <div className="form-error-summary" role="alert" aria-live="polite">
      <strong className="form-error-summary__title">{title}</strong>
      {general ? <p className="form-error-summary__general">{general}</p> : null}
      {fieldMessages.length > 0 ? (
        <ul className="form-error-summary__list">
          {fieldMessages.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      ) : null}
    </div>
  )
}

export default FormErrorSummary