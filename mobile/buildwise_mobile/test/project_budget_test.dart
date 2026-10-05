import 'package:buildwise_mobile/features/procurement/services/procurement_service.dart';
import 'package:buildwise_mobile/features/procurement/widgets/project_budget_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

class BudgetService extends ProcurementService {
  final saved = <double?>[];
  @override
  Future<Map<String, dynamic>> getProjectBudget(int projectId) async => {
    'projectName': 'Riverside Apartments',
    'materialBudgetAmount': 5000,
  };
  @override
  Future<Map<String, dynamic>> updateProjectBudget(
    int projectId,
    double? amount,
  ) async {
    saved.add(amount);
    return {'materialBudgetAmount': amount};
  }
}

void main() {
  testWidgets('officer sees project budget without editing controls', (
    tester,
  ) async {
    final service = BudgetService();
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ProjectBudgetCard(
            projectId: 3,
            service: service,
            canEdit: false,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Project: Riverside Apartments'), findsOneWidget);
    expect(tester.widget<TextField>(find.byType(TextField)).enabled, isFalse);
    expect(find.text('Save budget'), findsNothing);
    expect(service.saved, isEmpty);
  });
  testWidgets(
    'manager rejects negative budgets and can save or clear allocation',
    (tester) async {
      final service = BudgetService();
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ProjectBudgetCard(
              projectId: 3,
              service: service,
              canEdit: true,
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), '-1');
      await tester.tap(find.text('Save budget'));
      await tester.pumpAndSettle();
      expect(service.saved, isEmpty);
      expect(
        find.text('Enter a non-negative amount, or leave blank for no budget.'),
        findsOneWidget,
      );
      await tester.enterText(find.byType(TextField), '1200.50');
      await tester.tap(find.text('Save budget'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), '');
      await tester.tap(find.text('Save budget'));
      await tester.pumpAndSettle();
      expect(service.saved, [1200.5, null]);
      expect(find.text('Budget saved.'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );
}
