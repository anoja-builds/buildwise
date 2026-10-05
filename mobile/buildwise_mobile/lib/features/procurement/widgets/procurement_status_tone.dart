import '../../../core/widgets/status_chip.dart';

/// Status → badge tone, mirroring
/// `web/buildwise-web/src/Features/procurement/components/statusTone.js`.
///
/// Both clients read the same table so a status is never one colour on the web
/// and another on mobile. An unknown status falls back to neutral, so a new
/// backend status degrades to an unremarkable chip rather than a crash.
StatusTone procurementStatusTone(String? status) => switch (status) {
      'Active' => StatusTone.success,
      'Inactive' => StatusTone.neutral,
      'Suspended' => StatusTone.danger,
      'Submitted' => StatusTone.info,
      'UnderReview' => StatusTone.warning,
      'Selected' => StatusTone.success,
      'Rejected' => StatusTone.danger,
      'Expired' => StatusTone.neutral,
      'Created' => StatusTone.info,
      'Confirmed' => StatusTone.warning,
      'InProgress' => StatusTone.warning,
      'Completed' => StatusTone.success,
      'Cancelled' => StatusTone.danger,
      'Pending' => StatusTone.neutral,
      'Running' => StatusTone.info,
      'AwaitingApproval' => StatusTone.warning,
      'Failed' => StatusTone.danger,
      'Approved' => StatusTone.success,
      'RevisionRequested' => StatusTone.warning,
      'Draft' => StatusTone.neutral,
      'Issued' => StatusTone.warning,
      'Closed' => StatusTone.success,
      'Accepted' => StatusTone.success,
      'PartiallyAccepted' => StatusTone.warning,
      'DiscrepancyReported' => StatusTone.danger,
      _ => StatusTone.neutral,
    };
