import 'package:flutter/material.dart';

import '../theme/app_colors.dart';

enum StatusTone { success, warning, danger, info, neutral }

class StatusChip extends StatelessWidget {
  const StatusChip({
    super.key,
    required this.label,
    this.tone = StatusTone.neutral,
  });
  final String label;
  final StatusTone tone;

  @override
  Widget build(BuildContext context) {
    final colors = switch (tone) {
      StatusTone.success => (AppColors.success, const Color(0xFFDCFCE7)),
      StatusTone.warning => (AppColors.warning, const Color(0xFFFEF3C7)),
      StatusTone.danger => (AppColors.danger, const Color(0xFFFEE2E2)),
      StatusTone.info => (const Color(0xFF1D4ED8), const Color(0xFFDBEAFE)),
      StatusTone.neutral => (AppColors.textMuted, const Color(0xFFEEF2F6)),
    };
    return Chip(
      label: Text(label),
      labelStyle: TextStyle(
        color: colors.$1,
        fontSize: 12,
        fontWeight: FontWeight.w700,
      ),
      backgroundColor: colors.$2,
      side: BorderSide.none,
      visualDensity: VisualDensity.compact,
      padding: const EdgeInsets.symmetric(horizontal: 4),
    );
  }
}
