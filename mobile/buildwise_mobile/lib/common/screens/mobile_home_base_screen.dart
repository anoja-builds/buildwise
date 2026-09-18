import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';
import '../../core/widgets/widgets.dart';

class MobileHomeBaseScreen extends StatelessWidget {
  const MobileHomeBaseScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final summaries = [
      ('Pending items', '12', Icons.schedule_outlined, AppColors.warning),
      ('Active workflows', '8', Icons.route_outlined, AppColors.primary),
      ('Needs attention', '3', Icons.warning_amber_outlined, AppColors.danger),
      ('Completed today', '19', Icons.check_circle_outline, AppColors.success),
    ];
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Text(
          'Good morning, Jordan',
          style: Theme.of(context).textTheme.headlineSmall,
        ),
        const SizedBox(height: 4),
        const Text(
          'Here is a general workspace overview.',
          style: TextStyle(color: AppColors.textMuted),
        ),
        const SizedBox(height: 20),
        GridView.builder(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: 2,
            crossAxisSpacing: 12,
            mainAxisSpacing: 12,
            childAspectRatio: 1.45,
          ),
          itemCount: summaries.length,
          itemBuilder: (context, index) {
            final item = summaries[index];
            return AppCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(item.$3, color: item.$4),
                  const Spacer(),
                  Text(
                    item.$2,
                    style: Theme.of(context).textTheme.headlineSmall,
                  ),
                  Text(
                    item.$1,
                    style: const TextStyle(color: AppColors.textMuted),
                  ),
                ],
              ),
            );
          },
        ),
        const SizedBox(height: 24),
        const SectionHeader(title: 'Quick actions'),
        const SizedBox(height: 8),
        Row(
          children: [
            Expanded(
              child: AppCard(
                onTap: _noop,
                child: const Column(
                  children: [
                    Icon(Icons.add_box_outlined, color: AppColors.primary),
                    SizedBox(height: 8),
                    Text('Create item', textAlign: TextAlign.center),
                  ],
                ),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: AppCard(
                onTap: _noop,
                child: const Column(
                  children: [
                    Icon(Icons.search_outlined, color: AppColors.primary),
                    SizedBox(height: 8),
                    Text('Find record', textAlign: TextAlign.center),
                  ],
                ),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: AppCard(
                onTap: _noop,
                child: const Column(
                  children: [
                    Icon(Icons.bar_chart_outlined, color: AppColors.primary),
                    SizedBox(height: 8),
                    Text('View report', textAlign: TextAlign.center),
                  ],
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 24),
        const SectionHeader(title: 'Recent activity', actionLabel: 'View all'),
        const SizedBox(height: 8),
        AppCard(
          child: Column(
            children: const [
              _ActivityRow(title: 'A new item was created', time: '1 hour ago'),
              Divider(height: 24),
              _ActivityRow(
                title: 'A record moved to review',
                time: '2 hours ago',
              ),
              Divider(height: 24),
              _ActivityRow(title: 'Team notes were updated', time: 'Yesterday'),
            ],
          ),
        ),
      ],
    );
  }

  static void _noop() {}
}

class _ActivityRow extends StatelessWidget {
  const _ActivityRow({required this.title, required this.time});
  final String title;
  final String time;
  @override
  Widget build(BuildContext context) => Row(
    children: [
      const CircleAvatar(
        radius: 17,
        backgroundColor: Color(0xFFFFF3D6),
        child: Icon(Icons.history, size: 17, color: AppColors.accent),
      ),
      const SizedBox(width: 12),
      Expanded(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title),
            const SizedBox(height: 3),
            Text(
              time,
              style: const TextStyle(fontSize: 12, color: AppColors.textMuted),
            ),
          ],
        ),
      ),
    ],
  );
}
