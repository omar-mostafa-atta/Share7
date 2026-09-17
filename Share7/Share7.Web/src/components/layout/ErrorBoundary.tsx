import { Component } from 'react'
import type { ErrorInfo, ReactNode } from 'react'
import { AlertTriangle, RefreshCw } from 'lucide-react'

// ===========================================================================
// Route error boundary
//
// Without one, a render-time throw anywhere in a page unmounts the entire React
// tree — the sidebar and topbar go with it and the viewer is left staring at a
// blank canvas with no way back except a reload. That is exactly what a
// `.map is not a function` on one unexpected response shape did.
//
// A class component because that is still the only way to catch a render error;
// there is no hook equivalent.
// ===========================================================================

interface Props {
  children: ReactNode

  /** Changing this resets the boundary — the route path, so navigating away recovers. */
  resetKey: string
}

interface State {
  error: Error | null
}

export class RouteErrorBoundary extends Component<Props, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  componentDidUpdate(previous: Props) {
    // Navigating to another page clears the error, so one broken screen does not
    // wedge the whole console until a reload.
    if (previous.resetKey !== this.props.resetKey && this.state.error) {
      this.setState({ error: null })
    }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Logged rather than reported anywhere: this console has no telemetry, and
    // the platform's crash reporter is a Unity-side concern. The stack in the
    // devtools console is what a developer needs.
    console.error('[s7] route crashed', error, info.componentStack)
  }

  render() {
    if (!this.state.error) return this.props.children

    return (
      <div className="s7-card" style={{ padding: '1.5rem', maxWidth: '46rem' }}>
        <div className="s7-inline" style={{ marginBottom: '0.6rem' }}>
          <AlertTriangle size={18} color="var(--s7-danger)" />
          <h2 style={{ fontSize: '1.05rem' }}>This page stopped rendering</h2>
        </div>

        <p style={{ fontSize: '0.85rem', color: 'var(--s7-muted)', marginBottom: '0.9rem' }}>
          The rest of the console still works — pick another page from the sidebar, or retry this
          one. The full stack is in the browser console.
        </p>

        <pre
          style={{
            margin: '0 0 1rem',
            padding: '0.7rem 0.8rem',
            overflowX: 'auto',
            fontFamily: 'var(--s7-font-mono)',
            fontSize: '0.75rem',
            color: 'var(--s7-danger-ink)',
            background: 'var(--s7-danger-bg)',
            border: '1px solid var(--s7-danger-line)',
            borderRadius: 'var(--s7-radius)',
          }}
        >
          {this.state.error.message}
        </pre>

        <button
          type="button"
          className="s7-btn s7-btn-primary"
          onClick={() => this.setState({ error: null })}
        >
          <RefreshCw size={15} /> Retry
        </button>
      </div>
    )
  }
}
