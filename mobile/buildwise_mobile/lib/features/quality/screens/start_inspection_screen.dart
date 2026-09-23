import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart' as shared;
import '../models/pending_inspection_delivery.dart';
import '../services/quality_api_service.dart';

class StartInspectionScreen extends StatefulWidget {
  const StartInspectionScreen({
    super.key,
    required this.delivery,
    required this.service,
  });
  final PendingInspectionDelivery delivery;
  // Borrow the pending screen's service; that screen owns its lifetime.
  final QualityApiService service;

  @override
  State<StartInspectionScreen> createState() => _StartInspectionScreenState();
}

class _StartInspectionScreenState extends State<StartInspectionScreen> {
  final _notes = TextEditingController();
  bool _submitting = false;
  String? _error;
  Map<String, dynamic>? _created;

  Future<void> _submit() async {
    if (_submitting || _created != null) return;
    FocusScope.of(context).unfocus();
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      final notes = _notes.text.trim();
      final created = await widget.service.startInspection(
        deliveryId: widget.delivery.deliveryId,
        notes: notes.isEmpty ? null : notes,
      );
      if (mounted) setState(() => _created = created);
    } on QualityApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } catch (_) {
      if (mounted) {
        setState(
          () => _error = 'Unable to start the inspection. Please try again.',
        );
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  void dispose() {
    _notes.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final delivery = widget.delivery;
    final created = _created;
    return PopScope(
      canPop: !_submitting,
      child: Scaffold(
        appBar: AppBar(
          title: Text(
            created == null ? 'Start Inspection' : 'Inspection Started',
          ),
          automaticallyImplyLeading: !_submitting,
        ),
        body: SafeArea(
          child: ListView(
            padding: const EdgeInsets.all(16),
            children: [
              shared.AppCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      delivery.displayReference,
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    Text('Delivery #${delivery.deliveryId}'),
                    shared.StatusChip(label: delivery.statusLabel),
                    Text(
                      '${delivery.items.length} ${delivery.items.length == 1 ? 'item' : 'items'}',
                    ),
                    for (final item in delivery.items)
                      Padding(
                        padding: const EdgeInsets.only(top: 8),
                        child: Text(
                          'Item #${item.deliveryItemId} (order item #${item.purchaseOrderItemId}): received ${item.receivedQuantity.toStringAsFixed(2)}',
                        ),
                      ),
                  ],
                ),
              ),
              const SizedBox(height: 16),
              if (created != null) ...[
                Semantics(
                  liveRegion: true,
                  child: Text(
                    'Inspection #${created['id']} started successfully.',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                const SizedBox(height: 8),
                shared.StatusChip(
                  label: switch (created['status']) {
                    'UnderInspection' || 1 => 'Under Inspection',
                    final value => '$value',
                  },
                  tone: shared.StatusTone.info,
                ),
                const SizedBox(height: 16),
                shared.AppButton(
                  label: 'Back to pending inspections',
                  onPressed: () => Navigator.pop(context),
                  expand: true,
                ),
              ] else ...[
                const Text(
                  'Your signed-in account will be recorded as the inspector. Add any initial notes before starting.',
                ),
                const SizedBox(height: 16),
                shared.AppTextField(
                  label: 'Notes (optional)',
                  controller: _notes,
                  maxLines: 4,
                  readOnly: _submitting,
                ),
                if (_error != null) ...[
                  const SizedBox(height: 16),
                  Semantics(
                    liveRegion: true,
                    child: Text(
                      _error!,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ),
                ],
                const SizedBox(height: 16),
                if (_submitting)
                  const Padding(
                    padding: EdgeInsets.only(bottom: 16),
                    child: LinearProgressIndicator(
                      semanticsLabel: 'Starting inspection',
                    ),
                  ),
                shared.AppButton(
                  label: _submitting
                      ? 'Starting...'
                      : 'Confirm Start Inspection',
                  onPressed: _submitting ? null : _submit,
                  expand: true,
                ),
                const SizedBox(height: 8),
                shared.AppButton(
                  label: 'Cancel',
                  variant: shared.AppButtonVariant.secondary,
                  onPressed: _submitting ? null : () => Navigator.pop(context),
                  expand: true,
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
