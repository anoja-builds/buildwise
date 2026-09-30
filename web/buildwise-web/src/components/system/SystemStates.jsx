import React from 'react'
import { Link } from 'react-router-dom'
import { Button, Card } from '../shared'
import './system.css'

export function AccessDeniedView({ landingPath = '/' }) {
  return (
    <div className="system-state-page">
      <Card className="system-state-card">
        <div className="system-state-icon danger">⛔</div>
        <h1 className="system-state-title">Access Denied</h1>
        <p className="system-state-desc">
          You do not have authorization to view this page or perform this action in BuildWise.
        </p>
        <div className="system-state-actions">
          <Link to={landingPath}>
            <Button variant="primary">Return to authorized landing</Button>
          </Link>
        </div>
      </Card>
    </div>
  )
}

export function NotFoundView({ landingPath = '/' }) {
  return (
    <div className="system-state-page">
      <div className="system-state-404">
        <div className="system-404-numeral">404</div>
        <h2 className="system-state-title">Page Not Found</h2>
        <p className="system-state-desc">
          The requested path does not exist in BuildWise or was moved.
        </p>
        <div className="system-state-actions">
          <Link to={landingPath}>
            <Button variant="primary">Return to Home</Button>
          </Link>
        </div>
      </div>
    </div>
  )
}

export function SessionExpiredView() {
  return (
    <div className="system-state-page">
      <Card className="system-state-card session-expired-card">
        <div className="system-state-icon warning">⏱️</div>
        <h1 className="system-state-title">Session Expired</h1>
        <p className="system-state-desc">
          Your active session has expired for security. Please sign in again to access BuildWise.
        </p>
        <div className="system-state-actions">
          <Link to="/login">
            <Button variant="primary">Sign in again</Button>
          </Link>
        </div>
      </Card>
    </div>
  )
}

export class ErrorBoundary extends React.Component {
  constructor(props) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error) {
    return { hasError: true, error }
  }

  componentDidCatch(error, errorInfo) {
    console.error('ErrorBoundary caught an error:', error, errorInfo)
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="system-state-page">
          <Card className="system-state-card">
            <div className="system-state-icon danger">⚠️</div>
            <h1 className="system-state-title">Something went wrong</h1>
            <p className="system-state-desc">
              An unexpected system error occurred. You can retry or reload the page.
            </p>
            {this.state.error && (
              <pre className="system-error-trace">{this.state.error.message || String(this.state.error)}</pre>
            )}
            <div className="system-state-actions">
              <Button variant="primary" onClick={() => window.location.reload()}>
                Reload Application
              </Button>
            </div>
          </Card>
        </div>
      )
    }
    return this.props.children
  }
}
