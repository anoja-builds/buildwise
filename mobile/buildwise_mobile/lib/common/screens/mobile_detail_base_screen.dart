import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';
import '../../core/widgets/widgets.dart';

class MobileDetailBaseScreen extends StatelessWidget {
  const MobileDetailBaseScreen({super.key});
  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(16),
    children: [
      const Text(
        'ITEM-001',
        style: TextStyle(
          color: AppColors.textMuted,
          fontWeight: FontWeight.w600,
        ),
      ),
      const SizedBox(height: 4),
      Text(
        'Example workspace item',
        style: Theme.of(context).textTheme.headlineSmall,
      ),
      const SizedBox(height: 10),
      const Align(
        alignment: Alignment.centerLeft,
        child: StatusChip(label: 'Pending review', tone: StatusTone.warning),
      ),
      const SizedBox(height: 18),
      AppCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Information', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            ...[
              ('Reference', 'ITEM-001'),
              ('Category', 'General'),
              ('Owner', 'Jordan Doe'),
              ('Created', '18 August 2026'),
            ].map((row) => _DetailRow(label: row.$1, value: row.$2)),
          ],
        ),
      ),
      const SizedBox(height: 16),
      AppCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Notes', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 10),
            const Text(
              'This neutral placeholder demonstrates how longer supporting information can be shown.',
              style: TextStyle(color: AppColors.textMuted),
            ),
          ],
        ),
      ),
      const SizedBox(height: 16),
      AppCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Activity', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 12),
            const ListTile(
              contentPadding: EdgeInsets.zero,
              leading: Icon(Icons.history, color: AppColors.accent),
              title: Text('Item created'),
              subtitle: Text('Today at 9:30 AM'),
            ),
            const Divider(),
            const ListTile(
              contentPadding: EdgeInsets.zero,
              leading: Icon(Icons.history, color: AppColors.accent),
              title: Text('Moved to pending review'),
              subtitle: Text('Today at 10:15 AM'),
            ),
          ],
        ),
      ),
      const SizedBox(height: 20),
      AppButton(label: 'Primary action', expand: true, onPressed: () {}),
    ],
  );
}

class _DetailRow extends StatelessWidget {
  const _DetailRow({required this.label, required this.value});
  final String label;
  final String value;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 11),
    child: Row(
      children: [
        Expanded(
          child: Text(
            label,
            style: const TextStyle(color: AppColors.textMuted),
          ),
        ),
        Expanded(
          child: Text(
            value,
            textAlign: TextAlign.end,
            style: const TextStyle(fontWeight: FontWeight.w600),
          ),
        ),
      ],
    ),
  );
}
