export default function AgentPanel({ summary, preview = false }) {
  if (!summary) {
    return (
      <div className="panel">
        <h3>AI Contribution</h3>
        <p className="muted" style={{ fontSize: 13 }}>
          The Request Validation &amp; Planning Agent runs once this request is submitted.
        </p>
      </div>
    );
  }

  return (
    <div className="panel">
      <h3>{preview ? 'Live preview' : 'AI Contribution'}</h3>
      <div className="agent-panel">
        <div className="agent-panel__head">
          <span className="agent-panel__tag">AGENT</span>
          <span className="agent-panel__title">{summary.agent}</span>
        </div>

        {summary.completeness && (
          <div className="agent-panel__row">
            <span>Completeness</span>
            <span className={`flag ${summary.completeness === 'Complete' ? 'flag-ok' : 'flag-warn'}`}>
              {summary.completeness}
            </span>
          </div>
        )}
        <div className="agent-panel__row">
          <span>Urgency</span>
          <span className={`flag ${summary.urgency === 'High' ? 'flag-warn' : 'flag-info'}`}>{summary.urgency}</span>
        </div>

        <div style={{ marginTop: 10, display: 'flex', flexDirection: 'column', gap: 8 }}>
          {summary.flags.map((f, i) => (
            <div key={i} className="agent-panel__row" style={{ borderBottom: 'none', padding: '2px 0' }}>
              <span className={`flag flag-${f.type === 'ok' ? 'ok' : f.type === 'warn' ? 'warn' : 'info'}`} style={{ flexShrink: 0 }}>
                {f.type === 'ok' ? 'OK' : f.type === 'warn' ? '!' : 'i'}
              </span>
              <span style={{ fontSize: 12.5 }}>{f.text}</span>
            </div>
          ))}
        </div>

        <p className="agent-panel__note">{summary.note}</p>
      </div>
    </div>
  );
}
