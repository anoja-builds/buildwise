import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import 'app_button.dart';
import 'app_card.dart';

/// Opens an agent result in a modal sheet, mirroring the web app's
/// "Run AI Analysis / Re-run Analysis" action.
///
/// The agent is called on open, so no result is ever shown before the agent has
/// actually answered. Re-running is offered afterwards and is safe: the agents
/// are read-only, so repeating one never creates a second NCR, discrepancy or
/// workflow record.
Future<void> showAiAnalysisSheet(
  BuildContext context, {
  required String title,
  required Future<Map<String, dynamic>> Function() run,
  required Widget Function(Map<String, dynamic> result) builder,
}) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    builder: (_) => _AiAnalysisSheet(title: title, run: run, builder: builder),
  );
}

class _AiAnalysisSheet extends StatefulWidget {
  const _AiAnalysisSheet({
    required this.title,
    required this.run,
    required this.builder,
  });

  final String title;
  final Future<Map<String, dynamic>> Function() run;
  final Widget Function(Map<String, dynamic> result) builder;

  @override
  State<_AiAnalysisSheet> createState() => _AiAnalysisSheetState();
}

class _AiAnalysisSheetState extends State<_AiAnalysisSheet> {
  Map<String, dynamic>? _result;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _run();
  }

  Future<void> _run() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final result = await widget.run();
      if (mounted) setState(() => _result = result);
    } catch (e) {
      if (mounted) setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
    child: SingleChildScrollView(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(widget.title, style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 16),
            if (_loading)
              const Center(
                child: Padding(
                  padding: EdgeInsets.all(24),
                  child: CircularProgressIndicator(),
                ),
              )
            else if (_error != null)
              AppCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Analysis failed', style: Theme.of(context).textTheme.titleMedium),
                    const SizedBox(height: 6),
                    Text(_error!, style: const TextStyle(color: AppColors.danger)),
                    const SizedBox(height: 12),
                    AppButton(label: 'Try again', onPressed: _run),
                  ],
                ),
              )
            else if (_result != null)
              AppCard(child: widget.builder(_result!)),
            const SizedBox(height: 16),
            AppButton(
              label: 'Re-run Analysis',
              expand: true,
              variant: AppButtonVariant.secondary,
              onPressed: _loading ? null : _run,
            ),
            const SizedBox(height: 8),
            AppButton(
              label: 'Close',
              expand: true,
              onPressed: () => Navigator.of(context).pop(),
            ),
          ],
        ),
      ),
    ),
  );
}
