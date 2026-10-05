import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/operations_service.dart';

/// Agent workflow runs — the cross-component view the web app exposes on its
/// Agent Workflows page.
///
/// A workflow is one procurement run that chains the agents. Each step records
/// which agent ran, the tool it called, and whether it succeeded, which is what
/// makes "the agents really executed" checkable rather than merely asserted.
class AgentWorkflowsScreen extends StatefulWidget {
  const AgentWorkflowsScreen({super.key, this.service});

  final OperationsService? service;

  @override
  State<AgentWorkflowsScreen> createState() => _AgentWorkflowsScreenState();
}

class _AgentWorkflowsScreenState extends State<AgentWorkflowsScreen> {
  final _service = OperationsService();
  List<Map<String, dynamic>> _workflows = const [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final page = await (widget.service ?? _service).listAgentWorkflows();
      if (mounted) {
        setState(
          () => _workflows = (page['items'] as List<dynamic>? ?? const []).cast<Map<String, dynamic>>(),
        );
      }
    } catch (e) {
      if (mounted) setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _open(Map<String, dynamic> workflow) async {
    final id = (workflow['id'] as num).toInt();
    try {
      final detail = await (widget.service ?? _service).getAgentWorkflow(id);
      if (!mounted) return;
      await showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        builder: (_) => _WorkflowDetailSheet(workflow: detail),
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(e.toString().replaceFirst('Exception: ', ''))),
      );
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Agent Workflows'),
      actions: [IconButton(onPressed: _load, icon: const Icon(Icons.refresh))],
    ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
            ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
            : _workflows.isEmpty
                ? const EmptyStateWidget(
                    title: 'No agent workflows yet',
                    message: 'A workflow is recorded when an approved request enters procurement.',
                  )
                : RefreshIndicator(
                    onRefresh: _load,
                    child: ListView.separated(
                      padding: const EdgeInsets.all(16),
                      itemCount: _workflows.length,
                      separatorBuilder: (_, _) => const SizedBox(height: 10),
                      itemBuilder: (_, index) => _WorkflowCard(
                        workflow: _workflows[index],
                        onTap: () => _open(_workflows[index]),
                      ),
                    ),
                  ),
  );
}

StatusTone _workflowTone(String status, int failed) => switch (status) {
      'Completed' => failed > 0 ? StatusTone.warning : StatusTone.success,
      'Failed' => StatusTone.danger,
      'Running' || 'InProgress' => StatusTone.info,
      'Pending' => StatusTone.warning,
      _ => StatusTone.neutral,
    };


class _WorkflowCard extends StatelessWidget {
  const _WorkflowCard({required this.workflow, required this.onTap});

  final Map<String, dynamic> workflow;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final id = (workflow['id'] as num?)?.toInt() ?? 0;
    final status = workflow['status']?.toString() ?? 'Unknown';
    final steps = (workflow['stepCount'] as num?)?.toInt() ?? 0;
    final failed = (workflow['failedStepCount'] as num?)?.toInt() ?? 0;
    return AppCard(
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text('Workflow #$id', style: Theme.of(context).textTheme.titleMedium),
              StatusChip(label: status, tone: _workflowTone(status, failed)),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            workflow['objective']?.toString() ?? 'Procurement run',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 8),
          Text(
            '$steps step(s) · $failed failed · '
            'request MR-${workflow['materialRequestId'] ?? '—'}',
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
      ),
    );
  }
}

/// Per-step detail: which agent ran, which tool, and whether it succeeded.
class _WorkflowDetailSheet extends StatelessWidget {
  const _WorkflowDetailSheet({required this.workflow});

  final Map<String, dynamic> workflow;

  @override
  Widget build(BuildContext context) {
    // The detail endpoint nests the run under `workflow`; fall back to the
    // summary shape so the sheet still renders if only that is present.
    final run = (workflow['workflow'] as Map<String, dynamic>?) ?? workflow;
    final steps = (workflow['steps'] as List<dynamic>?) ?? const [];
    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      child: SingleChildScrollView(
        child: Padding(
          padding: const EdgeInsets.all(20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Text('Workflow #${run['id']}', style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 4),
              Text(
                run['objective']?.toString() ?? '',
                style: Theme.of(context).textTheme.bodySmall,
              ),
              const SizedBox(height: 16),
              if (steps.isEmpty)
                const AppCard(child: Text('No recorded steps for this workflow.'))
              else
                ...steps.map((step) {
                  final row = step as Map<String, dynamic>;
                  final stepStatus = row['status']?.toString() ?? 'Unknown';
                  return Padding(
                    padding: const EdgeInsets.only(bottom: 10),
                    child: AppCard(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            mainAxisAlignment: MainAxisAlignment.spaceBetween,
                            children: [
                              Expanded(
                                child: Text(
                                  row['agentRole']?.toString() ?? 'Agent',
                                  style: const TextStyle(fontWeight: FontWeight.bold),
                                ),
                              ),
                              StatusChip(
                                label: stepStatus,
                                tone: stepStatus == 'Completed'
                                    ? StatusTone.success
                                    : stepStatus == 'Failed'
                                        ? StatusTone.danger
                                        : StatusTone.neutral,
                              ),
                            ],
                          ),
                          const SizedBox(height: 6),
                          if (row['toolName'] != null) Text('Tool: ${row['toolName']}'),
                          if (row['sequence'] != null)
                            Text('Step ${row['sequence']}', style: Theme.of(context).textTheme.bodySmall),
                        ],
                      ),
                    ),
                  );
                }),
              const SizedBox(height: 12),
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
}
