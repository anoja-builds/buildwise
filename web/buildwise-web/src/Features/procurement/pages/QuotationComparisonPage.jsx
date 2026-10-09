import React, { useEffect, useState } from 'react';
import { procurementService } from '../services/procurementService';

export default function QuotationComparisonPage() {
  const [materialRequestId, setMaterialRequestId] = useState('1');
  const [comparison, setComparison] = useState(null);
  const [loading, setLoading] = useState(false);
  const [evaluating, setEvaluating] = useState(false);
  const [approving, setApproving] = useState(false);
  const [error, setError] = useState('');
  const [successMessage, setSuccessMessage] = useState('');
  const [aiResult, setAiResult] = useState(null);

  const loadComparison = async (reqId) => {
    setLoading(true);
    setError('');
    setSuccessMessage('');
    try {
      const data = await procurementService.getQuotationComparison(reqId);
      setComparison(data);
    } catch (err) {
      setError(err.message || 'Failed to load quotation comparison data');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadComparison(materialRequestId);
  }, [materialRequestId]);

  const handleRunAiEvaluation = async () => {
    setEvaluating(true);
    setError('');
    setSuccessMessage('');
    try {
      const result = await procurementService.evaluateWithAI(materialRequestId);
      setAiResult(result);
      loadComparison(materialRequestId);
    } catch (err) {
      setError(err.message || 'Failed to evaluate quotations with AI Agent');
    } finally {
      setEvaluating(false);
    }
  };

  const handleApproveRecommendation = async (recommendationId) => {
    if (!window.confirm('HUMAN APPROVAL BOUNDARY: Authorize PO issue for the recommended supplier?')) return;
    setApproving(true);
    setError('');
    try {
      const res = await procurementService.approveRecommendation(recommendationId, {
        userId: 2, // Procurement Manager ID
        decision: 'Approved',
        comment: 'Human Manager approved AI recommended supplier after evaluating delivery dates and landed cost.'
      });
      setSuccessMessage(res.message);
      loadComparison(materialRequestId);
    } catch (err) {
      setError(err.message || 'Failed to authorize procurement recommendation');
    } finally {
      setApproving(false);
    }
  };

  return (
    <div style={{ padding: '20px' }} className="fade-in">
      <div className="page-header" style={{ marginBottom: '20px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div>
          <h1 className="page-header__title">Quotation Comparison & Manager Approval</h1>
          <p className="page-header__subtitle">
            Side-by-side quotation comparison matrix, AI multi-criteria evaluation, and human manager authorization boundary.
          </p>
        </div>
        <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
          <label style={{ fontWeight: 'bold', fontSize: '13px' }}>Select Material Request:</label>
          <select 
            className="form-control" 
            value={materialRequestId} 
            onChange={(e) => setMaterialRequestId(e.target.value)}
            style={{ padding: '6px 12px' }}
          >
            <option value="1">MR-0001 (300 Bags OPC Cement)</option>
          </select>
        </div>
      </div>

      {error && <div className="error-state" style={{ marginBottom: '20px' }}>⚠️ {error}</div>}
      {successMessage && <div style={{ background: '#d4edda', color: '#155724', padding: '12px', borderRadius: '4px', marginBottom: '20px', fontWeight: 'bold' }}>✅ {successMessage}</div>}

      {loading ? (
        <div style={{ textAlign: 'center', padding: '40px' }}>
          <div className="spinner"></div>
          <p>Loading comparative matrix...</p>
        </div>
      ) : comparison && (
        <div>
          {/* Header Request Summary */}
          <div className="card" style={{ padding: '16px', marginBottom: '20px', background: '#f8f9fa' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <div>
                <h3 style={{ margin: 0 }}>Request: MR-{comparison.materialRequestId.toString().padStart(4, '0')}</h3>
                <div style={{ fontSize: '13px', color: '#555', marginTop: '4px' }}>
                  Project: <strong>{comparison.projectName}</strong> | Site Required Date: <strong>{new Date(comparison.requiredDate).toLocaleDateString()}</strong>
                </div>
              </div>
              <button 
                className="btn btn--primary" 
                onClick={handleRunAiEvaluation} 
                disabled={evaluating}
                style={{ background: '#27ae60', borderColor: '#27ae60' }}
              >
                {evaluating ? 'Evaluating...' : '🤖 Run AI Supplier Evaluation (Agent 2)'}
              </button>
            </div>
          </div>

          {/* AI Recommendation Result Alert if available */}
          {(comparison.latestAIRecommendation || aiResult) && (
            <div className="card" style={{ padding: '20px', marginBottom: '24px', borderLeft: '5px solid #27ae60', background: '#eafaf1' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                <div>
                  <h3 style={{ margin: '0 0 8px 0', color: '#1e8449', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <span>🤖</span> AI Recommended Supplier Selection
                  </h3>
                  <div style={{ fontSize: '15px', fontWeight: 'bold', color: '#145a32' }}>
                    {comparison.latestAIRecommendation?.summary || aiResult?.justification}
                  </div>
                  <p style={{ margin: '8px 0 0 0', fontSize: '13.5px', color: '#27ae60' }}>
                    {comparison.latestAIRecommendation?.justification}
                  </p>
                </div>

                {comparison.latestAIRecommendation?.status === 'AwaitingApproval' ? (
                  <button 
                    className="btn btn--primary"
                    onClick={() => handleApproveRecommendation(comparison.latestAIRecommendation.id)}
                    disabled={approving}
                    style={{ background: '#e67e22', borderColor: '#d35400', padding: '10px 16px', fontWeight: 'bold' }}
                  >
                    {approving ? 'Authorizing...' : '🛡️ Human Manager: Approve & Issue PO'}
                  </button>
                ) : (
                  <span className="badge badge--success" style={{ fontSize: '14px', padding: '6px 12px' }}>
                    Status: {comparison.latestAIRecommendation?.status || 'Evaluated'}
                  </span>
                )}
              </div>
            </div>
          )}

          {/* Side-by-side Comparative Table */}
          <div className="card" style={{ padding: '20px' }}>
            <h3 style={{ marginTop: 0, marginBottom: '16px' }}>Side-by-Side Quotation Comparison Matrix</h3>
            {comparison.quotations.length === 0 ? (
              <p className="text-muted">No quotations recorded for this material request yet.</p>
            ) : (
              <table className="table" style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead>
                  <tr style={{ background: '#f2f4f4', textAlign: 'left' }}>
                    <th style={{ padding: '12px' }}>Criteria</th>
                    {comparison.quotations.map((q) => (
                      <th key={q.id} style={{ padding: '12px', background: '#ebf5fb', borderLeft: '2px solid #d4e6f1' }}>
                        <div>{q.supplierName}</div>
                        <div style={{ fontSize: '11px', color: '#555', fontWeight: 'normal' }}>{q.quotationNumber}</div>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  <tr style={{ borderBottom: '1px solid #eee' }}>
                    <td style={{ padding: '12px', fontWeight: 'bold' }}>Supplier Status</td>
                    {comparison.quotations.map((q) => (
                      <td key={q.id} style={{ padding: '12px', borderLeft: '2px solid #f2f4f4' }}>
                        <span className={`badge ${q.supplierStatus === 'Active' ? 'badge--success' : 'badge--danger'}`}>
                          {q.supplierStatus}
                        </span>
                      </td>
                    ))}
                  </tr>
                  <tr style={{ borderBottom: '1px solid #eee' }}>
                    <td style={{ padding: '12px', fontWeight: 'bold' }}>Promised Delivery Date</td>
                    {comparison.quotations.map((q) => (
                      <td key={q.id} style={{ padding: '12px', borderLeft: '2px solid #f2f4f4' }}>
                        <div>{new Date(q.promisedDeliveryDate).toLocaleDateString()}</div>
                        {q.canMeetRequiredDate ? (
                          <span style={{ fontSize: '11px', color: '#27ae60', fontWeight: 'bold' }}>✓ Meets Required Date</span>
                        ) : (
                          <span style={{ fontSize: '11px', color: '#c0392b', fontWeight: 'bold' }}>⚠️ Exceeds Site Required Date</span>
                        )}
                      </td>
                    ))}
                  </tr>
                  <tr style={{ borderBottom: '1px solid #eee' }}>
                    <td style={{ padding: '12px', fontWeight: 'bold' }}>Unit Price Subtotal</td>
                    {comparison.quotations.map((q) => (
                      <td key={q.id} style={{ padding: '12px', borderLeft: '2px solid #f2f4f4' }}>
                        LKR {q.totalUnitPrice.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </td>
                    ))}
                  </tr>
                  <tr style={{ borderBottom: '1px solid #eee' }}>
                    <td style={{ padding: '12px', fontWeight: 'bold' }}>Transport & Logistics Charge</td>
                    {comparison.quotations.map((q) => (
                      <td key={q.id} style={{ padding: '12px', borderLeft: '2px solid #f2f4f4' }}>
                        LKR {q.transportCharge.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </td>
                    ))}
                  </tr>
                  <tr style={{ borderBottom: '1px solid #eee', background: '#fcf3cf' }}>
                    <td style={{ padding: '12px', fontWeight: 'bold' }}>Total Landed Cost</td>
                    {comparison.quotations.map((q) => (
                      <td key={q.id} style={{ padding: '12px', borderLeft: '2px solid #f9e79f', fontWeight: 'bold', fontSize: '15px', color: '#7d6608' }}>
                        LKR {q.totalLandedCost.toLocaleString('en-US', { minimumFractionDigits: 2 })}
                      </td>
                    ))}
                  </tr>
                  <tr style={{ borderBottom: '1px solid #eee' }}>
                    <td style={{ padding: '12px', fontWeight: 'bold' }}>Payment Terms</td>
                    {comparison.quotations.map((q) => (
                      <td key={q.id} style={{ padding: '12px', borderLeft: '2px solid #f2f4f4' }}>
                        {q.paymentTerms}
                      </td>
                    ))}
                  </tr>
                </tbody>
              </table>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
