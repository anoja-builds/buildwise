import 'package:flutter/material.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/widgets/widgets.dart';
import '../services/procurement_status_service.dart';

/// Site Engineer's read-only procurement status section (spec §9.1).
/// Deliberately shows only a status message — never supplier names,
/// prices, or quotation detail, which stay in the React/office tooling.
class ProcurementStatusWidget extends StatelessWidget {
  const ProcurementStatusWidget({super.key, required this.info});

  final ProcurementStatusInfo info;

  static const _copy = <ProcurementStatus, (String, String, IconData)>{
    ProcurementStatus.notStarted: (
      'Procurement not started',
      'Quotations have not been requested for this material request yet.',
      Icons.hourglass_empty,
    ),
    ProcurementStatus.quotationsInProgress: (
      'Quotations in progress',
      'The procurement team is collecting and reviewing supplier quotations.',
      Icons.request_quote_outlined,
    ),
    ProcurementStatus.awaitingApproval: (
      'Awaiting manager approval',
      'A recommendation is ready and is waiting on the Procurement Manager\'s decision.',
      Icons.fact_check_outlined,
    ),
    ProcurementStatus.purchaseOrderCreated: (
      'Purchase Order created',
      'The purchase order has been created and procurement is complete.',
      Icons.check_circle_outline,
    ),
    ProcurementStatus.rejected: (
      'Procurement recommendation rejected',
      'The Procurement Manager rejected the recommendation. New quotations may be requested.',
      Icons.cancel_outlined,
    ),
  };

  static const _tones = <ProcurementStatus, StatusTone>{
    ProcurementStatus.notStarted: StatusTone.neutral,
    ProcurementStatus.quotationsInProgress: StatusTone.info,
    ProcurementStatus.awaitingApproval: StatusTone.warning,
    ProcurementStatus.purchaseOrderCreated: StatusTone.success,
    ProcurementStatus.rejected: StatusTone.danger,
  };

  @override
  Widget build(BuildContext context) {
    final (title, message, icon) = _copy[info.status]!;
    final tone = _tones[info.status]!;
    final label = info.status == ProcurementStatus.purchaseOrderCreated &&
            info.purchaseOrderId != null
        ? 'Purchase Order Created (PO #${info.purchaseOrderId})'
        : title;

    return AppCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SectionHeader(title: 'Procurement status'),
          const SizedBox(height: 12),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(icon, color: AppColors.primary),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Align(
                      alignment: Alignment.centerLeft,
                      child: StatusChip(label: label, tone: tone),
                    ),
                    const SizedBox(height: 8),
                    Text(
                      message,
                      style: const TextStyle(color: AppColors.textMuted),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
