import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart' as shared;
import '../models/pending_inspection_delivery.dart';
import '../services/quality_api_service.dart';

class PendingInspectionsScreen extends StatefulWidget {
  const PendingInspectionsScreen({super.key, this.service});

  /// Injected services belong to the caller; otherwise this screen owns one.
  final QualityApiService? service;

  @override
  State<PendingInspectionsScreen> createState() =>
      _PendingInspectionsScreenState();
}

class _PendingInspectionsScreenState extends State<PendingInspectionsScreen> {
  QualityApiService? _service;
  bool _loading = true;
  String? _error;
  List<PendingInspectionDelivery> _deliveries = [];

  @override
  void initState() {
    super.initState();
    _service = widget.service;
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      _service ??= QualityApiService();
      final deliveries = await _service!.getPendingDeliveries();
      if (!mounted) return;
      setState(() => _deliveries = deliveries);
    } on QualityApiException catch (error) {
      if (!mounted) return;
      setState(() => _error = error.message);
    } on ArgumentError {
      // A missing API URL should not crash the existing UI preview.
      if (!mounted) return;
      setState(
        () => _error = 'The quality service is not configured. Contact the app administrator.',
      );
    } catch (_) {
      if (!mounted) return;
      setState(
        () => _error = 'Unable to load pending inspections. Please try again.',
      );
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  void dispose() {
    if (widget.service == null) _service?.close();
    super.dispose();
  }

  // Presentation only: never filter eligibility here. The API owns that rule.
  String _statusLabel(Object status) => switch (status) {
    0 || 'Scheduled' => 'Scheduled',
    1 || 'InTransit' => 'In Transit',
    2 || 'Arrived' => 'Arrived',
    3 || 'ReceivingInProgress' => 'Receiving In Progress',
    4 || 'Received' => 'Received',
    5 || 'PartiallyReceived' => 'Partially Received',
    6 || 'DiscrepancyReported' => 'Discrepancy Reported',
    _ => 'Unknown status ($status)',
  };

  String _reference(PendingInspectionDelivery delivery) {
    final reference = delivery.deliveryReference?.trim();
    return reference == null || reference.isEmpty
        ? 'Delivery #${delivery.deliveryId}'
        : reference;
  }

  void _selectDelivery(PendingInspectionDelivery delivery) {
    showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Delivery selected'),
        content: Text(
          '${_reference(delivery)} is selected. Inspection entry will be available in a later step. No inspection has been started.',
        ),
        actions: [
          shared.AppButton(
            label: 'Back to deliveries',
            onPressed: () => Navigator.pop(context),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Pending Inspections'),
      actions: [
        IconButton(
          tooltip: 'Refresh deliveries',
          onPressed: _loading ? null : _load,
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: SafeArea(child: _body()),
  );

  Widget _body() {
    if (_loading) {
      return const shared.LoadingWidget(
        message: 'Loading pending inspections...',
      );
    }
    if (_error != null) {
      return shared.ErrorWidget(
        title: 'Unable to load inspections',
        message: _error!,
        onRetry: _load,
      );
    }
    if (_deliveries.isEmpty) {
      return shared.EmptyStateWidget(
        title: 'No deliveries ready for inspection',
        message: 'Deliveries will appear here when they are ready for quality inspection.',
        actionLabel: 'Refresh',
        onAction: _load,
      );
    }
    return ListView.separated(
      padding: const EdgeInsets.all(16),
      itemCount: _deliveries.length,
      separatorBuilder: (_, index) => const SizedBox(height: 12),
      itemBuilder: (context, index) {
        final delivery = _deliveries[index];
        return shared.AppCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                _reference(delivery),
                style: Theme.of(context).textTheme.titleMedium,
              ),
              const SizedBox(height: 8),
              shared.StatusChip(label: _statusLabel(delivery.status)),
              const SizedBox(height: 8),
              Text(
                '${delivery.items.length} ${delivery.items.length == 1 ? 'item' : 'items'}',
              ),
              const SizedBox(height: 8),
              for (final item in delivery.items)
                Padding(
                  padding: const EdgeInsets.only(bottom: 4),
                  child: Text(
                    'Item #${item.deliveryItemId}: received ${item.receivedQuantity.toStringAsFixed(2)}',
                  ),
                ),
              const SizedBox(height: 12),
              shared.AppButton(
                label: 'Start Inspection',
                expand: true,
                onPressed: () => _selectDelivery(delivery),
              ),
            ],
          ),
        );
      },
    );
  }
}
