import Button from './Button'

/**
 * Success popup shown after a record was created or a decision was recorded.
 *
 * The create/decision flows previously reported their outcome as an inline
 * panel the user had to notice; this dialog pops the result in front of them
 * and offers a single action (usually "Back to list"). It reuses the same
 * `.dialog-backdrop` / `.dialog` styles as ConfirmDialog so every modal on the
 * site looks the same. The message text is the contract the tests assert on,
 * so it is passed in verbatim by each caller.
 */
export default function SuccessDialog({ open, title = 'Success', message, confirmLabel = 'OK', onClose }) {
  if (!open) return null
  return (
    <div className="dialog-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && onClose?.()}>
      <div className="dialog" role="alertdialog" aria-modal="true" aria-labelledby="success-dialog-title" aria-describedby="success-dialog-message">
        <h2 id="success-dialog-title">{title}</h2>
        <p id="success-dialog-message">{message}</p>
        <div className="dialog__actions">
          <Button variant="primary" onClick={onClose}>
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  )
}