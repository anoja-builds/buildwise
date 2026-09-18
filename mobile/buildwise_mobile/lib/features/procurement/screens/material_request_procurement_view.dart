import 'package:flutter/material.dart' hide ErrorWidget;

import '../../../core/widgets/widgets.dart';
import '../services/notification_service.dart';
import '../services/procurement_status_service.dart';
import '../widgets/procurement_status_widget.dart';

/// Embeds the procurement status section into a Site Engineer's material
/// request detail screen (spec §9.1). This is a self-contained card, meant
/// to be dropped into that request's detail view once Component 1 builds it.
///
/// Also demonstrates spec §9.2's "push notification handling": each refresh
/// compares the status against the last known one and fires a local
/// notification on a real transition ("awaiting approval" / "PO created").
class MaterialRequestProcurementView extends StatefulWidget {
  const MaterialRequestProcurementView({
    super.key,
    required this.materialRequestId,
    ProcurementStatusService? service,
    NotificationService? notificationService,
  }) : _service = service,
       _notificationService = notificationService;

  final int materialRequestId;
  final ProcurementStatusService? _service;
  final NotificationService? _notificationService;

  @override
  State<MaterialRequestProcurementView> createState() =>
      _MaterialRequestProcurementViewState();
}

class _MaterialRequestProcurementViewState
    extends State<MaterialRequestProcurementView> {
  late final ProcurementStatusService _service =
      widget._service ?? ProcurementStatusService();
  late final NotificationService _notifications =
      widget._notificationService ?? NotificationService.instance;
  late Future<ProcurementStatusInfo> _future;
  ProcurementStatus? _lastNotifiedStatus;

  @override
  void initState() {
    super.initState();
    _future = _load(notifyOnChange: false);
  }

  Future<ProcurementStatusInfo> _load({required bool notifyOnChange}) async {
    final info = await _service.getStatus(widget.materialRequestId);

    if (notifyOnChange && _lastNotifiedStatus != null && _lastNotifiedStatus != info.status) {
      await _notifyStatusChange(info);
    }
    _lastNotifiedStatus = info.status;

    return info;
  }

  Future<void> _notifyStatusChange(ProcurementStatusInfo info) async {
    switch (info.status) {
      case ProcurementStatus.awaitingApproval:
        await _notifications.showProcurementUpdate(
          id: widget.materialRequestId,
          title: 'Procurement recommendation awaiting approval',
          body: 'Request #${info.materialRequestId} has a recommendation ready for manager review.',
        );
      case ProcurementStatus.purchaseOrderCreated:
        await _notifications.showProcurementUpdate(
          id: widget.materialRequestId,
          title: 'Purchase Order created',
          body: 'Request #${info.materialRequestId} — PO #${info.purchaseOrderId} has been created.',
        );
      default:
        break;
    }
  }

  void _refresh() {
    setState(() => _future = _load(notifyOnChange: true));
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      FutureBuilder<ProcurementStatusInfo>(
        future: _future,
        builder: (context, snapshot) {
          if (snapshot.connectionState == ConnectionState.waiting) {
            return const LoadingWidget(message: 'Checking procurement status…');
          }
          if (snapshot.hasError) {
            return ErrorWidget(
              title: 'Could not load procurement status',
              message: snapshot.error.toString(),
              onRetry: _refresh,
            );
          }
          return ProcurementStatusWidget(info: snapshot.data!);
        },
      ),
      const SizedBox(height: 8),
      Align(
        alignment: Alignment.centerRight,
        child: TextButton.icon(
          onPressed: _refresh,
          icon: const Icon(Icons.refresh, size: 18),
          label: const Text('Refresh status'),
        ),
      ),
    ],
  );
}
