import Button from './Button'
export default function ConfirmDialog({ open, title = 'Confirm action', message, confirmLabel = 'Confirm', variant = 'danger', onConfirm, onCancel }) {
  if (!open) return null
  return <div className="dialog-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && onCancel?.()}><div className="dialog" role="alertdialog" aria-modal="true" aria-labelledby="dialog-title" aria-describedby="dialog-message"><h2 id="dialog-title">{title}</h2><p id="dialog-message">{message}</p><div className="dialog__actions"><Button variant="secondary" onClick={onCancel}>Cancel</Button><Button variant={variant} onClick={onConfirm}>{confirmLabel}</Button></div></div></div>
}
