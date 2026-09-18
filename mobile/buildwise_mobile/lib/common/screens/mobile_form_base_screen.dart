import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';
import '../../core/widgets/widgets.dart';

class MobileFormBaseScreen extends StatefulWidget {
  const MobileFormBaseScreen({super.key});
  @override
  State<MobileFormBaseScreen> createState() => _MobileFormBaseScreenState();
}

class _MobileFormBaseScreenState extends State<MobileFormBaseScreen> {
  String? category;
  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(16),
    children: [
      Text('Create item', style: Theme.of(context).textTheme.headlineSmall),
      const SizedBox(height: 4),
      const Text(
        'Reusable neutral form template.',
        style: TextStyle(color: AppColors.textMuted),
      ),
      const SizedBox(height: 20),
      AppCard(
        child: Column(
          children: [
            const AppTextField(label: 'Item name', hint: 'Enter a clear name'),
            const SizedBox(height: 16),
            AppDropdown(
              label: 'Category',
              items: const ['General', 'Priority', 'Other'],
              value: category,
              onChanged: (value) => setState(() => category = value),
            ),
            const SizedBox(height: 16),
            const AppTextField(
              label: 'Quantity',
              hint: '0',
              keyboardType: TextInputType.number,
            ),
            const SizedBox(height: 16),
            const AppTextField(
              label: 'Target date',
              hint: 'DD / MM / YYYY',
              readOnly: true,
              suffixIcon: Icon(Icons.calendar_today_outlined),
            ),
            const SizedBox(height: 16),
            const AppTextField(
              label: 'Reference code',
              hint: 'ITEM-000',
              errorText: 'Use the format ITEM-000.',
            ),
            const SizedBox(height: 16),
            const AppTextField(
              label: 'Notes',
              hint: 'Add useful context',
              maxLines: 4,
            ),
            const SizedBox(height: 20),
            Row(
              children: [
                Expanded(
                  child: AppButton(
                    label: 'Cancel',
                    variant: AppButtonVariant.secondary,
                    onPressed: () {},
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: AppButton(label: 'Save item', onPressed: () {}),
                ),
              ],
            ),
          ],
        ),
      ),
    ],
  );
}
