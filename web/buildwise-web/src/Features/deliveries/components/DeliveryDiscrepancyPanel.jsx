import React, { useState, useEffect } from 'react';
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
  const [historyLoading, setHistoryLoading] = useState(false);
  const [error, setError] = useState('');
  const [showHistory, setShowHistory] = useState(false);

  // Load workflow history when panel opens
  useEffect(() => {
    if (deliveryId) {
      loadHistory();
    }
  }, [deliveryId]);

  const loadHistory = async () => {
    setHistoryLoading(true);
    try {
      const data = await deliveryService.getDiscrepancyHistory(deliveryId);
      setHistory(data);
    } catch {
      // History is optional — don't block the panel for this
    } finally {
      setHistoryLoading(false);
    }
  };

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
      case 'high': return '#e74c3c';
      case 'medium': return '#f39c12';
      case 'low': return '#3498db';
      default: return '#27ae60';
    }
  };

  return (
    <div className="card" style={{ marginTop: '20px', borderLeft: '4px solid #e67e22' }}>
      <div style={{ padding: '16px' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
          <h4 style={{ margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
            🔍 Delivery Discrepancy Agent
            <span style={{ fontSize: '11px', color: '#888', fontWeight: 'normal' }}>
              (Post-Receiving Analysis)
            </span>
          </h4>
          <div style={{ display: 'flex', gap: '8px' }}>
            {history.length > 0 && (
              <button
                className="btn btn--secondary"
                onClick={() => setShowHistory(!showHistory)}
                style={{ padding: '4px 10px', fontSize: '12px' }}
              >
                {showHistory ? 'Hide History' : `History (${history.length})`}
              </button>
            )}
            <button
              className="btn btn--primary"
              onClick={handleAnalyze}
              disabled={loading}
              style={{ padding: '6px 14px', fontSize: '13px' }}
            >
              {loading ? 'Analyzing...' : 'Run Discrepancy Analysis'}
            </button>
          </div>
        </div>

        {loading && (
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px', color: '#666', padding: '10px 0' }}>
            <div className="spinner"></div>
            <span>Agent comparing ordered, received, and damaged quantities...</span>
          </div>
        )}

        {error && (
          <div style={{ color: '#dc3545', padding: '10px', background: '#fdf2f2', borderRadius: '6px', marginBottom: '12px' }}>
            ⚠️ {error}
          </div>
        )}

        {!loading && !analysis && !error && (
          <p style={{ fontSize: '13px', color: '#888', margin: 0 }}>
            Run the Discrepancy Agent to analyze actual receiving discrepancies — shortages, damage, and unexpected quantities.
          </p>
        )}

        {/* Analysis Results */}
        {analysis && (
          <div style={{ animation: 'fadeIn 0.3s ease-in-out' }}>
            {/* Summary Header */}
            <div style={{
              display: 'flex', gap: '16px', marginBottom: '16px', padding: '12px',
              background: analysis.hasDiscrepancies ? '#fef9e7' : '#eafaf1', borderRadius: '6px'
            }}>
              <div style={{ textAlign: 'center', flex: 1 }}>
                <div style={{ fontSize: '11px', color: '#888', marginBottom: '2px' }}>Status</div>
                <span className={`badge ${analysis.hasDiscrepancies ? 'badge--danger' : 'badge--success'}`}>
                  {analysis.hasDiscrepancies ? 'DISCREPANCIES FOUND' : 'NO DISCREPANCIES'}
                </span>
              </div>
              <div style={{ textAlign: 'center', flex: 1 }}>
                <div style={{ fontSize: '11px', color: '#888', marginBottom: '2px' }}>Total Shortage</div>
                <span style={{ fontWeight: 'bold', color: analysis.totalShortage > 0 ? '#d35400' : '#27ae60' }}>
                  {analysis.totalShortage}
                </span>
              </div>
              <div style={{ textAlign: 'center', flex: 1 }}>
                <div style={{ fontSize: '11px', color: '#888', marginBottom: '2px' }}>Total Damaged</div>
                <span style={{ fontWeight: 'bold', color: analysis.totalDamaged > 0 ? '#c0392b' : '#27ae60' }}>
                  {analysis.totalDamaged}
                </span>
              </div>
              <div style={{ textAlign: 'center', flex: 1 }}>
                <div style={{ fontSize: '11px', color: '#888', marginBottom: '2px' }}>Undamaged Received</div>
                <span style={{ fontWeight: 'bold', color: '#2980b9' }}>
                  {analysis.totalUndamagedReceived}
                </span>
              </div>
              <div style={{ textAlign: 'center', flex: 1 }}>
                <div style={{ fontSize: '11px', color: '#888', marginBottom: '2px' }}>Workflow</div>
                <span style={{ fontSize: '12px', color: '#6c757d' }}>#{analysis.workflowId}</span>
              </div>
            </div>

            {/* Item-Level Breakdown */}
            <div style={{ marginBottom: '16px' }}>
              <h5 style={{ margin: '0 0 8px 0', fontSize: '13px' }}>Item-Level Analysis</h5>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '12.5px' }}>
                <thead>
                  <tr style={{ background: '#f8f9fa', textAlign: 'left' }}>
                    <th style={{ padding: '8px' }}>Material</th>
                    <th style={{ padding: '8px' }}>Ordered</th>
                    <th style={{ padding: '8px' }}>Prior Recv</th>
                    <th style={{ padding: '8px' }}>New Recv</th>
                    <th style={{ padding: '8px' }}>Damaged</th>
                    <th style={{ padding: '8px' }}>Undamaged</th>
                    <th style={{ padding: '8px' }}>Shortage</th>
                    <th style={{ padding: '8px' }}>Flags</th>
                  </tr>
                </thead>
                <tbody>
                  {analysis.items?.map((item, idx) => (
                    <tr key={idx} style={{
                      borderBottom: '1px solid #eee',
                      background: item.hasDiscrepancy ? '#fffbf0' : 'transparent'
                    }}>
                      <td style={{ padding: '8px', fontWeight: 'bold' }}>
                        {item.materialName} <span style={{ color: '#888', fontWeight: 'normal' }}>({item.materialUnit})</span>
                      </td>
                      <td style={{ padding: '8px' }}>{item.orderedQuantity}</td>
                      <td style={{ padding: '8px', color: '#6c757d' }}>{item.previouslyReceivedQuantity}</td>
                      <td style={{ padding: '8px' }}>{item.newlyReceivedQuantity}</td>
                      <td style={{ padding: '8px', color: item.damagedQuantity > 0 ? '#c0392b' : '#27ae60', fontWeight: 'bold' }}>
                        {item.damagedQuantity}
                      </td>
                      <td style={{ padding: '8px', color: '#2980b9', fontWeight: 'bold' }}>
                        {item.undamagedReceivedQuantity}
                      </td>
                      <td style={{ padding: '8px', color: item.shortageQuantity > 0 ? '#d35400' : '#27ae60', fontWeight: 'bold' }}>
                        {item.shortageQuantity}
                      </td>
                      <td style={{ padding: '8px' }}>
                        {item.discrepancyFlags?.map((flag, i) => (
                          <span key={i} className="badge badge--warning" style={{ marginRight: '4px', fontSize: '10px', padding: '2px 6px' }}>
                            {flag}
                          </span>
                        ))}
                        {!item.hasDiscrepancy && <span style={{ color: '#27ae60', fontSize: '11px' }}>✓ OK</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Recommendations */}
            {analysis.recommendations?.length > 0 && (
              <div style={{ marginBottom: '12px' }}>
                <h5 style={{ margin: '0 0 8px 0', fontSize: '13px' }}>Agent Recommendations</h5>
                {analysis.recommendations.map((rec, idx) => (
                  <div key={idx} style={{
                    padding: '10px', marginBottom: '8px', borderRadius: '6px',
                    background: '#f8f9fa', borderLeft: `3px solid ${getSeverityColor(rec.severity)}`
                  }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' }}>
                      <span style={{ fontWeight: 'bold', fontSize: '12.5px' }}>
                        {rec.materialName} — {rec.category}
                      </span>
                      <span style={{
                        fontSize: '10px', padding: '2px 8px', borderRadius: '10px',
                        background: getSeverityColor(rec.severity), color: '#fff'
                      }}>
                        {rec.severity}
                      </span>
                    </div>
                    <p style={{ margin: '0 0 4px 0', fontSize: '12px', color: '#555' }}>{rec.description}</p>
                    <p style={{ margin: 0, fontSize: '12px', color: '#16a085', fontStyle: 'italic' }}>
                      💡 {rec.advisory}
                    </p>
                    {rec.isActionRequired && (
                      <span style={{ fontSize: '10px', color: '#e74c3c', marginTop: '4px', display: 'inline-block' }}>
                        ⚡ Action Required
                      </span>
                    )}
                  </div>
                ))}
              </div>
            )}

            {/* Validation */}
            {analysis.validation && (
              <div style={{
                padding: '8px 12px', borderRadius: '4px', fontSize: '11px',
                background: analysis.validation.isValid ? '#eafaf1' : '#fdf2f2',
                color: analysis.validation.isValid ? '#27ae60' : '#c0392b'
              }}>
                {analysis.validation.isValid
                  ? '✓ Analysis validated — all quantities internally consistent.'
                  : `⚠ Validation issues: ${analysis.validation.errors?.join('; ')}`}
                <span style={{ color: '#888', marginLeft: '8px' }}>
                  Mode: {analysis.executionMode}
                </span>
              </div>
            )}
          </div>
        )}

        {/* Workflow History */}
        {showHistory && history.length > 0 && (
          <div style={{ marginTop: '16px', borderTop: '1px solid #eee', paddingTop: '12px' }}>
            <h5 style={{ margin: '0 0 8px 0', fontSize: '13px' }}>Discrepancy Analysis History</h5>
            {history.map((wf, idx) => (
              <div key={idx} style={{
                padding: '10px', marginBottom: '8px', background: '#f8f9fa',
                borderRadius: '6px', fontSize: '12px'
              }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '4px' }}>
                  <span style={{ fontWeight: 'bold' }}>Workflow #{wf.workflowId}</span>
                  <span className={`badge ${wf.status === 'Completed' ? 'badge--success' : 'badge--warning'}`}>
                    {wf.status}
                  </span>
                </div>
                <div style={{ color: '#555' }}>{wf.finalOutcome}</div>
                <div style={{ color: '#888', fontSize: '11px', marginTop: '4px' }}>
                  {wf.completedAt ? new Date(wf.completedAt).toLocaleString() : 'In Progress'}
                  {' — '}{wf.steps?.length || 0} steps executed
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
