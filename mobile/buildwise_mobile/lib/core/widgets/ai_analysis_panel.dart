import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import 'status_chip.dart';

/// One label/value pair in an [AiAnalysisPanel].
///
/// [tone] highlights a value the user must notice (a High risk level, a
/// detected shortage) using the same badge scale as the rest of the app.
class AiField {
  const AiField(this.label, this.value, {this.tone});

  final String label;
  final String value;
  final StatusTone? tone;
}

/// Renders a real agent result.
///
/// The three provenance rows (agent, tool, execution source) are shown first and
/// are never faked: [agent], [tool] and [executionSource] come straight from the
/// backend response. `executionSource` in particular is what proves the real
/// Python agent answered rather than the deterministic fallback, so a demo can
/// show it instead of asserting that "AI analysis completed".
class AiAnalysisPanel extends StatelessWidget {
  const AiAnalysisPanel({
    super.key,
    required this.agent,
    required this.tool,
    this.executionSource,
    required this.fields,
    this.flags = const [],
    this.advisory = defaultAdvisory,
  });

  /// The authority boundary shown under every agent result. Agents recommend;
  /// the backend and the authorized user create and change records.
  static const String defaultAdvisory =
      'Advisory only: it does not change this record or create one. '
      'The backend and the authorized user decide.';

  final String agent;
  final String tool;

  /// Provenance of the answer, straight from the backend response.
  ///
  /// Nullable on purpose. The delivery and quality endpoints report
  /// `executionSource`, which is what proves the real Python agent answered
  /// rather than the deterministic fallback. The request-analysis endpoint does
  /// not report it, and this panel must not invent a source it was not told —
  /// an unverified "Source: PythonRequestAgent" would be a worse claim than no
  /// claim at all.
  final String? executionSource;

  final List<AiField> fields;
  final List<String> flags;
  final String advisory;

  /// Presentation rules for agent flags. These mirror
  /// web/buildwise-web/src/components/shared/materialRequestStatus.js so both
  /// clients colour the same flag identically. An unknown flag falls back to
  /// neutral, so a newer agent can never render as an unexplained badge.
  static const Map<String, StatusTone> _flagTones = {
    'HIGH_URGENCY': StatusTone.danger,
    'LARGE_QUANTITY_ORDER': StatusTone.warning,
    'BULK_ORDER': StatusTone.warning,
    'MINOR_QUALITY_DEFECT': StatusTone.warning,
    'ELEVATED_DEFECT_RATE': StatusTone.warning,
    'HIGH_DEFECT_RATE': StatusTone.danger,
    'HIGH_REJECTION_PERCENTAGE': StatusTone.danger,
  };

  static const Map<String, String> _flagLabels = {
    'HIGH_URGENCY': 'Time-critical — expedite procurement',
    'LARGE_QUANTITY_ORDER': 'Large total quantity — check supply and budget',
    'BULK_ORDER': 'Many line items — plan a consolidated order',
    'MINOR_QUALITY_DEFECT': 'Minor quality defect found',
    'ELEVATED_DEFECT_RATE': 'Elevated defect rate',
    'HIGH_DEFECT_RATE': 'High defect rate',
    'HIGH_REJECTION_PERCENTAGE': 'High rejection percentage',
  };

  static StatusTone flagTone(String flag) => _flagTones[flag] ?? StatusTone.neutral;

  static String flagLabel(String flag) => _flagLabels[flag] ?? flag;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            StatusChip(label: agent, tone: StatusTone.info),
            StatusChip(label: tool, tone: StatusTone.neutral),
            if (executionSource != null)
              StatusChip(label: 'Source: $executionSource', tone: StatusTone.neutral),
          ],
        ),
        if (fields.isNotEmpty) ...[
          const SizedBox(height: 14),
          for (final field in fields) _FieldRow(field: field),
        ],
        if (flags.isNotEmpty) ...[
          const SizedBox(height: 14),
          Text('Risk flags', style: theme.textTheme.labelLarge),
          const SizedBox(height: 6),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final flag in flags)
                StatusChip(label: flagLabel(flag), tone: flagTone(flag)),
              // The raw token stays visible so the exact flag string the agent
              // emitted can be read off the screen during a demo.
              for (final flag in flags) Text(flag, style: theme.textTheme.bodySmall),
            ],
          ),
        ],
        const SizedBox(height: 14),
        Container(
          padding: const EdgeInsets.all(10),
          decoration: BoxDecoration(
            color: AppColors.warning.withValues(alpha: 0.10),
            borderRadius: BorderRadius.circular(8),
          ),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Icon(Icons.info_outline, size: 16, color: AppColors.warning),
              const SizedBox(width: 8),
              Expanded(child: Text(advisory, style: theme.textTheme.bodySmall)),
            ],
          ),
        ),
      ],
    );
  }
}

class _FieldRow extends StatelessWidget {
  const _FieldRow({required this.field});

  final AiField field;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 5),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 132,
            child: Text(
              field.label,
              style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textMuted),
            ),
          ),
          Expanded(
            child: field.tone == null
                ? Text(field.value, style: theme.textTheme.bodyMedium)
                : Align(
                    alignment: Alignment.centerLeft,
                    child: StatusChip(label: field.value, tone: field.tone!),
                  ),
          ),
        ],
      ),
    );
  }
}
