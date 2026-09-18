import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import 'app_button.dart';

class EmptyStateWidget extends StatelessWidget {
  const EmptyStateWidget({
    super.key,
    this.title = 'Nothing here yet',
    this.message = 'New items will appear here.',
    this.actionLabel,
    this.onAction,
  });
  final String title;
  final String message;
  final String? actionLabel;
  final VoidCallback? onAction;
  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(28),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.inbox_outlined, size: 48, color: AppColors.primary),
          const SizedBox(height: 12),
          Text(title, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 6),
          Text(
            message,
            textAlign: TextAlign.center,
            style: const TextStyle(color: AppColors.textMuted),
          ),
          if (actionLabel != null) ...[
            const SizedBox(height: 16),
            AppButton(
              label: actionLabel!,
              variant: AppButtonVariant.secondary,
              onPressed: onAction,
            ),
          ],
        ],
      ),
    ),
  );
}
