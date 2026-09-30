import React, { useState, useEffect, useCallback } from 'react';
import { deliveryService } from '../services/deliveryService';

/**
 * DeliveryDiscrepancyPanel — displays real discrepancy analysis from the
 * Delivery Discrepancy Agent (distinct from the Delivery Risk Agent).
 *
 * Risk Agent: evaluates supplier timing risk BEFORE receiving.
 * Discrepancy Agent: analyzes ACTUAL quantity discrepancies AFTER receiving.
 *
 * Calls authenticated ASP.NET Core API only — never calls Python directly.
 * Shows real API errors instead of silently falling back to mock data.
 */
export default function DeliveryDiscrepancyPanel({ deliveryId, onAnalyzed }) {
  const [loading, setLoading] = useState(false);
  const [analysis, setAnalysis] = useState(null);
  const [history, setHistory] = useState([]);
  const [error, setError] = useState('');
  const [showHistory, setShowHistory] = useState(false);

  const loadHistory = useCallback(async () => {
    try {
      const data = await deliveryService.getDiscrepancyHistory(deliveryId);
      setHistory(data);
    } catch {
      // History is optional — don't block the panel for this
    }
  }, [deliveryId]);

  // Load workflow history when panel opens
  useEffect(() => {
    if (deliveryId) {
      loadHistory();
    }
  }, [deliveryId, loadHistory]);

  const handleAnalyze = async () => {
    setLoading(true);
    setError('');
    try {
      const result = await deliveryService.analyzeDiscrepancies(deliveryId);
      setAnalysis(result);
      if (onAnalyzed) onAnalyzed(result);
      // Refresh history after new analysis
      loadHistory();
    } catch (err) {
      setError(err.message || 'Discrepancy analysis failed.');
    } finally {
      setLoading(false);
    }
  };

  const getSeverityColor = (severity) => {
    switch (severity?.toLowerCase()) {
      case 'high': return '#dc2626';
      case 'medium': return '#d97706';
      case 'low': return '#2563eb';
      default: return '#16a34a';
    }
  };

  return (
    <div className="card" style={{ marginTop: '20px', borderLeft: '4px solid var(--color-accent-600)', background: 'var(--color-white)', borderRadius: 'var(--radius-lg)' }}>
      <div style={{ padding: '16px' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px', flexWrap: 'wrap', gap: '8px' }}>
          <div>
            <h4 style={{ margin: 0, display: 'flex', alignItems: 'center', gap: '8px', fontSize: '15px', fontWeight: 700 }}>
              🔍 Delivery Discrepancy Agent
              <span style={{ fontSize: '11px', color: 'var(--color-text-muted)', fontWeight: 'normal' }}>
                (Post-Receiving Analysis)
              </span>
            </h4>
            <span style={{ fontSize: '12px', color: 'var(--color-text-muted)' }}>
              Reconciles ordered vs physically received quantities to identify shortages and transit damage.
            </span>
          </div>
          <div style={{ display: 'flex', gap: '8px' }}>
            {history.length > 0 && (
              <button
                type="button"
                className="btn btn--secondary"
                onClick={() => setShowHistory(!showHistory)}
                style={{ padding: '4px 10px', fontSize: '12px' }}
              >
                {showHistory ? 'Hide History' : `History (${history.length})`}
              </button>
            )}
            <button
              type="button"
              className="btn btn--primary"
              onClick={handleAnalyze}
              disabled={loading}
              style={{ padding: '6px 14px', fontSize: '13px' }}
            >
              {loading ? 'Analyzing...' : 'Run Discrepancy Analysis'}
            </button>
          </div>
        </div>

        <div style={{
          padding: '8px 12px',
          background: 'var(--color-warning-100)',
          borderRadius: 'var(--radius-md)',
          border: '1px solid rgb(217 119 6 / 0.2)',
          fontSize: '12px',
          color: 'var(--color-warning-700)',
          marginBottom: '12px',
          display: 'flex',
          alignItems: 'center',
          gap: '6px'
        }}>
          <span>ℹ️</span>
          <span><strong>Advisory Notice:</strong> Discrepancy evaluation is advisory. Formal goods acceptance and NCR creation require authorized personnel sign-off.</span>
        </div>

        {loading && (
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px', color: 'var(--color-text-muted)', padding: '10px 0' }}>
            <div className="spinner"></div>
            <span>Agent comparing ordered, received, and damaged quantities...</span>
          </div>
        )}

        {error && (
          <div style={{ color: 'var(--color-danger-700)', padding: '10px', background: 'var(--color-danger-100)', borderRadius: '6px', marginBottom: '12px', fontSize: '13px' }}>
            ⚠️ {error}
          </div>
        )}

        {!loading && !analysis && !error && (
          <p style={{ fontSize: '13px', color: 'var(--color-text-muted)', margin: 0 }}>
            Run the Discrepancy Agent to analyze actual receiving discrepancies — shortages, damage, and unexpected quantities.
          </p>
        )}

        {/* Analysis Results */}
        {analysis && (
          <div style={{ animation: 'fadeIn 0.3s ease-in-out' }}>
            {/* Summary Header */}
            <div style={{
              display: 'flex', gap: '16px', marginBottom: '16px', padding: '12px',
              background: analysis.hasDiscrepancies ? 'var(--color-warning-100)' : 'var(--color-success-100)',
              borderRadius: 'var(--radius-md)',
              border: `1px solid ${analysis.hasDiscrepancies ? 'rgb(217 119 6 / 0.25)' : 'rgb(22 163 74 / 0.25)'}`
            }}>
              <div style={{ textAlign: 'center', flex: 1 }}>
                <div style={{ fontSize: '11px', color: 'var(--color-text-muted)', marginBottom: '2px' }}>Status</div>
                <span className={`badge ${analysis.hasDiscrepancies ? 'badge--danger' : 'badge--success'}`}>
                  {analysis.hasDiscrepancies ? 'DISCREPANCIES FOUND' : 'NO DISCREPANCIES'}
                </span>
              </div>
              <div style={{ textAlign: 'center', flex: 1 }}>
                <div style={{ fontSize: '11px', color: 'var(--color-text-muted)', marginBottom: '2px' }}>Total Shortage</div>
                <span style={{ fontWeight: 'bold', color: analysis.totalShortage > 0 ? '#d35400' : '#27ae60' }}>
                  {analysis.totalShortage}
                </span>
              </div>
              <div style={{ textAlign: 'center', flex: 1 }}>
                <div style={{ fontSize: '11px', color: 'var(--color-text-muted)', marginBottom: '2px' }}>Total Damaged</div>
                <span style={{ fontWeight: 'bold', color: analysis.totalDamaged > 0 ? '#c0392b' : '#27ae60' }}>
                  {analysis.totalDamaged}
                </span>
              </div>
              <div style={{ textAlign: 'center', flex: 1 }}>
                <div style={{ fontSize: '11px', color: 'var(--color-text-muted)', marginBottom: '2px' }}>Undamaged Received</div>
                <span style={{ fontWeight: 'bold', color: '#2980b9' }}>
                  {analysis.totalUndamagedReceived}
                </span>
              </div>
              <div style={{ textAlign: 'center', flex: 1 }}>
                <div style={{ fontSize: '11px', color: 'var(--color-text-muted)', marginBottom: '2px' }}>Workflow</div>
                <span style={{ fontSize: '12px', color: '#6c757d' }}>#{analysis.workflowId}</span>
              </div>
            </div>

            {/* Item-Level Breakdown */}
            <div style={{ marginBottom: '16px' }}>
              <h5 style={{ margin: '0 0 8px 0', fontSize: '13px', fontWeight: 600 }}>Item-Level Analysis</h5>
              <div className="table-wrap" style={{ border: '1px solid var(--color-border)', borderRadius: 'var(--radius-md)' }}>
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '12.5px' }}>
                  <thead>
                    <tr style={{ background: 'var(--color-surface-muted)', textAlign: 'left' }}>
                      <th style={{ padding: '8px' }}>Material</th>
                      <th style={{ padding: '8px' }}>Ordered</th>
                      <th style={{ padding: '8px' }}>Received</th>
                      <th style={{ padding: '8px' }}>Damaged</th>
                      <th style={{ padding: '8px' }}>Undamaged</th>
                      <th style={{ padding: '8px' }}>Shortage</th>
                      <th style={{ padding: '8px' }}>Flags</th>
                    </tr>
                  </thead>
                  <tbody>
                    {analysis.items.map((item, idx) => (
                      <tr key={idx} style={{ borderTop: '1px solid var(--color-border)' }}>
                        <td style={{ padding: '8px', fontWeight: 'bold' }}>
                          <span>{item.materialName}</span> <span style={{ color: 'var(--color-text-muted)', fontWeight: 'normal' }}>({item.materialUnit})</span>
                        </td>
                        <td style={{ padding: '8px' }}>{item.orderedQuantity}</td>
                        <td style={{ padding: '8px' }}>{item.newlyReceivedQuantity}</td>
                        <td style={{ padding: '8px', color: item.damagedQuantity > 0 ? '#c0392b' : 'inherit' }}>
                          {item.damagedQuantity}
                        </td>
                        <td style={{ padding: '8px', color: '#27ae60' }}>
                          {item.undamagedReceivedQuantity}
                        </td>
                        <td style={{ padding: '8px', color: item.shortageQuantity > 0 ? '#d35400' : 'inherit' }}>
                          {item.shortageQuantity}
                        </td>
                        <td style={{ padding: '8px' }}>
                          {item.discrepancyFlags && item.discrepancyFlags.length > 0 ? (
                            item.discrepancyFlags.map((flag, fIdx) => (
                              <span key={fIdx} className="badge badge--danger" style={{ marginRight: '4px', fontSize: '10px' }}>
                                {flag}
                              </span>
                            ))
                          ) : (
                            <span className="badge badge--success" style={{ fontSize: '10px' }}>OK</span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>

            {/* Recommendations */}
            {analysis.recommendations && analysis.recommendations.length > 0 && (
              <div style={{ marginBottom: '16px' }}>
                <h5 style={{ margin: '0 0 8px 0', fontSize: '13px', fontWeight: 600 }}>
                  Agent Recommendations ({analysis.recommendations.length})
                </h5>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                  {analysis.recommendations.map((rec, rIdx) => (
                    <div
                      key={rIdx}
                      style={{
                        padding: '10px 12px',
                        borderLeft: `4px solid ${getSeverityColor(rec.severity)}`,
                        background: 'var(--color-surface-muted)',
                        borderRadius: '0 var(--radius-md) var(--radius-md) 0',
                        fontSize: '12.5px'
                      }}
                    >
                      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '4px' }}>
                        <strong style={{ color: getSeverityColor(rec.severity) }}>
                          [{rec.category}] {rec.materialName}
                        </strong>
                        <span style={{ fontSize: '11px', color: 'var(--color-text-muted)' }}>
                          Severity: {rec.severity}
                        </span>
                      </div>
                      <p style={{ margin: '0 0 4px 0', color: 'var(--color-text)' }}>{rec.description}</p>
                      {rec.advisory && (
                        <p style={{ margin: 0, color: 'var(--color-text-muted)', fontStyle: 'italic', fontSize: '11.5px' }}>
                          Advisory: {rec.advisory}
                        </p>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}

        {/* History drawer/section */}
        {showHistory && history.length > 0 && (
          <div style={{ marginTop: '16px', borderTop: '1px solid var(--color-border)', paddingTop: '12px' }}>
            <h5 style={{ margin: '0 0 8px 0', fontSize: '13px', fontWeight: 600 }}>Analysis History</h5>
            <ul style={{ margin: 0, paddingLeft: '18px', fontSize: '12px', color: 'var(--color-text-muted)' }}>
              {history.map((h, idx) => (
                <li key={idx} style={{ marginBottom: '4px' }}>
                  Workflow #{h.workflowId} — {new Date(h.createdAt).toLocaleString()} — {h.executionMode} ({h.status})
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </div>
  );
}
