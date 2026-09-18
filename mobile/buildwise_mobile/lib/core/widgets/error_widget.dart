import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import 'app_button.dart';

class ErrorWidget extends StatelessWidget {
  const ErrorWidget({
    super.key,
    this.title = 'Something went wrong',
    this.message = 'Please try again.',
    this.onRetry,
  });
  final String title;
  final String message;
  final VoidCallback? onRetry;
  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(28),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.error_outline, size: 48, color: AppColors.danger),
          const SizedBox(height: 12),
          Text(title, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 6),
          Text(
            message,
            textAlign: TextAlign.center,
            style: const TextStyle(color: AppColors.textMuted),
          ),
          if (onRetry != null) ...[
            const SizedBox(height: 16),
            AppButton(
              label: 'Try again',
              variant: AppButtonVariant.secondary,
              onPressed: onRetry,
            ),
          ],
        ],
      ),
    ),
  );
}
