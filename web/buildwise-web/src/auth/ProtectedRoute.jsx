export default function ProtectedRoute({ roles = [], allowedRoles = [], children }) {
  if (allowedRoles.some((role) => roles.includes(role))) return children

  return (
    <div className="stack" role="alert">
      <div className="summary-card">
        <div className="summary-card__label">403 — Access denied</div>
        <div className="summary-card__note">Your assigned role cannot open this workspace.</div>
      </div>
    </div>
  )
}
