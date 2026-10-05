import 'package:buildwise_mobile/features/operations/screens/material_requests_screen.dart';
import 'package:buildwise_mobile/features/operations/services/operations_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

class ScopeService extends OperationsService {
  final calls = <String>[];
  @override
  Future<List<Map<String, dynamic>>> listMyRequests() async {
    calls.add('mine');
    return [];
  }

  @override
  Future<List<Map<String, dynamic>>> listRequests({String? status}) async {
    calls.add('register:$status');
    return [];
  }
}

void main() {
  for (final ownRequestsOnly in [true, false]) {
    testWidgets(
      ownRequestsOnly ? 'Site Officer reads own requests without creation' : 'Procurement Officer reads complete request register without creation',
      (tester) async {
        final service = ScopeService();
        await tester.pumpWidget(
          MaterialApp(
            home: MaterialRequestsScreen(
              service: service,
              readOnly: true,
              ownRequestsOnly: ownRequestsOnly,
              autoRefresh: false,
            ),
          ),
        );
        await tester.pumpAndSettle();
        expect(service.calls, [ownRequestsOnly ? 'mine' : 'register:all']);
        expect(find.byType(FloatingActionButton), findsNothing);
        expect(tester.takeException(), isNull);
      },
    );
  }
}
