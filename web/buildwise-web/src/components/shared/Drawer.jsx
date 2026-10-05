import { useEffect } from 'react'
import Button from './Button'

// Closes the panel on Escape. Kept separate so the Drawer stays declarative.
function useEscapeToClose(open, onClose) {
  useEffect(() => {
    if (!open) return
    const onKeyDown = (event) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [open, onClose])
}

/**
 * Side panel for detail views that would otherwise occupy a whole page section
 * (delivery detail, non-conformance detail). Reuses the app's `.dialog-backdrop`
 * overlay so Escape and click-outside behave like every other modal.
 *
 * `open` prop gates rendering — if false, nothing is mounted so the page
 * beneath is fully interactive and the white overlay bug is gone.
 */
export default function Drawer({ open, title, subtitle, onClose, children, footer }) {
  useEscapeToClose(open, onClose)

  // Do not mount at all when closed — avoids the "white panel always visible" bug
  if (!open) return null

  return (
    <div
      className="dialog-backdrop"
      role="presentation"
      style={{ alignItems: 'stretch', padding: 0 }}
      onMouseDown={(event) => event.target === event.currentTarget && onClose()}
    >
      <div
        className="app-drawer"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        style={{ marginLeft: 'auto' }}
      >
        <header className="app-drawer__head">
          <div>
            <h2>{title}</h2>
            {subtitle && <p>{subtitle}</p>}
          </div>
          <button
            type="button"
            className="app-drawer__close-btn"
            onClick={onClose}
            aria-label="Close panel"
            title="Close"
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </button>
        </header>
        <div className="app-drawer__body">{children}</div>
        {footer && <div className="app-drawer__foot">{footer}</div>}
      </div>
    </div>
  )
}