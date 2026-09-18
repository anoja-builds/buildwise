import 'package:flutter/material.dart' hide ErrorWidget;

import '../../core/widgets/widgets.dart' as app;

class MobileUiStatesScreen extends StatelessWidget {
  const MobileUiStatesScreen({super.key});
  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(16),
    children: [
      Text('UI states', style: Theme.of(context).textTheme.headlineSmall),
      const SizedBox(height: 16),
      const app.AppCard(
        child: app.LoadingWidget(message: 'Loading information…'),
      ),
      const SizedBox(height: 12),
      const app.AppCard(
        child: app.EmptyStateWidget(actionLabel: 'Create first item'),
      ),
      const SizedBox(height: 12),
      app.AppCard(child: app.ErrorWidget(onRetry: _noop)),
      const SizedBox(height: 12),
      app.AppCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Status styles',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 12),
            const Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                app.StatusChip(label: 'Success', tone: app.StatusTone.success),
                app.StatusChip(label: 'Warning', tone: app.StatusTone.warning),
                app.StatusChip(label: 'Rejected', tone: app.StatusTone.danger),
                app.StatusChip(label: 'In progress', tone: app.StatusTone.info),
              ],
            ),
          ],
        ),
      ),
    ],
  );
  static void _noop() {}
}
