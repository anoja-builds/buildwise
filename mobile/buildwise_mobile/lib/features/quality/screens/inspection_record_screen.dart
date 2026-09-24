import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart' as shared;
import '../models/inspection_record.dart';
import '../services/quality_api_service.dart';

class InspectionRecordScreen extends StatefulWidget {
  const InspectionRecordScreen({
    super.key,
    required this.inspectionId,
    required this.service,
  });
  final int inspectionId;
  final QualityApiService service;

  @override
  State<InspectionRecordScreen> createState() => _InspectionRecordScreenState();
}

class _InspectionRecordScreenState extends State<InspectionRecordScreen> {
  InspectionRecord? _record;
  final _notes = TextEditingController();
  final _scroll = ScrollController();
  final _drafts = <_ItemDraft>[];
  String? _decision;
  String? _error;
  bool _busy = true;

  void _revealError() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted && _error != null && _scroll.hasClients) _scroll.jumpTo(0);
    });
  }

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final record = await widget.service.getInspectionRecord(
        widget.inspectionId,
      );
      if (!mounted) return;
      // A status check after a lost response preserves any unsaved form values.
      if (_record == null || record.completed) _setRecord(record);
      if (!record.editable && !record.completed) {
        _record = record;
      }
    } on QualityApiException catch (error) {
      if (mounted) _error = error.message;
    } catch (_) {
      if (mounted) _error = 'Unable to load the inspection. Please try again.';
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _setRecord(InspectionRecord record) {
    for (final draft in _drafts) {
      draft.dispose();
    }
    _drafts.clear();
    _drafts.addAll(record.items.map(_ItemDraft.new));
    _record = record;
    _decision = record.decision;
    _notes.text = record.notes ?? '';
  }

  Future<void> _complete() async {
    if (_busy || _record?.editable != true) return;
    final items = <InspectionItemSubmission>[];
    try {
      if (_decision == null) {
        throw const FormatException('Choose an overall decision.');
      }
      for (final draft in _drafts) {
        items.add(draft.submission());
      }
      if (items.isEmpty) {
        throw const FormatException('There are no delivery items to inspect.');
      }
    } on FormatException catch (error) {
      setState(() => _error = error.message);
      _revealError();
      return;
    }
    FocusScope.of(context).unfocus();
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final record = await widget.service.completeInspection(
        widget.inspectionId,
        overallDecision: _decision!,
        items: items,
        notes: _notes.text.trim(),
      );
      if (mounted) _setRecord(record);
    } on QualityApiException catch (error) {
      if (mounted) _error = error.message;
    } catch (_) {
      if (mounted) _error = 'Unable to complete the inspection. Check its saved status before retrying.';
    } finally {
      if (mounted) setState(() => _busy = false);
      _revealError();
    }
  }

  @override
  void dispose() {
    _notes.dispose();
    _scroll.dispose();
    for (final draft in _drafts) {
      draft.dispose();
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final record = _record;
    final readOnly = _busy || record?.editable != true;
    return PopScope(
      canPop: !_busy,
      child: Scaffold(
        appBar: AppBar(
          title: Text('Inspection #${widget.inspectionId}'),
          automaticallyImplyLeading: !_busy,
        ),
        body: SafeArea(
          child: ListView(
            controller: _scroll,
            padding: const EdgeInsets.all(16),
            children: [
              if (_busy)
                const LinearProgressIndicator(
                  semanticsLabel: 'Loading inspection',
                ),
              if (_error != null) ...[
                Semantics(
                  liveRegion: true,
                  child: Text(
                    _error!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ),
                shared.AppButton(
                  label: record == null ? 'Retry' : 'Check saved status',
                  onPressed: _busy ? null : _load,
                ),
              ],
              if (record != null) ...[
                Text(
                  record.reference ?? 'Delivery #${record.deliveryId}',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                if (record.completed)
                  Semantics(
                    liveRegion: true,
                    child: const Text('Inspection completed successfully.'),
                  )
                else if (!record.editable)
                  Text(
                    'This inspection cannot be completed in its current state (${record.status}).',
                  )
                else
                  const Text(
                    'Enter results for every item. Edits are saved only when you complete the inspection. Damaged quantities are delivery observations; choose your own inspection decision.',
                  ),
                for (final draft in _drafts)
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: 8),
                    child: shared.AppCard(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            'Item #${draft.item.id} (order item #${draft.item.orderItemId})',
                          ),
                          Text(
                            'Received: ${draft.item.received.toStringAsFixed(2)}',
                          ),
                          Text(
                            'Damaged: ${draft.item.damaged.toStringAsFixed(2)}',
                          ),
                          shared.AppTextField(
                            label: 'Condition',
                            controller: draft.condition,
                            readOnly: readOnly,
                          ),
                          shared.AppTextField(
                            label: 'Accepted quantity',
                            controller: draft.accepted,
                            keyboardType: const TextInputType.numberWithOptions(
                              decimal: true,
                            ),
                            readOnly: readOnly,
                          ),
                          shared.AppTextField(
                            label: 'Rejected quantity',
                            controller: draft.rejected,
                            keyboardType: const TextInputType.numberWithOptions(
                              decimal: true,
                            ),
                            readOnly: readOnly,
                          ),
                          shared.AppTextField(
                            label: 'Remarks',
                            controller: draft.remarks,
                            maxLines: 2,
                            readOnly: readOnly,
                          ),
                        ],
                      ),
                    ),
                  ),
                if (record.completed)
                  Text('Overall decision: ${_label(record.decision)}')
                else
                  DropdownButtonFormField<String>(
                    initialValue: _decision,
                    decoration: const InputDecoration(
                      labelText: 'Overall decision',
                    ),
                    items: const ['Accepted', 'PartiallyAccepted', 'Rejected']
                        .map(
                          (value) => DropdownMenuItem(
                            value: value,
                            child: Text(_label(value)),
                          ),
                        )
                        .toList(),
                    onChanged: readOnly
                        ? null
                        : (value) => setState(() => _decision = value),
                  ),
                shared.AppTextField(
                  label: 'Inspection notes',
                  controller: _notes,
                  maxLines: 3,
                  readOnly: readOnly,
                ),
                const SizedBox(height: 16),
                if (record.editable)
                  shared.AppButton(
                    label: _busy ? 'Saving...' : 'Complete Inspection',
                    onPressed: readOnly ? null : _complete,
                    expand: true,
                  ),
                if (record.completed)
                  shared.AppButton(
                    label: 'Done',
                    onPressed: () => Navigator.pop(context, true),
                    expand: true,
                  ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  static String _label(String? value) => value == 'PartiallyAccepted'
      ? 'Partially Accepted'
      : value ?? 'Not recorded';
}

class _ItemDraft {
  _ItemDraft(this.item)
    : condition = TextEditingController(text: item.condition),
      accepted = TextEditingController(text: item.accepted?.toStringAsFixed(2)),
      rejected = TextEditingController(text: item.rejected?.toStringAsFixed(2)),
      remarks = TextEditingController(text: item.remarks);
  final InspectionRecordItem item;
  final TextEditingController condition, accepted, rejected, remarks;
  InspectionItemSubmission submission() {
    double quantity(TextEditingController controller) {
      final text = controller.text.trim();
      final value = double.tryParse(text);
      if (!RegExp(r'^\d+(\.\d{1,2})?$').hasMatch(text) ||
          value == null ||
          !value.isFinite ||
          value > 9999999999.99) {
        throw FormatException(
          'Item #${item.id}: enter nonnegative quantities with at most two decimal places (maximum 9999999999.99).',
        );
      }
      return value;
    }

    if (condition.text.trim().length > 100) {
      throw FormatException(
        'Item #${item.id}: condition must not exceed 100 characters.',
      );
    }
    return InspectionItemSubmission(
      deliveryItemId: item.id,
      acceptedQuantity: quantity(accepted),
      rejectedQuantity: quantity(rejected),
      condition: condition.text.trim(),
      remarks: remarks.text.trim(),
    );
  }

  void dispose() {
    condition.dispose();
    accepted.dispose();
    rejected.dispose();
    remarks.dispose();
  }
}
