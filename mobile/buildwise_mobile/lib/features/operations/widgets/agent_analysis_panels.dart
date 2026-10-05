import 'package:flutter/material.dart';

import '../../../core/widgets/ai_analysis_panel.dart';
import '../../../core/widgets/status_chip.dart';

/// Maps each agent's raw response into the [AiAnalysisPanel] model.
///
/// This lives in one place so the web client and the mobile client describe the
/// same agent result the same way, and so a field added to an agent response
/// only has to be surfaced once. Every value here is read from the backend
/// response; nothing is hardcoded, so the panel cannot claim a result the agent
/// did not return.

/// Agent 2 - RequestAnalysisAgent.
/// Response: `{ requestId, flags: [...], status }`.
Widget buildRequestAnalysisPanel(Map<String, dynamic> result) {
  final flags = (result['flags'] as List<dynamic>? ?? const [])
      .map((flag) => flag.toString())
      .toList();
  final status = result['status']?.toString() ?? 'Analyzed';
  return AiAnalysisPanel(
    agent: 'RequestAnalysisAgent',
    tool: 'analyze_request',
    // No executionSource: this endpoint returns only {requestId, flags, status},
    // so the panel states nothing about which engine answered rather than
    // claiming a source it was never told.
    flags: flags,
    fields: [
      AiField('Request', 'MR-${result['requestId'] ?? '—'}'),
      AiField('Analysis status', status, tone: StatusTone.info),
      AiField(
        'Findings',
        flags.isEmpty ? 'No risk flags raised' : '${flags.length} risk flag(s)',
        tone: flags.isEmpty ? StatusTone.success : StatusTone.warning,
      ),
    ],
  );
}

/// Agent 3 - DeliveryDiscrepancyAgent.
/// Response: `{ deliveryId, agent, tool, executionSource, orderedQuantity,
/// receivedQuantity, damagedQuantity, shortageQuantity, shortageDetected,
/// damageDetected, summary, recommendation, deliveryStatus, analyzedAtUtc }`.
Widget buildDeliveryAnalysisPanel(Map<String, dynamic> result) {
  final shortage = result['shortageDetected'] == true;
  final damage = result['damageDetected'] == true;
  return AiAnalysisPanel(
    agent: result['agent']?.toString() ?? 'DeliveryDiscrepancyAgent',
    tool: result['tool']?.toString() ?? 'analyze_discrepancy',
    executionSource: result['executionSource']?.toString(),
    fields: [
      AiField('Delivery', 'DEL-${result['deliveryId'] ?? '—'}'),
      AiField(
        'Quantities',
        'ordered ${result['orderedQuantity'] ?? 0} · '
        'received ${result['receivedQuantity'] ?? 0} · '
        'damaged ${result['damagedQuantity'] ?? 0}',
      ),
      AiField(
        'Shortage',
        shortage ? 'Detected (${result['shortageQuantity'] ?? 0})' : 'None',
        tone: shortage ? StatusTone.danger : StatusTone.success,
      ),
      AiField(
        'Damage',
        damage ? 'Detected' : 'None',
        tone: damage ? StatusTone.danger : StatusTone.success,
      ),
      AiField(
        'Delivery status',
        result['deliveryStatus']?.toString() ?? '—',
        tone: StatusTone.info,
      ),
      AiField('Summary', result['summary']?.toString() ?? '—'),
      AiField('Recommendation', result['recommendation']?.toString() ?? '—'),
    ],
  );
}

/// Agent 4 - QualityRiskAnalysisAgent.
/// Response: `{ inspectionId, deliveryId, agent, tool, executionSource,
/// riskLevel, requiresNcr, suggestedCorrectiveAction, riskFlags, totalInspected,
/// totalRejected, rejectionRatePct, inspectionStatus, overallDecision }`.
Widget buildQualityRiskPanel(Map<String, dynamic> result) {
  final risk = result['riskLevel']?.toString() ?? 'Unknown';
  final requiresNcr = result['requiresNcr'] == true;
  final flags = (result['riskFlags'] as List<dynamic>? ?? const [])
      .map((flag) => flag.toString())
      .toList();
  return AiAnalysisPanel(
    agent: result['agent']?.toString() ?? 'QualityRiskAnalysisAgent',
    tool: result['tool']?.toString() ?? 'analyze_quality_risk',
    executionSource: result['executionSource']?.toString(),
    flags: flags,
    fields: [
      AiField('Inspection', 'INS-${result['inspectionId'] ?? '—'}'),
      AiField('Delivery', 'DEL-${result['deliveryId'] ?? '—'}'),
      AiField(
        'Risk level',
        risk,
        tone: switch (risk) {
          'High' => StatusTone.danger,
          'Medium' => StatusTone.warning,
          'Low' => StatusTone.success,
          _ => StatusTone.neutral,
        },
      ),
      AiField(
        'NCR',
        requiresNcr ? 'Required by this assessment' : 'Not required',
        tone: requiresNcr ? StatusTone.warning : StatusTone.success,
      ),
      AiField(
        'Quantities',
        '${result['totalInspected'] ?? 0} inspected · '
        '${result['totalRejected'] ?? 0} rejected · '
        '${result['rejectionRatePct'] ?? 0}%',
      ),
      // The authoritative record, not the agent's view of it. Showing both makes
      // the authority boundary visible: the agent assesses, the record decides.
      AiField(
        'Recorded inspection',
        '${_humanize(result['inspectionStatus']?.toString())} · '
        '${_humanize(result['overallDecision']?.toString())}',
        tone: StatusTone.info,
      ),
      AiField('Corrective action', result['suggestedCorrectiveAction']?.toString() ?? '—'),
    ],
  );
}

String _humanize(String? value) {
  if (value == null || value.isEmpty) return '—';
  final text = value.replaceAllMapped(
    RegExp('([a-z0-9])([A-Z])'),
    (match) => '${match.group(1)} ${match.group(2)}',
  );
  return '${text[0].toUpperCase()}${text.substring(1)}';
}
