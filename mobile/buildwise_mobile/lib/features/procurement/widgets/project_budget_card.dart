import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart';
import '../services/procurement_service.dart';

class ProjectBudgetCard extends StatefulWidget {
  const ProjectBudgetCard({
    super.key,
    required this.projectId,
    required this.service,
    required this.canEdit,
  });
  final int projectId;
  final ProcurementService service;
  final bool canEdit;
  @override
  State<ProjectBudgetCard> createState() => _ProjectBudgetCardState();
}

class _ProjectBudgetCardState extends State<ProjectBudgetCard> {
  final _draft = TextEditingController();
  String? _error;
  String _projectName = '';
  bool _loading = true, _saving = false, _saved = false;
  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _draft.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final data = await widget.service.getProjectBudget(widget.projectId);
      if (!mounted) return;
      _draft.text = data['materialBudgetAmount']?.toString() ?? '';
      _projectName = data['projectName']?.toString() ?? '';
      _error = null;
    } catch (error) {
      if (mounted) _error = error.toString().replaceFirst('Exception: ', '');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _save() async {
    if (!widget.canEdit || _saving) return;
    final value = _draft.text.trim();
    final amount = value.isEmpty ? null : double.tryParse(value);
    if (value.isNotEmpty &&
        (amount == null || !amount.isFinite || amount < 0)) {
      setState(
        () => _error =
            'Enter a non-negative amount, or leave blank for no budget.',
      );
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
      _saved = false;
    });
    try {
      final data = await widget.service.updateProjectBudget(
        widget.projectId,
        amount,
      );
      if (mounted) {
        setState(() {
          _draft.text = data['materialBudgetAmount']?.toString() ?? '';
          _saved = true;
        });
      }
    } catch (error) {
      if (mounted) {
        setState(
          () => _error = error.toString().replaceFirst('Exception: ', ''),
        );
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) => AppCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Materials budget', style: Theme.of(context).textTheme.titleLarge),
        if (_projectName.isNotEmpty) Text('Project: $_projectName'),
        const SizedBox(height: 12),
        if (_loading)
          const LinearProgressIndicator()
        else ...[
          TextField(
            controller: _draft,
            enabled: widget.canEdit && !_saving,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: const InputDecoration(
              labelText: 'Materials budget (LKR)',
              hintText: 'No budget set',
            ),
          ),
          const SizedBox(height: 8),
          Text(
            widget.canEdit
                ? 'A quotation above this landed total is flagged for the approving manager. It is not blocked.'
                : 'Read-only. A Procurement Manager or Site Manager sets the allocation.',
          ),
          if (widget.canEdit)
            TextButton(
              onPressed: _saving ? null : _save,
              child: Text(_saving ? 'Saving…' : 'Save budget'),
            ),
          if (_saved) const Text('Budget saved.'),
          if (_error != null)
            Text(
              _error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
        ],
      ],
    ),
  );
}
