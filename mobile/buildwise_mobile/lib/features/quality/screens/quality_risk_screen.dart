import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart';
import '../services/quality_api_service.dart';

class QualityRiskScreen extends StatefulWidget {
  const QualityRiskScreen({
    super.key,
    required this.inspectionId,
    required this.service,
  });
  final int inspectionId;
  final QualityApiService service;
  @override
  State<QualityRiskScreen> createState() => _QualityRiskScreenState();
}

class _QualityRiskScreenState extends State<QualityRiskScreen> {
  Map<String, dynamic>? _workflow;
  bool _busy = false;
  String? _error;
  Future<void> _analyse() async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final result = await widget.service.analyseInspection(
        widget.inspectionId,
      );
      if (mounted) setState(() => _workflow = result);
    } catch (error) {
      if (mounted) setState(() => _error = error.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    Map<String, dynamic>? recommendation;
    // Only the validated public recommendation is presented, never agent traces.
    if (_workflow?['status'] == 'Completed') {
      for (final step in _workflow?['steps'] as List? ?? []) {
        final result = step['structuredResult'];
        if (result is Map && result['recommendation'] is Map) {
          recommendation = Map<String, dynamic>.from(
            result['recommendation'] as Map,
          );
        }
      }
    }
    final r = recommendation;
    return Scaffold(
      appBar: const WorkspaceAppBar(
        title: Text('AI Quality Risk'),
        subtitle: 'Agent 4 · Inspection assistance',
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          const AdvisoryBanner(
            'Recommendations require human review. AI does not create an NCR or change the inspection decision.',
          ),
          const SizedBox(height: 16),
          if (_workflow == null)
            RecordCard(
              title: reference('INS', widget.inspectionId),
              children: const [
                Text(
                  'Analyse the completed inspection using recorded quality evidence.',
                ),
              ],
            ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 12),
              child: Text(
                _error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
          if (_busy)
            const Padding(
              padding: EdgeInsets.all(20),
              child: LoadingWidget(message: 'Analysing quality evidence...'),
            ),
          if (_workflow == null && !_busy)
            AppButton(
              label: 'Run Quality Analysis',
              onPressed: _analyse,
              expand: true,
            ),
          if (_workflow != null) ...[
            RecordCard(
              title: 'Workflow Status',
              status: '${_workflow!['status']}',
              children: [Text('${_workflow!['finalOutcome'] ?? ''}')],
            ),
            const SizedBox(height: 16),
          ],
          if (r != null) ...[
            RecordCard(
              title: 'Risk Assessment',
              status: r['riskLevel']?.toString(),
              children: [
                Text('${r['evidenceSummary'] ?? ''}'),
                const SizedBox(height: 8),
                Text('${r['rationaleSummary'] ?? ''}'),
                FieldRow(
                  'NCR review suggested',
                  r['ncrRecommended'] == true ? 'Yes' : 'No',
                ),
                for (final flag in r['riskFlags'] as List? ?? [])
                  Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Text('• ${flag['flag']}'),
                  ),
              ],
            ),
            const SizedBox(height: 16),
            for (final item in r['itemRecommendations'] as List? ?? [])
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: RecordCard(
                  title: 'Item #${item['inspectionItemId']}',
                  children: [
                    if (item['suggestedSeverity'] != null)
                      FieldRow(
                        'Suggested severity',
                        '${item['suggestedSeverity']}',
                      ),
                    if (item['suggestedIssueDescription'] != null)
                      Text('${item['suggestedIssueDescription']}'),
                    if (item['suggestedCorrectiveAction'] != null)
                      Text('${item['suggestedCorrectiveAction']}'),
                    Text('${item['rationale'] ?? ''}'),
                  ],
                ),
              ),
          ],
        ],
      ),
    );
  }
}
