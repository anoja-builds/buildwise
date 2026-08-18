import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';
import '../../core/widgets/widgets.dart';

class MobileListBaseScreen extends StatefulWidget {
  const MobileListBaseScreen({super.key});
  @override
  State<MobileListBaseScreen> createState() => _MobileListBaseScreenState();
}

class _MobileListBaseScreenState extends State<MobileListBaseScreen> {
  String? filter = 'All statuses';
  @override
  Widget build(BuildContext context) {
    final items = [
      ('ITEM-001', 'Example workspace item', 'In progress', StatusTone.info),
      ('ITEM-002', 'General review item', 'Pending', StatusTone.warning),
      ('ITEM-003', 'Completed sample item', 'Completed', StatusTone.success),
    ];
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Text('Items', style: Theme.of(context).textTheme.headlineSmall),
        const SizedBox(height: 4),
        const Text(
          'Reusable mobile list template.',
          style: TextStyle(color: AppColors.textMuted),
        ),
        const SizedBox(height: 16),
        const AppTextField(
          label: 'Search',
          hint: 'Search items…',
          suffixIcon: Icon(Icons.search),
        ),
        const SizedBox(height: 12),
        AppDropdown(
          label: 'Filter',
          items: const ['All statuses', 'Pending', 'In progress', 'Completed'],
          value: filter,
          onChanged: (value) => setState(() => filter = value),
        ),
        const SizedBox(height: 18),
        ...items.map(
          (item) => Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: AppCard(
              onTap: () {},
              child: Row(
                children: [
                  Container(
                    width: 44,
                    height: 44,
                    decoration: BoxDecoration(
                      color: const Color(0xFFE8F0F8),
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: const Icon(
                      Icons.description_outlined,
                      color: AppColors.primary,
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          item.$1,
                          style: const TextStyle(fontWeight: FontWeight.w700),
                        ),
                        const SizedBox(height: 4),
                        Text(
                          item.$2,
                          style: const TextStyle(color: AppColors.textMuted),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 8),
                  StatusChip(label: item.$3, tone: item.$4),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}
