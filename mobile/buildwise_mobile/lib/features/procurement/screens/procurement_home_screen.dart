import 'package:flutter/material.dart';

import '../../../core/theme/app_colors.dart';
import 'material_request_procurement_view.dart';

/// Reachable home for Component 2's Flutter slice. Component 1's real
/// material-request list/detail screens don't exist yet, so this shows the
/// seeded demo request (see backend/BuildWise.Api/Data/DbSeeder.cs) — once
/// that list exists, [MaterialRequestProcurementView] drops straight into
/// its detail screen unchanged.
class ProcurementHomeScreen extends StatelessWidget {
  const ProcurementHomeScreen({super.key});

  static const _demoMaterialRequestId = 1;

  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(16),
    children: [
      const Text(
        'MATERIAL REQUEST #$_demoMaterialRequestId',
        style: TextStyle(color: AppColors.textMuted, fontWeight: FontWeight.w600),
      ),
      const SizedBox(height: 4),
      Text('Riverside Apartments — Block C', style: Theme.of(context).textTheme.headlineSmall),
      const SizedBox(height: 4),
      const Text('Foundation pour for Block C', style: TextStyle(color: AppColors.textMuted)),
      const SizedBox(height: 18),
      const MaterialRequestProcurementView(materialRequestId: _demoMaterialRequestId),
    ],
  );
}
